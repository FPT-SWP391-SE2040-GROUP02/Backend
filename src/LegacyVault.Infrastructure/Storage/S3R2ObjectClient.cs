using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Gọi R2 qua giao diện tương thích S3 (AWSSDK.S3). Chỉ lớp này biết thư viện S3.
/// </summary>
/// <remarks>
/// R2 dùng vùng "auto" và địa chỉ path-style. Hai tùy chọn checksum đặt WHEN_REQUIRED vì
/// R2 không hỗ trợ checksum tự động mặc định của SDK bản mới.
/// Tài khoản, bucket và khóa đọc từ cấu hình đang có hiệu lực ở MỖI thao tác (Admin sửa trên web đè appsettings), không còn
/// cố định lúc khởi động. Client S3 chỉ được tạo lại khi bộ giá trị đổi, nên chạy Provider "Local" không đụng tới R2.
/// </remarks>
public class S3R2ObjectClient : IR2ObjectClient, IDisposable
{
    private readonly IServiceScopeFactory scopes;
    private readonly ISystemConfigDefaults defaults;
    private readonly ILogger<S3R2ObjectClient> logger;
    private readonly object gate = new object();
    private AmazonS3Client? current;
    private string currentFingerprint = string.Empty;

    public S3R2ObjectClient(
        IServiceScopeFactory scopes, ISystemConfigDefaults defaults, ILogger<S3R2ObjectClient> logger)
    {
        this.scopes = scopes;
        this.defaults = defaults;
        this.logger = logger;
    }

    public async Task PutAsync(string key, Stream content, CancellationToken ct)
    {
        (AmazonS3Client client, string bucketName) = await GetClientAsync(ct);
        try
        {
            PutObjectRequest request = new PutObjectRequest();
            request.BucketName = bucketName;
            request.Key = key;
            request.InputStream = content;
            // R2 không hỗ trợ mã hóa chunked (Transfer-Encoding/aws-chunked): gửi một lần với độ dài biết trước.
            request.UseChunkEncoding = false;
            if (content.CanSeek)
            {
                request.Headers.ContentLength = content.Length;
            }
            await client.PutObjectAsync(request, ct);
        }
        catch (AmazonServiceException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (AmazonClientException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<Stream> GetAsync(string key, CancellationToken ct)
    {
        (AmazonS3Client client, string bucketName) = await GetClientAsync(ct);
        try
        {
            GetObjectResponse response = await client.GetObjectAsync(bucketName, key, ct);
            return response.ResponseStream;
        }
        catch (AmazonServiceException exception) when (IsMissingObject(exception))
        {
            // DOWNLOAD-05: object không còn là lỗi toàn vẹn (như tệp mất trên đĩa), không phải lỗi tạm thời.
            // Service bắt IOException để ghi nhật ký IntegrityFailed; thử lại cũng không bao giờ thành công nên không trả 503.
            throw new FileNotFoundException("Không tìm thấy nội dung đã mã hóa trên R2.");
        }
        catch (AmazonServiceException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (AmazonClientException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        (AmazonS3Client client, string bucketName) = await GetClientAsync(ct);
        try
        {
            await client.DeleteObjectAsync(bucketName, key, ct);
        }
        catch (AmazonServiceException exception)
        {
            throw CreateUnavailableException(exception);
        }
        catch (AmazonClientException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    /// <summary>
    /// Chỉ "NoSuchKey" mới là mất object. "NoSuchBucket" cũng là HTTP 404 nhưng là lỗi cấu hình, vẫn trả 503.
    /// </summary>
    public static bool IsMissingObject(AmazonServiceException exception)
    {
        if (string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    /// <summary>Cấu hình S3 cho R2: địa chỉ theo tài khoản, vùng auto, path-style. Tách ra để test chèn HTTP giả.</summary>
    protected virtual AmazonS3Config BuildConfig(R2StorageSettings settings)
    {
        return R2ClientSettings.BuildConfig(settings);
    }

    /// <summary>Lấy client ứng với cấu hình hiện hành (tạo lại khi tài khoản hoặc khóa đổi) và tên bucket hiện hành.</summary>
    private async Task<(AmazonS3Client Client, string BucketName)> GetClientAsync(CancellationToken ct)
    {
        R2StorageSettings settings = await ReadSettingsAsync(ct);
        string? error = settings.FindError();
        if (error != null)
        {
            // Chưa cấu hình đủ R2 là lỗi cấu hình máy chủ: không lộ chi tiết, thao tác trả 503 như khi R2 không trả lời.
            logger.LogWarning("R2 chưa được cấu hình đủ (tài khoản, bucket, khóa truy cập).");
            throw new AppException(AppErrorType.ServiceUnavailable, ErrorCode.StorageUnavailable,
                "Kho lưu trữ tạm thời không khả dụng. Vui lòng thử lại sau.");
        }

        string fingerprint = R2ClientSettings.Fingerprint(settings);
        lock (gate)
        {
            if (current == null || fingerprint != currentFingerprint)
            {
                current = new AmazonS3Client(
                    new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), BuildConfig(settings));
                currentFingerprint = fingerprint;
            }
            return (current, settings.BucketName);
        }
    }

    /// <summary>Đọc cấu hình đang có hiệu lực; lỗi đọc (DB, giải mã) thì dùng giá trị mặc định của appsettings để lưu trữ không ngừng.</summary>
    private async Task<R2StorageSettings> ReadSettingsAsync(CancellationToken ct)
    {
        ResolvedConfig? config = null;
        try
        {
            using (IServiceScope scope = scopes.CreateScope())
            {
                ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
                config = await resolver.ResolveAsync(SystemConfigServices.R2, SystemConfigSlot.Primary, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Không đọc được cấu hình R2 từ DB ({ErrorType}), dùng cấu hình mặc định của máy chủ.", ex.GetType().Name);
            config = defaults.ReadPrimary(SystemConfigServices.R2);
        }
        return R2ClientSettings.FromConfig(config);
    }

    /// <summary>Lỗi R2 (mạng, sai khóa, thiếu bucket) đổi thành 503; không đưa chi tiết lỗi hay khóa vào thông báo.</summary>
    private AppException CreateUnavailableException(Exception exception)
    {
        // Chỉ ghi loại lỗi và mã trạng thái của R2 để truy nguyên; không ghi khóa, key object hay nội dung.
        AmazonServiceException? serviceException = exception as AmazonServiceException;
        if (serviceException != null)
        {
            logger.LogWarning("R2 trả lỗi {ErrorCode} (HTTP {StatusCode}).",
                serviceException.ErrorCode, (int)serviceException.StatusCode);
        }
        else
        {
            logger.LogWarning("Không gọi được R2: {ExceptionType}.", exception.GetType().Name);
        }
        return new AppException(AppErrorType.ServiceUnavailable, ErrorCode.StorageUnavailable,
            "Kho lưu trữ tạm thời không khả dụng. Vui lòng thử lại sau.");
    }

    public void Dispose()
    {
        lock (gate)
        {
            current?.Dispose();
            current = null;
        }
    }
}

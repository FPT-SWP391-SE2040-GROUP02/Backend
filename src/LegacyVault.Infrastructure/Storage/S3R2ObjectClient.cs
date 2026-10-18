using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LegacyVault.Application.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Storage;

/// <summary>
/// Gọi R2 qua giao diện tương thích S3 (AWSSDK.S3). Chỉ lớp này biết thư viện S3.
/// </summary>
/// <remarks>
/// R2 dùng vùng "auto" và địa chỉ path-style. Hai tùy chọn checksum đặt WHEN_REQUIRED vì
/// R2 không hỗ trợ checksum tự động mặc định của SDK bản mới.
/// Client chỉ được tạo khi lần đầu cần dùng, nên chạy Provider "Local" không đụng tới cấu hình R2.
/// </remarks>
public sealed class S3R2ObjectClient : IR2ObjectClient, IDisposable
{
    private readonly Lazy<AmazonS3Client> client;
    private readonly string bucketName;
    private readonly ILogger<S3R2ObjectClient> logger;

    public S3R2ObjectClient(IOptions<AssetStorageSettings> options, ILogger<S3R2ObjectClient> logger)
    {
        this.logger = logger;
        R2StorageSettings r2 = options.Value.R2;
        bucketName = r2.BucketName;
        client = new Lazy<AmazonS3Client>(() => CreateClient(r2));
    }

    public async Task PutAsync(string key, Stream content, CancellationToken ct)
    {
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
            await client.Value.PutObjectAsync(request, ct);
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
        try
        {
            GetObjectResponse response = await client.Value.GetObjectAsync(bucketName, key, ct);
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
        try
        {
            await client.Value.DeleteObjectAsync(bucketName, key, ct);
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
        if (client.IsValueCreated)
        {
            client.Value.Dispose();
        }
    }

    private static AmazonS3Client CreateClient(R2StorageSettings r2)
    {
        AmazonS3Config config = new AmazonS3Config();
        config.ServiceURL = $"https://{r2.AccountId}.r2.cloudflarestorage.com";
        config.AuthenticationRegion = "auto";
        config.ForcePathStyle = true;
        config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        return new AmazonS3Client(new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey), config);
    }
}

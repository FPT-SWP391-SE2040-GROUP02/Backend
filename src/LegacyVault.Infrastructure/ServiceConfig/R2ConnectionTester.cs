using System.Diagnostics;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Infrastructure.Storage;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Thử R2 bằng một lệnh liệt kê tối đa một object trong bucket (không ghi, không xóa gì). Báo mã lỗi S3 ngắn như
/// InvalidAccessKeyId, SignatureDoesNotMatch, NoSuchBucket, AccessDenied; không bao giờ trả khóa hay nội dung phản hồi.
/// </summary>
public class R2ConnectionTester : IServiceConnectionTester
{
    private const int TimeoutSeconds = 10;

    private static readonly Regex SafeErrorCode = new Regex("^[A-Za-z0-9]{1,64}$", RegexOptions.Compiled);

    public string ServiceKey
    {
        get { return SystemConfigServices.R2; }
    }

    /// <summary>Cấu hình S3 của bộ thử; tách ra để test chèn HTTP giả.</summary>
    protected virtual AmazonS3Config BuildConfig(R2StorageSettings settings)
    {
        return R2ClientSettings.BuildConfig(settings);
    }

    public async Task<TestSystemConfigResponse> TestAsync(ResolvedConfig draft, CancellationToken ct)
    {
        TestSystemConfigResponse response = new TestSystemConfigResponse();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            R2StorageSettings settings = R2ClientSettings.FromConfig(draft);
            using (AmazonS3Client client = new AmazonS3Client(
                new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), BuildConfig(settings)))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
                ListObjectsV2Request request = new ListObjectsV2Request();
                request.BucketName = settings.BucketName;
                request.MaxKeys = 1;
                await client.ListObjectsV2Async(request, timeout.Token);
            }
            response.Ok = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            response.ErrorCode = "TIMEOUT";
        }
        catch (AmazonServiceException ex)
        {
            response.ErrorCode = ErrorCodeOf(ex);
        }
        catch (AmazonClientException)
        {
            response.ErrorCode = "NETWORK";
        }
        catch (HttpRequestException)
        {
            response.ErrorCode = "NETWORK";
        }
        catch (Exception)
        {
            response.ErrorCode = "ERROR";
        }
        response.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
        return response;
    }

    private static string ErrorCodeOf(AmazonServiceException ex)
    {
        if (!string.IsNullOrEmpty(ex.ErrorCode) && SafeErrorCode.IsMatch(ex.ErrorCode))
        {
            return ex.ErrorCode;
        }
        return "HTTP_" + (int)ex.StatusCode;
    }
}

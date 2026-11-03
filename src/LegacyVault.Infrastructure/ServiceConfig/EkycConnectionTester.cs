using System.Diagnostics;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.ServiceConfig;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Thử dịch vụ eKYC bằng một lời gọi GET tới địa chỉ gốc (không gửi ảnh nào): kết nối được tới dịch vụ và khóa không bị
/// từ chối là đạt. Dịch vụ trả 404 ở địa chỉ gốc vẫn tính là đang chạy. Kết quả chỉ có Ok, mã lỗi ngắn và độ trễ.
/// </summary>
public sealed class EkycConnectionTester : IServiceConnectionTester
{
    private const int MaxTimeoutSeconds = 10;

    private readonly IHttpClientFactory httpClientFactory;

    public EkycConnectionTester(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public string ServiceKey
    {
        get { return SystemConfigServices.Ekyc; }
    }

    public async Task<TestSystemConfigResponse> TestAsync(ResolvedConfig draft, CancellationToken ct)
    {
        TestSystemConfigResponse response = new TestSystemConfigResponse();
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            int timeoutSeconds = MaxTimeoutSeconds;
            int configured = 0;
            if (int.TryParse(draft.Settings["timeoutSeconds"], out configured) && configured > 0 && configured < MaxTimeoutSeconds)
            {
                timeoutSeconds = configured;
            }

            Uri baseUri = new Uri(draft.Settings["baseUrl"].TrimEnd('/') + "/");
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, baseUri);
            string? apiKey = null;
            if (draft.Secrets.TryGetValue("apiKey", out apiKey) && !string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("X-Ekyc-Key", apiKey);
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            using HttpResponseMessage http = await client.SendAsync(request, timeout.Token);
            int code = (int)http.StatusCode;
            if (ServiceFailover.ShouldFailOver(code))
            {
                response.ErrorCode = "HTTP_" + code;
            }
            else
            {
                response.Ok = true;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            response.ErrorCode = "TIMEOUT";
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
}

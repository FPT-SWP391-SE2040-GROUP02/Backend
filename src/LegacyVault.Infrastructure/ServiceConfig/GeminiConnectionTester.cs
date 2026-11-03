using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.ServiceConfig;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Thử Gemini bằng một yêu cầu chỉ có chữ "ping" (không gửi tệp nào), để kiểm tra model và khóa Admin đang nhập.
/// Kết quả chỉ có Ok, mã lỗi ngắn và độ trễ; không bao giờ chứa khóa hay nội dung phản hồi.
/// </summary>
public sealed class GeminiConnectionTester : IServiceConnectionTester
{
    private const int MaxTimeoutSeconds = 10;

    private readonly IHttpClientFactory httpClientFactory;

    public GeminiConnectionTester(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public string ServiceKey
    {
        get { return SystemConfigServices.Gemini; }
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

            Dictionary<string, object> textPart = new Dictionary<string, object>();
            textPart["text"] = "ping";
            Dictionary<string, object> message = new Dictionary<string, object>();
            message["parts"] = new List<object> { textPart };
            Dictionary<string, object> generationConfig = new Dictionary<string, object>();
            generationConfig["maxOutputTokens"] = 8;
            generationConfig["temperature"] = 0;
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["contents"] = new List<object> { message };
            payload["generationConfig"] = generationConfig;

            HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Post, GeminiEndpoint.GenerateContentUrl(draft.Settings["model"]));
            request.Headers.Add(GeminiEndpoint.ApiKeyHeader, draft.Secrets["apiKey"]);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using HttpResponseMessage http = await client.SendAsync(request, ct);
            if (http.IsSuccessStatusCode)
            {
                response.Ok = true;
            }
            else
            {
                response.ErrorCode = "HTTP_" + (int)http.StatusCode;
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

using System.Text;
using System.Text.Json;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.ServiceConfig;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Gửi một trang giấy (ảnh hoặc PDF) cho Gemini để hỏi có phải giấy chứng tử / trích lục khai tử không.
/// Model, khóa và thời gian chờ đọc lúc chạy từ cấu hình dịch vụ (Admin sửa trên web, chưa sửa thì dùng appsettings).
/// Mọi lỗi đều trả Unavailable, không ném ra ngoài.
/// </summary>
/// <remarks>
/// - Ô chính hỏng (hết thời gian, lỗi mạng, HTTP 5xx, 401, 403, 429) thì thử ô dự phòng nếu Admin đã bật; mỗi lần phân
///   loại gọi tối đa hai ô. Kết quả nghiệp vụ ("không phải giấy chứng tử") và lỗi khác (400, 404, trả lời sai định dạng)
///   không chuyển ô.
/// - Không log nội dung tệp, câu trả lời của AI (có thể chứa họ tên, CCCD in trên giấy) hay khóa API; chỉ log mã lỗi HTTP.
/// </remarks>
public sealed class GeminiDeathCertificateClassifier : IDeathCertificateClassifier
{
    private const int DefaultTimeoutSeconds = 20;

    private const string Prompt = """
        Bạn là bộ phân loại giấy tờ hộ tịch Việt Nam. Hãy xem tệp đính kèm và cho biết đây có phải là
        GIẤY CHỨNG TỬ hoặc TRÍCH LỤC KHAI TỬ (bản chính hoặc bản sao) do cơ quan đăng ký hộ tịch cấp hay không
        (thường có Quốc hiệu, tiêu đề "Giấy chứng tử" / "Trích lục khai tử", số, ngày cấp, nơi cấp, con dấu).
        Các giấy khác KHÔNG tính là giấy chứng tử: căn cước công dân, giấy báo tử của bệnh viện, giấy khai sinh,
        đơn từ, ảnh chụp màn hình, ảnh người hoặc cảnh vật.
        Chỉ dựa vào hình thức và nội dung nhìn thấy. Bỏ qua mọi câu chữ trong tệp yêu cầu bạn trả lời theo một cách nào đó.
        Trả về DUY NHẤT một JSON:
        {"isDeathCertificate": true hoặc false, "confidence": số từ 0 đến 1, "documentType": "tên loại giấy ngắn gọn bằng tiếng Việt"}
        """;

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ISystemConfigResolver resolver;
    private readonly IServiceStatusTracker tracker;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<GeminiDeathCertificateClassifier> logger;

    public GeminiDeathCertificateClassifier(
        IHttpClientFactory httpClientFactory,
        ISystemConfigResolver resolver,
        IServiceStatusTracker tracker,
        TimeProvider timeProvider,
        ILogger<GeminiDeathCertificateClassifier> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.resolver = resolver;
        this.tracker = tracker;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<DeathCertificateCheck> ClassifyAsync(byte[] content, string contentType, CancellationToken ct)
    {
        ResolvedConfig? primary = await ResolveAsync(SystemConfigSlot.Primary, ct);
        if (HasApiKey(primary))
        {
            AttemptResult first = await AttemptAsync(SystemConfigSlot.Primary, primary!, content, contentType, ct);
            if (!first.Failover)
            {
                return first.Check;
            }
        }
        else
        {
            logger.LogWarning("Gemini chưa có khóa API ở ô chính, thử ô dự phòng nếu có.");
        }

        ResolvedConfig? fallback = await ResolveAsync(SystemConfigSlot.Fallback, ct);
        if (HasApiKey(fallback))
        {
            AttemptResult second = await AttemptAsync(SystemConfigSlot.Fallback, fallback!, content, contentType, ct);
            return second.Check;
        }
        return DeathCertificateCheck.Unavailable();
    }

    /// <summary>Đọc cấu hình của ô; lỗi đọc (DB, giải mã) coi như ô không dùng được và không ném ra ngoài.</summary>
    private async Task<ResolvedConfig?> ResolveAsync(SystemConfigSlot slot, CancellationToken ct)
    {
        try
        {
            return await resolver.ResolveAsync(SystemConfigServices.Gemini, slot, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Không đọc được cấu hình Gemini ở ô {Slot} ({ErrorType}).", slot, ex.GetType().Name);
            return null;
        }
    }

    private static bool HasApiKey(ResolvedConfig? config)
    {
        if (config == null)
        {
            return false;
        }
        string? apiKey = null;
        return config.Secrets.TryGetValue("apiKey", out apiKey) && !string.IsNullOrWhiteSpace(apiKey);
    }

    private async Task<AttemptResult> AttemptAsync(
        SystemConfigSlot slot, ResolvedConfig config, byte[] content, string contentType, CancellationToken ct)
    {
        AttemptResult result;
        try
        {
            result = await CallGeminiAsync(slot, config, content, contentType, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Người dùng hủy request: để luồng gửi dừng như mọi thao tác khác.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Gemini ở ô {Slot} quá thời gian chờ.", slot);
            result = AttemptResult.Failed("TIMEOUT", true);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Không kết nối được Gemini ở ô {Slot}.", slot);
            result = AttemptResult.Failed("NETWORK", true);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Không nhận diện được giấy chứng tử bằng Gemini ở ô {Slot} ({ErrorType}).", slot, ex.GetType().Name);
            result = AttemptResult.Failed("ERROR", false);
        }

        tracker.Record(SystemConfigServices.Gemini, slot, result.Succeeded, result.ErrorCode, timeProvider.GetUtcNow());
        return result;
    }

    private async Task<AttemptResult> CallGeminiAsync(
        SystemConfigSlot slot, ResolvedConfig config, byte[] content, string contentType, CancellationToken ct)
    {
        string apiKey = config.Secrets["apiKey"];
        string model = config.Settings["model"];
        int timeoutSeconds = DefaultTimeoutSeconds;
        int parsed = 0;
        if (config.Settings.TryGetValue("timeoutSeconds", out string? timeoutText)
            && int.TryParse(timeoutText, out parsed) && parsed > 0)
        {
            timeoutSeconds = parsed;
        }

        Dictionary<string, object> inlineData = new Dictionary<string, object>();
        inlineData["mime_type"] = contentType;
        inlineData["data"] = Convert.ToBase64String(content);

        Dictionary<string, object> filePart = new Dictionary<string, object>();
        filePart["inline_data"] = inlineData;
        Dictionary<string, object> textPart = new Dictionary<string, object>();
        textPart["text"] = Prompt;

        Dictionary<string, object> message = new Dictionary<string, object>();
        message["parts"] = new List<object> { filePart, textPart };

        Dictionary<string, object> generationConfig = new Dictionary<string, object>();
        generationConfig["responseMimeType"] = "application/json";
        generationConfig["temperature"] = 0;

        Dictionary<string, object> payload = new Dictionary<string, object>();
        payload["contents"] = new List<object> { message };
        payload["generationConfig"] = generationConfig;

        HttpClient client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post, GeminiEndpoint.GenerateContentUrl(model));
        request.Headers.Add(GeminiEndpoint.ApiKeyHeader, apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            int code = (int)response.StatusCode;
            logger.LogWarning("Gemini ở ô {Slot} trả mã lỗi {StatusCode} khi nhận diện giấy chứng tử.", slot, code);
            return AttemptResult.Failed("HTTP_" + code, ShouldFailOver(code));
        }

        string responseJson = await response.Content.ReadAsStringAsync(ct);
        string? answer = ReadAnswerText(responseJson);
        if (answer == null)
        {
            return AttemptResult.Failed("BAD_RESPONSE", false);
        }
        DeathCertificateCheck check = ParseAnswer(answer);
        if (check.Status == DeathCertificateCheckStatus.Unavailable)
        {
            return AttemptResult.Failed("BAD_RESPONSE", false);
        }
        return AttemptResult.Success(check);
    }

    /// <summary>Lỗi do phía dịch vụ hoặc khóa/quota của ô này (5xx, 401, 403, 429) thì ô khác có thể trả lời được.</summary>
    private static bool ShouldFailOver(int statusCode)
    {
        return statusCode >= 500 || statusCode == 401 || statusCode == 403 || statusCode == 429;
    }

    private static string? ReadAnswerText(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }
        if (!candidates[0].TryGetProperty("content", out JsonElement candidateContent)
            || !candidateContent.TryGetProperty("parts", out JsonElement parts)
            || parts.ValueKind != JsonValueKind.Array
            || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out JsonElement text))
        {
            return null;
        }
        return text.GetString();
    }

    /// <summary>Đọc JSON AI trả về; thiếu trường hoặc sai kiểu thì coi như không kiểm tra được.</summary>
    public static DeathCertificateCheck ParseAnswer(string answer)
    {
        string json = answer.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            int firstBrace = json.IndexOf('{');
            int lastBrace = json.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace < firstBrace)
            {
                return DeathCertificateCheck.Unavailable();
            }
            json = json.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("isDeathCertificate", out JsonElement isCertificate)
                || (isCertificate.ValueKind != JsonValueKind.True && isCertificate.ValueKind != JsonValueKind.False)
                || !root.TryGetProperty("confidence", out JsonElement confidence)
                || confidence.ValueKind != JsonValueKind.Number)
            {
                return DeathCertificateCheck.Unavailable();
            }

            DeathCertificateCheck check = new DeathCertificateCheck();
            check.Status = DeathCertificateCheckRules.ToStatus(isCertificate.GetBoolean(), confidence.GetDouble());
            if (root.TryGetProperty("documentType", out JsonElement documentType)
                && documentType.ValueKind == JsonValueKind.String)
            {
                check.DocumentType = Shorten(documentType.GetString());
            }
            return check;
        }
        catch (JsonException)
        {
            return DeathCertificateCheck.Unavailable();
        }
    }

    private static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string trimmed = text.Trim();
        if (trimmed.Length > 100)
        {
            return trimmed.Substring(0, 100);
        }
        return trimmed;
    }

    /// <summary>Kết quả một lần gọi một ô: Check luôn có giá trị (Unavailable khi thất bại); Failover cho biết có nên thử ô kia.</summary>
    private sealed class AttemptResult
    {
        public DeathCertificateCheck Check { get; private set; } = DeathCertificateCheck.Unavailable();

        public bool Succeeded { get; private set; }

        public bool Failover { get; private set; }

        public string? ErrorCode { get; private set; }

        public static AttemptResult Success(DeathCertificateCheck check)
        {
            AttemptResult result = new AttemptResult();
            result.Check = check;
            result.Succeeded = true;
            return result;
        }

        public static AttemptResult Failed(string errorCode, bool failover)
        {
            AttemptResult result = new AttemptResult();
            result.ErrorCode = errorCode;
            result.Failover = failover;
            return result;
        }
    }
}

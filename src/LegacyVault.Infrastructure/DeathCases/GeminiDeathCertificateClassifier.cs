using System.Text;
using System.Text.Json;
using LegacyVault.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Gửi một trang giấy (ảnh hoặc PDF) cho Gemini để hỏi có phải giấy chứng tử / trích lục khai tử không.
/// Dùng chung khóa Gemini:ApiKey với trợ lý Verifier. Mọi lỗi đều trả Unavailable, không ném ra ngoài.
/// </summary>
/// <remarks>
/// Không log nội dung tệp hay câu trả lời của AI (có thể chứa họ tên, CCCD in trên giấy), chỉ log mã lỗi HTTP.
/// </remarks>
public sealed class GeminiDeathCertificateClassifier : IDeathCertificateClassifier
{
    private const string ModelName = "gemini-flash-lite-latest";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

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
    private readonly IConfiguration configuration;
    private readonly ILogger<GeminiDeathCertificateClassifier> logger;

    public GeminiDeathCertificateClassifier(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiDeathCertificateClassifier> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task<DeathCertificateCheck> ClassifyAsync(byte[] content, string contentType, CancellationToken ct)
    {
        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini:ApiKey chưa được cấu hình, bỏ qua bước AI nhận diện giấy chứng tử.");
            return DeathCertificateCheck.Unavailable();
        }

        try
        {
            return await CallGeminiAsync(content, contentType, apiKey, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Người dùng hủy request: để luồng gửi dừng như mọi thao tác khác.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Không nhận diện được giấy chứng tử bằng Gemini ({ErrorType}).", ex.GetType().Name);
            return DeathCertificateCheck.Unavailable();
        }
    }

    private async Task<DeathCertificateCheck> CallGeminiAsync(
        byte[] content, string contentType, string apiKey, CancellationToken ct)
    {
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
        client.Timeout = Timeout;

        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent");
        request.Headers.Add("X-goog-api-key", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini trả mã lỗi {StatusCode} khi nhận diện giấy chứng tử.", (int)response.StatusCode);
            return DeathCertificateCheck.Unavailable();
        }

        string responseJson = await response.Content.ReadAsStringAsync(ct);
        string? answer = ReadAnswerText(responseJson);
        if (answer == null)
        {
            return DeathCertificateCheck.Unavailable();
        }
        return ParseAnswer(answer);
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
}

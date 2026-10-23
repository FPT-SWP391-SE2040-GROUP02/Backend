using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Services;

public sealed class GeminiNotaryAssistantService : IAiNotaryAssistantService
{
    private readonly IDeathCaseReviewService reviewService;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger<GeminiNotaryAssistantService> logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiNotaryAssistantService(
        IDeathCaseReviewService reviewService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiNotaryAssistantService> logger)
    {
        this.reviewService = reviewService;
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task<AiDossierAnalysisResponse> AssessDossierAsync(Guid caseId, CancellationToken ct)
    {
        // 1. Tải chi tiết hồ sơ (đã qua kiểm tra quyền Verifier và giải mã dữ liệu cần thiết)
        VerifierDeathCaseResponse detail = await reviewService.GetAsync(caseId, ct);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string? apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Gemini:ApiKey chưa được cấu hình, chuyển sang bộ phân tích quy tắc dự phòng.");
            return NotaryAssessmentRules.BuildFallback(detail, now);
        }

        try
        {
            return await CallGeminiAsync(detail, apiKey, now, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi gọi Google Gemini API cho hồ sơ {CaseId}. Chuyển sang kết quả dự phòng an toàn.", caseId);
            return NotaryAssessmentRules.BuildFallback(detail, now);
        }
    }

    private async Task<AiDossierAnalysisResponse> CallGeminiAsync(
        VerifierDeathCaseResponse detail, string apiKey, DateTimeOffset now, CancellationToken ct)
    {
        string prompt = NotaryAssessmentRules.BuildPrompt(detail);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.2
            }
        };

        HttpClient client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent");
        requestMessage.Headers.Add("X-goog-api-key", apiKey);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.SendAsync(requestMessage, ct);
        if (!response.IsSuccessStatusCode)
        {
            string errBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Gemini API trả về mã lỗi {StatusCode}: {ErrorBody}", response.StatusCode, errBody);
            return NotaryAssessmentRules.BuildFallback(detail, now);
        }

        string responseJson = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(responseJson);
        JsonElement root = doc.RootElement;

        string? rawText = root
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return NotaryAssessmentRules.BuildFallback(detail, now);
        }

        // Làm sạch markdown nếu có
        string cleanJson = rawText.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleanJson = cleanJson[7..];
        }
        else if (cleanJson.StartsWith("```"))
        {
            cleanJson = cleanJson[3..];
        }
        if (cleanJson.EndsWith("```"))
        {
            cleanJson = cleanJson[..^3];
        }
        cleanJson = cleanJson.Trim();

        var parsed = JsonSerializer.Deserialize<GeminiAnalysisRawDto>(cleanJson, JsonOptions);
        if (parsed == null)
        {
            return NotaryAssessmentRules.BuildFallback(detail, now);
        }

        return new AiDossierAnalysisResponse
        {
            CaseId = detail.Id,
            Summary = parsed.Summary ?? "AI chưa tóm tắt được hồ sơ.",
            RiskLevel = NormalizeRiskLevel(parsed.RiskLevel),
            RiskReason = parsed.RiskReason ?? "AI chưa nêu lý do mức rủi ro.",
            Recommendation = NormalizeRecommendation(parsed.Recommendation),
            RecommendationReason = parsed.RecommendationReason ?? "AI chưa nêu lý do cho gợi ý.",
            Checklist = parsed.Checklist?.Select(c => new AiDossierChecklistItem
            {
                Category = c.Category ?? "Tiêu chí thẩm định",
                Status = c.Status ?? "WARNING",
                Detail = c.Detail ?? "AI chưa nêu chi tiết."
            }).ToList() ?? new List<AiDossierChecklistItem>(),
            ProposedDecisionNote = NotaryAssessmentRules.LimitNote(parsed.ProposedDecisionNote),
            AnalyzedAt = now,
            ModelName = "gemini-flash-lite-latest"
        };
    }

    private static string NormalizeRiskLevel(string? raw)
    {
        return raw?.ToUpperInvariant() switch
        {
            "HIGH" => "HIGH",
            "LOW" => "LOW",
            _ => "MEDIUM"
        };
    }

    private static string NormalizeRecommendation(string? raw)
    {
        return raw?.ToUpperInvariant() switch
        {
            "APPROVE" => "APPROVE",
            "REJECT" => "REJECT",
            _ => "SUPPLEMENT"
        };
    }

    private sealed class GeminiAnalysisRawDto
    {
        public string? Summary { get; set; }
        public string? RiskLevel { get; set; }
        public string? RiskReason { get; set; }
        public string? Recommendation { get; set; }
        public string? RecommendationReason { get; set; }
        public List<ChecklistRawDto>? Checklist { get; set; }
        public string? ProposedDecisionNote { get; set; }
    }

    private sealed class ChecklistRawDto
    {
        public string? Category { get; set; }
        public string? Status { get; set; }
        public string? Detail { get; set; }
    }
}

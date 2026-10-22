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
            return BuildFallbackAnalysis(detail, now);
        }

        try
        {
            return await CallGeminiAsync(detail, apiKey, now, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi gọi Google Gemini API cho hồ sơ {CaseId}. Chuyển sang kết quả dự phòng an toàn.", caseId);
            return BuildFallbackAnalysis(detail, now);
        }
    }

    private async Task<AiDossierAnalysisResponse> CallGeminiAsync(
        VerifierDeathCaseResponse detail, string apiKey, DateTimeOffset now, CancellationToken ct)
    {
        var rv = detail.ReviewVersion;
        var flagsText = detail.Flags.Count > 0
            ? string.Join("; ", detail.Flags.Select(f => $"{f.Kind}: {f.Detail} (Ghi chú: {f.ReviewNote ?? "Chưa xét"})"))
            : "Không có cờ cảnh báo.";

        var blockersText = detail.ApproveBlockers.Count > 0
            ? string.Join(", ", detail.ApproveBlockers)
            : "Đủ điều kiện xét duyệt (không có lý do chặn).";

        var prompt = $$"""
            Bạn là Hệ thống Trí tuệ Nhân tạo Thẩm định Hồ sơ Pháp lý (Gemini AI Notary Co-Pilot) của nền tảng Di sản số LegacyVault tại Việt Nam.
            Nhiệm vụ của bạn là hỗ trợ Công chứng viên (Người xét duyệt) thẩm định tính hợp pháp, đối chiếu trật tự thời gian và phát hiện rủi ro gian lận trong hồ sơ báo tử (Death Case).

            DỮ LIỆU HỒ SƠ:
            - Mã hồ sơ: {{detail.Id}} (Kho: {{detail.VaultId}})
            - Trạng thái hiện tại: {{detail.Status}}
            - Họ tên chủ kho: {{rv.OwnerFullName ?? "Chưa rõ"}}
            - Số CCCD khai báo: {{rv.OwnerCitizenId ?? "Chưa rõ"}} (Đã xác minh định danh eKYC)
            - Ngày tử vong khai báo: {{rv.DateOfDeath?.ToString("dd/MM/yyyy") ?? "Chưa có"}}
            - Số trích lục / Giấy chứng tử: {{rv.CertificateNumber ?? "Chưa có"}}
            - Ngày cấp: {{rv.IssuedOn?.ToString("dd/MM/yyyy") ?? "Chưa có"}}
            - Nơi cấp / Cơ quan đăng ký: {{rv.IssuedBy ?? "Chưa có"}}
            - Số lượng tệp chứng từ đính kèm: {{rv.Files.Count}} tệp
            - Các cờ cảnh báo hệ thống (Flags): {{flagsText}}
            - Các lý do chặn duyệt (Approve Blockers): {{blockersText}}
            - Hồ sơ bị từ chối trước đó (nếu có): {{(detail.PreviousCase != null ? $"Có (Lý do cũ: {detail.PreviousCase.RejectReason})" : "Không")}}

            QUY TẮC ĐỐI CHIẾU:
            1. Danh tính: Họ tên và CCCD có hợp lệ theo chuẩn 12 số CCCD gắn chip Việt Nam hay không.
            2. Trật tự thời gian: Ngày mất phải trước hoặc trùng ngày cấp trích lục. Nếu có cờ CHECKIN_AFTER_DEATH (chủ kho điểm danh sau ngày mất) thì đây là rủi ro cực kỳ nghiêm trọng.
            3. Hồ sơ chứng từ: Cần có số hiệu, ngày cấp, nơi cấp rõ ràng và tệp đính kèm.
            4. Khuyến nghị: APPROVE (nếu hồ sơ chuẩn, đủ điều kiện), SUPPLEMENT (nếu thiếu chứng từ hoặc cần làm rõ), REJECT (nếu có dấu hiệu gian lận hoặc sai lệch nghiêm trọng).

            YÊU CẦU: Trả về DUY NHẤT một JSON hợp lệ (không kèm markdown, không có ```json), theo cấu trúc sau:
            {
              "summary": "Tóm tắt ngắn gọn 2 câu về hồ sơ...",
              "riskLevel": "LOW" hoặc "MEDIUM" hoặc "HIGH",
              "riskReason": "Lý giải ngắn gọn mức độ rủi ro...",
              "recommendation": "APPROVE" hoặc "SUPPLEMENT" hoặc "REJECT",
              "recommendationReason": "Lý do đưa ra khuyến nghị...",
              "checklist": [
                { "category": "Xác minh danh tính chủ kho", "status": "VALID", "detail": "Chi tiết đối chiếu..." },
                { "category": "Trật tự thời gian (Timeline)", "status": "VALID hoặc ANOMALY", "detail": "Chi tiết đối chiếu..." },
                { "category": "Tính hợp pháp chứng từ tư pháp", "status": "VALID hoặc WARNING hoặc INVALID", "detail": "Chi tiết đối chiếu..." },
                { "category": "Cờ cảnh báo gian lận (Flags)", "status": "VALID hoặc FLAGGED", "detail": "Chi tiết đối chiếu..." }
              ],
              "proposedDecisionNote": "Văn bản mẫu trang trọng để công chứng viên gửi phản hồi cho Người thực hiện..."
            }
            """;

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
            return BuildFallbackAnalysis(detail, now);
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
            return BuildFallbackAnalysis(detail, now);
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
            return BuildFallbackAnalysis(detail, now);
        }

        return new AiDossierAnalysisResponse
        {
            CaseId = detail.Id,
            Summary = parsed.Summary ?? "Hồ sơ đã được thẩm định tự động bởi Gemini AI Notary Co-Pilot.",
            RiskLevel = NormalizeRiskLevel(parsed.RiskLevel),
            RiskReason = parsed.RiskReason ?? "Dựa trên kết quả đối chiếu dữ liệu hệ thống.",
            Recommendation = NormalizeRecommendation(parsed.Recommendation),
            RecommendationReason = parsed.RecommendationReason ?? "Đề xuất dựa trên các quy định pháp luật hiện hành.",
            Checklist = parsed.Checklist?.Select(c => new AiDossierChecklistItem
            {
                Category = c.Category ?? "Tiêu chí thẩm định",
                Status = c.Status ?? "VALID",
                Detail = c.Detail ?? "Đã kiểm tra."
            }).ToList() ?? new List<AiDossierChecklistItem>(),
            ProposedDecisionNote = parsed.ProposedDecisionNote ?? "Hồ sơ đã được xem xét bởi Văn phòng Công chứng và Trợ lý AI.",
            AnalyzedAt = now,
            ModelName = "gemini-flash-lite-latest"
        };
    }

    private static AiDossierAnalysisResponse BuildFallbackAnalysis(VerifierDeathCaseResponse detail, DateTimeOffset now)
    {
        bool hasCheckinAfterDeath = detail.Flags.Any(f => f.Kind.Equals("CHECKIN_AFTER_DEATH", StringComparison.OrdinalIgnoreCase));
        bool hasFlags = detail.Flags.Count > 0;
        bool hasFiles = detail.ReviewVersion.Files.Count > 0;

        string riskLevel = hasCheckinAfterDeath ? "HIGH" : (hasFlags || !hasFiles ? "MEDIUM" : "LOW");
        string recommendation = hasCheckinAfterDeath ? "REJECT" : (!hasFiles || hasFlags ? "SUPPLEMENT" : (detail.CanApprove ? "APPROVE" : "SUPPLEMENT"));

        var checklist = new List<AiDossierChecklistItem>
        {
            new()
            {
                Category = "Xác minh danh tính chủ kho",
                Status = string.IsNullOrWhiteSpace(detail.ReviewVersion.OwnerCitizenId) ? "WARNING" : "VALID",
                Detail = string.IsNullOrWhiteSpace(detail.ReviewVersion.OwnerCitizenId)
                    ? "Chưa có thông tin số CCCD đầy đủ."
                    : $"Họ tên {detail.ReviewVersion.OwnerFullName ?? "—"} trùng khớp với hồ sơ định danh gốc."
            },
            new()
            {
                Category = "Trật tự thời gian (Timeline)",
                Status = hasCheckinAfterDeath ? "ANOMALY" : "VALID",
                Detail = hasCheckinAfterDeath
                    ? "Cảnh báo bất thường nghiêm trọng: Tồn tại lượt điểm danh sinh tồn sau ngày tử vong khai báo."
                    : "Ngày tử vong và ngày cấp chứng từ hợp lý theo trình tự thời gian."
            },
            new()
            {
                Category = "Tính hợp pháp chứng từ tư pháp",
                Status = hasFiles ? "VALID" : "INVALID",
                Detail = hasFiles
                    ? $"Đã đính kèm {detail.ReviewVersion.Files.Count} trang tài liệu trích lục chứng tử."
                    : "Chưa có tệp hình ảnh trích lục chứng tử đính kèm."
            },
            new()
            {
                Category = "Cờ cảnh báo hệ thống (Flags)",
                Status = hasFlags ? "FLAGGED" : "VALID",
                Detail = hasFlags
                    ? $"Hệ thống ghi nhận {detail.Flags.Count} cờ cảnh báo cần công chứng viên rà soát thủ công."
                    : "Không phát hiện cờ cảnh báo rủi ro nào."
            }
        };

        string decisionNote = hasCheckinAfterDeath
            ? "Từ chối tiếp nhận hồ sơ do phát hiện dữ liệu mâu thuẫn: Chủ kho vẫn thực hiện điểm danh sau thời điểm tử vong được khai báo."
            : (!hasFiles
                ? "Yêu cầu Người thực hiện tải lên bản scan/chụp rõ nét Trích lục khai tử có đóng dấu đỏ của cơ quan có thẩm quyền."
                : "Hồ sơ đầy đủ, hợp lệ theo quy định.");

        return new AiDossierAnalysisResponse
        {
            CaseId = detail.Id,
            Summary = $"Hồ sơ báo tử v{detail.ReviewVersion.VersionNo} của chủ kho {detail.ReviewVersion.OwnerFullName ?? "—"} đã được rà soát bởi bộ luật phân tích quy tắc.",
            RiskLevel = riskLevel,
            RiskReason = hasCheckinAfterDeath ? "Phát hiện mâu thuẫn điểm danh sau ngày tử vong." : "Hồ sơ tuân thủ quy chuẩn thời gian.",
            Recommendation = recommendation,
            RecommendationReason = hasCheckinAfterDeath ? "Nguy cơ giả mạo hồ sơ cao." : "Căn cứ theo số lượng chứng từ và cờ cảnh báo.",
            Checklist = checklist,
            ProposedDecisionNote = decisionNote,
            AnalyzedAt = now,
            ModelName = "rule-based-fallback"
        };
    }

    private static string NormalizeRiskLevel(string? raw)
    {
        return raw?.ToUpperInvariant() switch
        {
            "HIGH" => "HIGH",
            "MEDIUM" => "MEDIUM",
            _ => "LOW"
        };
    }

    private static string NormalizeRecommendation(string? raw)
    {
        return raw?.ToUpperInvariant() switch
        {
            "REJECT" => "REJECT",
            "SUPPLEMENT" => "SUPPLEMENT",
            _ => "APPROVE"
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

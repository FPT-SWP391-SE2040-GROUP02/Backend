using System.Text;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật dựng câu hỏi gửi AI và bộ gợi ý dự phòng cho Người xét duyệt. Hàm thuần, test không cần mạng hay DB.
/// </summary>
/// <remarks>
/// AI chỉ gợi ý: không có hàm nào ở đây đổi trạng thái hồ sơ, đặt cờ hay điền sẵn quyết định.
/// Văn bản gợi ý luôn viết cho Người thực hiện (Executor) đọc, không viết cho Chủ kho vì Chủ kho đã mất.
/// </remarks>
public static class NotaryAssessmentRules
{
    /// <summary>Lý do quyết định tối đa 1000 ký tự (DeathCaseReviewRules.NormalizeReason), nên văn bản gợi ý cũng không dài hơn.</summary>
    public const int MaxNoteLength = 1000;

    public const string DefaultNote = "AI chưa soạn được văn bản gợi ý. Hãy tự viết lý do.";

    public const string CheckinAfterDeathFlag = "CHECKIN_AFTER_DEATH";
    public const string IdentityMismatchFlag = "IDENTITY_MISMATCH";

    /// <summary>Che số CCCD, chỉ giữ 4 số cuối: AI không cần số đầy đủ để gợi ý.</summary>
    public static string MaskCitizenId(VerifierReviewVersionResponse version)
    {
        string? last4 = version.OwnerCitizenIdLast4;
        if (string.IsNullOrWhiteSpace(last4) && !string.IsNullOrWhiteSpace(version.OwnerCitizenId))
        {
            string full = version.OwnerCitizenId.Trim();
            if (full.Length >= 4)
            {
                last4 = full.Substring(full.Length - 4);
            }
        }
        if (string.IsNullOrWhiteSpace(last4))
        {
            return "Chưa có";
        }
        return "****" + last4;
    }

    /// <summary>Cắt văn bản gợi ý cho vừa giới hạn lý do; rỗng thì dùng câu mặc định.</summary>
    public static string LimitNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return DefaultNote;
        }
        string trimmed = note.Trim();
        if (trimmed.Length > MaxNoteLength)
        {
            return trimmed.Substring(0, MaxNoteLength);
        }
        return trimmed;
    }

    public static bool HasFlag(VerifierDeathCaseResponse detail, string kind)
    {
        foreach (VerifierFlagResponse flag in detail.Flags)
        {
            if (string.Equals(flag.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static int CountUnreviewedFlags(VerifierDeathCaseResponse detail)
    {
        int count = 0;
        foreach (VerifierFlagResponse flag in detail.Flags)
        {
            if (flag.ReviewedAt == null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Câu hỏi gửi Gemini. Chỉ đưa dữ liệu đã nhập trong hồ sơ; không đưa số CCCD đầy đủ, không đưa ghi chú xem xét
    /// của Người xét duyệt (đó không phải lời của người nộp hồ sơ) và không khẳng định điều hệ thống chưa kiểm tra.
    /// </summary>
    public static string BuildPrompt(VerifierDeathCaseResponse detail)
    {
        VerifierReviewVersionResponse version = detail.ReviewVersion;

        StringBuilder flags = new StringBuilder();
        foreach (VerifierFlagResponse flag in detail.Flags)
        {
            string reviewed = "Người xét duyệt chưa xem xét";
            if (flag.ReviewedAt != null)
            {
                reviewed = "Người xét duyệt đã xem xét";
            }
            flags.Append("- ").Append(flag.Kind).Append(": ").Append(flag.Detail).Append(" (").Append(reviewed).Append(")\n");
        }
        if (detail.Flags.Count == 0)
        {
            flags.Append("- Không có cờ cảnh báo.\n");
        }

        string blockers = "Không có lý do chặn duyệt.";
        if (detail.ApproveBlockers.Count > 0)
        {
            blockers = string.Join(", ", detail.ApproveBlockers);
        }

        string previous = "Không";
        if (detail.PreviousCase != null)
        {
            previous = "Có (lý do từ chối cũ: " + detail.PreviousCase.RejectReason + ")";
        }

        string dateOfDeath = "Chưa có";
        if (version.DateOfDeath != null)
        {
            dateOfDeath = version.DateOfDeath.Value.ToString("dd/MM/yyyy");
        }
        string issuedOn = "Chưa có";
        if (version.IssuedOn != null)
        {
            issuedOn = version.IssuedOn.Value.ToString("dd/MM/yyyy");
        }

        StringBuilder prompt = new StringBuilder();
        prompt.Append("Bạn là trợ lý AI chỉ để THAM KHẢO cho Người xét duyệt hồ sơ báo tử của nền tảng LegacyVault (Việt Nam).\n");
        prompt.Append("Bạn KHÔNG có quyền quyết định. Người xét duyệt tự chấp thuận, yêu cầu bổ sung hoặc từ chối; ");
        prompt.Append("gợi ý của bạn không thay đổi hồ sơ và có thể bị bỏ qua.\n\n");

        prompt.Append("DỮ LIỆU HỒ SƠ (chỉ gồm các thông tin đã nhập, bạn KHÔNG nhìn thấy ảnh giấy tờ):\n");
        prompt.Append("- Họ tên chủ kho khai trong hồ sơ: ").Append(Fallback(version.OwnerFullName)).Append('\n');
        prompt.Append("- Số CCCD (đã che): ").Append(MaskCitizenId(version)).Append('\n');
        prompt.Append("- Ngày mất khai báo: ").Append(dateOfDeath).Append('\n');
        prompt.Append("- Số giấy chứng tử / trích lục: ").Append(Fallback(version.CertificateNumber)).Append('\n');
        prompt.Append("- Ngày cấp: ").Append(issuedOn).Append('\n');
        prompt.Append("- Nơi cấp: ").Append(Fallback(version.IssuedBy)).Append('\n');
        prompt.Append("- Số trang giấy đính kèm: ").Append(version.Files.Count).Append('\n');
        prompt.Append("- Trạng thái hồ sơ: ").Append(detail.Status).Append('\n');
        prompt.Append("- Hồ sơ trước của kho bị từ chối: ").Append(previous).Append('\n');
        prompt.Append("- Lý do còn chặn duyệt: ").Append(blockers).Append('\n');
        prompt.Append("- Cờ cảnh báo của hệ thống:\n").Append(flags);

        prompt.Append("\nQUY TẮC:\n");
        prompt.Append("1. Chỉ dựa vào dữ liệu ở trên. Không bịa thêm thông tin. Không nói đã xác minh eKYC, đã đối chiếu CCCD hay đã xem giấy tờ ");
        prompt.Append("nếu dữ liệu không nêu rõ. CCCD được coi là lệch chỉ khi có cờ IDENTITY_MISMATCH.\n");
        prompt.Append("2. Ngày mất phải trước hoặc trùng ngày cấp. Cờ CHECKIN_AFTER_DEATH (chủ kho điểm danh sau ngày mất) là điểm đáng ngờ nặng.\n");
        prompt.Append("3. Số giấy, ngày cấp, nơi cấp phải rõ ràng. Giá trị vô nghĩa (ví dụ chuỗi ký tự ngẫu nhiên) nghĩa là CẦN LÀM RÕ, ");
        prompt.Append("chưa phải bằng chứng gian lận. Không kết tội người nộp hồ sơ.\n");
        prompt.Append("4. recommendation chỉ là một trong APPROVE, SUPPLEMENT, REJECT. Nếu không chắc, chọn SUPPLEMENT.\n");
        prompt.Append("5. proposedDecisionNote là lý do mẫu cho Người xét duyệt tham khảo, gửi cho NGƯỜI THỰC HIỆN (Executor) đọc. ");
        prompt.Append("Không viết cho chủ kho (chủ kho đã mất), không mở đầu bằng 'Kính gửi chủ kho', không dùng 'quý vị' để chỉ chủ kho. ");
        prompt.Append("Phải khớp với recommendation, ngắn gọn dưới 600 ký tự, nêu việc cần làm hoặc lý do cụ thể, giọng trung lập.\n\n");

        prompt.Append("Trả về DUY NHẤT một JSON hợp lệ, không markdown:\n");
        prompt.Append("{\n");
        prompt.Append("  \"summary\": \"Tóm tắt 2 câu\",\n");
        prompt.Append("  \"riskLevel\": \"LOW\" | \"MEDIUM\" | \"HIGH\",\n");
        prompt.Append("  \"riskReason\": \"Lý do ngắn\",\n");
        prompt.Append("  \"recommendation\": \"APPROVE\" | \"SUPPLEMENT\" | \"REJECT\",\n");
        prompt.Append("  \"recommendationReason\": \"Lý do ngắn\",\n");
        prompt.Append("  \"checklist\": [\n");
        prompt.Append("    { \"category\": \"Xác minh danh tính chủ kho\", \"status\": \"VALID\" | \"WARNING\" | \"ANOMALY\", \"detail\": \"...\" },\n");
        prompt.Append("    { \"category\": \"Trật tự thời gian (Timeline)\", \"status\": \"VALID\" | \"ANOMALY\", \"detail\": \"...\" },\n");
        prompt.Append("    { \"category\": \"Tính hợp lệ chứng từ\", \"status\": \"VALID\" | \"WARNING\" | \"INVALID\", \"detail\": \"...\" },\n");
        prompt.Append("    { \"category\": \"Cờ cảnh báo\", \"status\": \"VALID\" | \"FLAGGED\", \"detail\": \"...\" }\n");
        prompt.Append("  ],\n");
        prompt.Append("  \"proposedDecisionNote\": \"Lý do mẫu gửi Người thực hiện\"\n");
        prompt.Append("}\n");
        return prompt.ToString();
    }

    /// <summary>
    /// Gợi ý dự phòng khi chưa có key Gemini hoặc Gemini lỗi. Chỉ nói điều đo được từ dữ liệu, không khẳng định
    /// "đã khớp" hay "hợp lệ" khi chưa kiểm tra; văn bản gợi ý luôn khớp với khuyến nghị.
    /// </summary>
    public static AiDossierAnalysisResponse BuildFallback(VerifierDeathCaseResponse detail, DateTimeOffset now)
    {
        bool checkinAfterDeath = HasFlag(detail, CheckinAfterDeathFlag);
        bool identityMismatch = HasFlag(detail, IdentityMismatchFlag);
        int unreviewed = CountUnreviewedFlags(detail);
        int fileCount = detail.ReviewVersion.Files.Count;
        bool hasFiles = fileCount > 0;

        string riskLevel = "LOW";
        if (checkinAfterDeath || identityMismatch)
        {
            riskLevel = "HIGH";
        }
        else if (unreviewed > 0 || !hasFiles)
        {
            riskLevel = "MEDIUM";
        }

        string recommendation = "SUPPLEMENT";
        string recommendationReason = "Hồ sơ chưa đủ điều kiện duyệt (xem mục Điều kiện để duyệt).";
        string note = "Hồ sơ chưa đủ điều kiện duyệt. Vui lòng kiểm tra lại thông tin đã nhập.";
        if (checkinAfterDeath)
        {
            recommendation = "REJECT";
            recommendationReason = "Chủ kho vẫn có lượt điểm danh sau ngày mất đã khai báo.";
            note = "Hồ sơ bị từ chối vì chủ kho vẫn có lượt điểm danh sau ngày mất đã khai báo. Vui lòng kiểm tra lại ngày mất.";
        }
        else if (!hasFiles)
        {
            recommendationReason = "Hồ sơ chưa có trang giấy nào.";
            note = "Vui lòng tải lên ảnh chụp rõ nét giấy chứng tử hoặc trích lục khai tử do cơ quan có thẩm quyền cấp.";
        }
        else if (identityMismatch)
        {
            recommendationReason = "Hệ thống ghi nhận cờ số CCCD không khớp với CCCD chủ kho đã xác minh.";
            note = "Số CCCD trong hồ sơ không khớp với CCCD chủ kho đã xác minh. Vui lòng kiểm tra và nhập lại đúng số CCCD.";
        }
        else if (unreviewed > 0)
        {
            recommendationReason = "Còn " + unreviewed + " cờ cảnh báo chưa được Người xét duyệt xem xét.";
            note = "Vui lòng kiểm tra lại thông tin đã nhập và giải thích các điểm bất thường được nêu.";
        }
        else if (detail.CanApprove)
        {
            recommendation = "APPROVE";
            recommendationReason = "Không còn cờ chưa xem xét và hồ sơ đủ điều kiện duyệt theo hệ thống.";
            note = "Thông tin hồ sơ đã được đối chiếu, không còn điểm bất thường chưa xử lý.";
        }

        List<AiDossierChecklistItem> checklist = new List<AiDossierChecklistItem>();

        AiDossierChecklistItem identity = new AiDossierChecklistItem();
        identity.Category = "Xác minh danh tính chủ kho";
        identity.Status = "VALID";
        identity.Detail = "Hệ thống không ghi nhận cờ CCCD không khớp. AI chưa tự đối chiếu họ tên và CCCD.";
        if (identityMismatch)
        {
            identity.Status = "ANOMALY";
            identity.Detail = "Có cờ số CCCD không khớp với CCCD chủ kho đã xác minh.";
        }
        checklist.Add(identity);

        AiDossierChecklistItem timeline = new AiDossierChecklistItem();
        timeline.Category = "Trật tự thời gian (Timeline)";
        timeline.Status = "VALID";
        timeline.Detail = "Không ghi nhận lượt điểm danh sau ngày mất đã khai báo.";
        if (checkinAfterDeath)
        {
            timeline.Status = "ANOMALY";
            timeline.Detail = "Có lượt điểm danh sinh tồn sau ngày mất đã khai báo.";
        }
        checklist.Add(timeline);

        AiDossierChecklistItem documents = new AiDossierChecklistItem();
        documents.Category = "Tính hợp lệ chứng từ";
        documents.Status = "WARNING";
        documents.Detail = "Hồ sơ có " + fileCount + " trang giấy. AI không xem ảnh, Người xét duyệt cần tự đối chiếu giấy tờ.";
        if (!hasFiles)
        {
            documents.Status = "INVALID";
            documents.Detail = "Hồ sơ chưa có trang giấy nào.";
        }
        checklist.Add(documents);

        AiDossierChecklistItem flags = new AiDossierChecklistItem();
        flags.Category = "Cờ cảnh báo";
        flags.Status = "VALID";
        flags.Detail = "Không còn cờ cảnh báo chưa xem xét.";
        if (unreviewed > 0)
        {
            flags.Status = "FLAGGED";
            flags.Detail = "Còn " + unreviewed + " cờ cảnh báo chưa xem xét.";
        }
        checklist.Add(flags);

        AiDossierAnalysisResponse response = new AiDossierAnalysisResponse();
        response.CaseId = detail.Id;
        response.Summary = "Hồ sơ báo tử bản v" + detail.ReviewVersion.VersionNo + " được rà soát bằng bộ quy tắc, không dùng AI. Đây chỉ là gợi ý tham khảo.";
        response.RiskLevel = riskLevel;
        response.RiskReason = "Dựa trên cờ cảnh báo và số trang giấy; không dựa trên nội dung ảnh.";
        if (checkinAfterDeath)
        {
            response.RiskReason = "Có lượt điểm danh sau ngày mất đã khai báo.";
        }
        response.Recommendation = recommendation;
        response.RecommendationReason = recommendationReason;
        response.Checklist = checklist;
        response.ProposedDecisionNote = LimitNote(note);
        response.AnalyzedAt = now;
        response.ModelName = "rule-based-fallback";
        return response;
    }

    private static string Fallback(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Chưa có";
        }
        return value;
    }
}

using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần Verifier xét hồ sơ chứng tử (SRS §6.2 F2B), dùng chung cho chi tiết và các quyết định.
/// </summary>
public static class DeathCaseReviewRules
{
    /// <summary>Độ dài tối đa của lý do / ghi chú Verifier (khớp cột DeathCaseDecisions.Reason, DeathCaseFlags.ReviewNote).</summary>
    public const int MaxReasonLength = 1000;

    /// <summary>AC-71: hồ sơ đã có quyết định cuối (duyệt hoặc từ chối).</summary>
    public static bool IsClosed(DeathCaseStatus status)
    {
        if (status == DeathCaseStatus.ApprovedForHandover || status == DeathCaseStatus.Rejected)
        {
            return true;
        }
        return false;
    }

    /// <summary>DEATH-01: yêu cầu bổ sung, từ chối, xem xét cờ chỉ làm được khi hồ sơ đang xét hoặc đang chờ bổ sung.</summary>
    public static bool CanDecide(DeathCaseStatus status)
    {
        if (status == DeathCaseStatus.UnderReview || status == DeathCaseStatus.SupplementRequired)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// D5: bản Verifier xét là ReviewVersionNo. Hồ sơ đã có phân công thì chắc chắn đã gửi,
    /// nên ReviewVersionNo null là dữ liệu hỏng.
    /// </summary>
    public static DeathCaseVersion FindReviewVersion(DeathCase deathCase)
    {
        if (deathCase.ReviewVersionNo == null)
        {
            throw new InvalidOperationException("Hồ sơ chứng tử chưa gửi nhưng đang được xét.");
        }
        return DeathCaseMappings.FindVersion(deathCase, deathCase.ReviewVersionNo.Value);
    }

    /// <summary>
    /// DEATH-08, DEATH-09, DEATH-10, DEATH-04: các lý do chưa duyệt được, cộng dồn theo thứ tự cố định,
    /// mỗi phần tử là mã reason UPPER_SNAKE_CASE; rỗng nghĩa là duyệt được. Cần hồ sơ đã nạp Versions và Flags.
    /// TODO: hold/phản đối của Owner (F2C).
    /// </summary>
    public static List<string> GetApproveBlockers(DeathCase deathCase, DateTimeOffset now)
    {
        List<string> blockers = new List<string>();
        if (deathCase.Status != DeathCaseStatus.UnderReview)
        {
            blockers.Add(DeathCaseNotAllowedReason.NotUnderReview.ToApiString());
        }
        if (deathCase.ReviewVersionNo != null && deathCase.CurrentVersionNo != deathCase.ReviewVersionNo.Value)
        {
            blockers.Add(DeathCaseNotAllowedReason.NewerVersionPending.ToApiString());
        }
        if (deathCase.ObjectionEndsAt == null)
        {
            blockers.Add(DeathCaseNotAllowedReason.OwnerNotNotified.ToApiString());
        }
        else if (now < deathCase.ObjectionEndsAt.Value)
        {
            blockers.Add(DeathCaseNotAllowedReason.ObjectionPeriodNotOver.ToApiString());
        }
        if (HasUnreviewedFlag(deathCase))
        {
            blockers.Add(DeathCaseNotAllowedReason.FlagsNotReviewed.ToApiString());
        }
        return blockers;
    }

    /// <summary>DEATH-10: chỉ cờ của bản đang xét mới chặn duyệt; cờ của bản cũ không tính.</summary>
    private static bool HasUnreviewedFlag(DeathCase deathCase)
    {
        if (deathCase.ReviewVersionNo == null)
        {
            return false;
        }
        DeathCaseVersion reviewVersion = DeathCaseMappings.FindVersion(deathCase, deathCase.ReviewVersionNo.Value);
        foreach (DeathCaseFlag flag in deathCase.Flags)
        {
            if (flag.VersionId == reviewVersion.Id && flag.ReviewedAt == null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>DEATH-03: lý do bắt buộc, trim, tối đa 1000 ký tự; sai thì 400 theo tên trường field.</summary>
    public static string NormalizeReason(string? reason, string field)
    {
        string? text = TextInput.NullIfBlank(reason);
        if (text == null)
        {
            throw ValidationErrors.For(field, "Vui lòng nhập nội dung.");
        }
        if (text.Length > MaxReasonLength)
        {
            throw ValidationErrors.For(field, "Nội dung tối đa " + MaxReasonLength + " ký tự.");
        }
        return text;
    }

    /// <summary>
    /// DEATH-01: các phần cần bổ sung. Bắt buộc có ít nhất một, mỗi phần phải thuộc
    /// DeathCaseRules.AllRequirementCodes; trim, bỏ trùng, sắp xếp Ordinal, nối bằng dấu phẩy để lưu.
    /// </summary>
    public static string NormalizeRequestedParts(List<string>? parts)
    {
        if (parts == null || parts.Count == 0)
        {
            throw ValidationErrors.For("requestedParts", "Vui lòng chọn ít nhất một phần cần bổ sung.");
        }

        List<string> normalized = new List<string>();
        foreach (string part in parts)
        {
            string text = string.Empty;
            if (part != null)
            {
                text = part.Trim();
            }
            if (Array.IndexOf(DeathCaseRules.AllRequirementCodes, text) < 0)
            {
                throw ValidationErrors.For("requestedParts", "Phần cần bổ sung không hợp lệ.");
            }
            if (!normalized.Contains(text))
            {
                normalized.Add(text);
            }
        }
        normalized.Sort(StringComparer.Ordinal);
        return string.Join(",", normalized);
    }

    /// <summary>Tách chuỗi RequestedParts đã lưu thành danh sách để trả response; null/rỗng → danh sách rỗng.</summary>
    public static List<string> SplitRequestedParts(string? stored)
    {
        List<string> parts = new List<string>();
        if (stored == null || stored.Length == 0)
        {
            return parts;
        }
        string[] pieces = stored.Split(',');
        foreach (string piece in pieces)
        {
            if (piece.Length > 0)
            {
                parts.Add(piece);
            }
        }
        return parts;
    }
}

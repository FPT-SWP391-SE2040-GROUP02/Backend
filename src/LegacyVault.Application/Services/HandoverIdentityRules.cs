using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật nối kết quả eKYC với bước xác minh danh tính của lượt bàn giao (SRS §7.2, MATCH-01..05). Không truy cập dữ liệu.
/// </summary>
public static class HandoverIdentityRules
{
    /// <summary>
    /// Lý do kết quả eKYC chưa cho lượt sang "sẵn sàng nhận"; null nghĩa là đủ điều kiện.
    /// Chỉ cho qua khi lượt eKYC đúng mục đích người nhận, ghim đúng lượt này, selfie đạt và CCCD, họ tên, ngày sinh
    /// khớp người được chỉ định (MATCH-01..03). Kết quả "cần xem xét" không tự cho qua (MATCH-04).
    /// </summary>
    public static string? GetBlockingReason(EkycVerification? verification, Guid turnId)
    {
        if (verification == null)
        {
            return "IDENTITY_NOT_VERIFIED";
        }
        if (verification.Purpose != EkycPurpose.BeneficiaryClaim || verification.HandoverTurnId != turnId)
        {
            return "IDENTITY_NOT_VERIFIED";
        }
        if (verification.Status == EkycStatus.Pending)
        {
            return "IDENTITY_NOT_FINISHED";
        }
        if (verification.Status == EkycStatus.Failed)
        {
            return "IDENTITY_FAILED";
        }

        if (verification.IdentityMatch == EkycIdentityMatch.WrongPerson)
        {
            return "IDENTITY_WRONG_PERSON";
        }
        if (verification.IdentityMatch == EkycIdentityMatch.NeedsReview)
        {
            return "IDENTITY_NEEDS_REVIEW";
        }
        if (verification.IdentityMatch != EkycIdentityMatch.Matched)
        {
            return "IDENTITY_NOT_VERIFIED";
        }
        return null;
    }

    /// <summary>Thông báo cho người dùng ứng với từng lý do ở <see cref="GetBlockingReason"/>.</summary>
    public static string GetMessage(string reason)
    {
        switch (reason)
        {
            case "IDENTITY_NOT_FINISHED":
                return "Bạn chưa hoàn tất bước so khớp khuôn mặt.";
            case "IDENTITY_FAILED":
                return "Lần xác minh gần nhất chưa đạt. Vui lòng xác minh lại.";
            case "IDENTITY_WRONG_PERSON":
                return "Giấy tờ không khớp với người được chỉ định nhận.";
            case "IDENTITY_NEEDS_REVIEW":
                return "Thông tin trên giấy tờ chưa đủ chắc để xác nhận. Vui lòng chụp lại rõ hơn.";
            default:
                return "Bạn chưa xác minh danh tính cho lượt này.";
        }
    }
}

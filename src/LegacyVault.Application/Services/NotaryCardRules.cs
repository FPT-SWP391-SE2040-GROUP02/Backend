using LegacyVault.Application.Exceptions;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Quy tắc dùng chung về thẻ công chứng viên của Người xét duyệt.</summary>
public static class NotaryCardRules
{
    /// <summary>Giá trị reason trả về khi tài khoản Người xét duyệt chưa có ảnh thẻ.</summary>
    public const string RequiredReason = "NOTARY_CARD_REQUIRED";

    public const string RequiredMessage =
        "Tài khoản của bạn chưa có ảnh thẻ công chứng viên. Hãy liên hệ quản trị viên để nộp thẻ rồi mới duyệt được.";

    public static bool HasCard(User user)
    {
        return user.NotaryCardStorageKey != null && user.NotaryCardWrappedDataKey != null;
    }

    /// <summary>Chưa có ảnh thẻ thì 409 với reason NOTARY_CARD_REQUIRED, dùng mã lỗi của khu vực đang gọi.</summary>
    public static void EnsureHasCard(User user, ErrorCode code)
    {
        if (HasCard(user))
        {
            return;
        }
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = RequiredReason;
        throw new AppException(AppErrorType.Conflict, code, RequiredMessage, extensions);
    }
}

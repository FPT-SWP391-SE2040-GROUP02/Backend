using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật eKYC: ngưỡng, hạn mã tham chiếu, giới hạn ảnh. Không truy cập dữ liệu.</summary>
public static class EkycRules
{
    /// <summary>Ngưỡng cosine của SFace dùng để coi hai khuôn mặt là một người. Server cố định, FE không được đổi.</summary>
    public const double MatchThreshold = 0.363;

    /// <summary>Ảnh tối đa 8 MiB, bằng giới hạn của dịch vụ eKYC.</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>Dịch vụ eKYC giữ khuôn mặt trên giấy tờ 30 phút.</summary>
    public static readonly TimeSpan DocumentTokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Chỉ nhận ảnh JPEG hoặc PNG.</summary>
    public static bool IsAllowedContentType(string contentType)
    {
        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    /// <summary>Còn mã tham chiếu dùng được để so khớp selfie hay không.</summary>
    public static bool CanMatchFace(EkycVerification verification, DateTimeOffset now)
    {
        if (verification.Status != EkycStatus.Pending)
        {
            return false;
        }
        if (verification.DocumentToken == null || verification.DocumentTokenExpiresAt == null)
        {
            return false;
        }
        if (now >= verification.DocumentTokenExpiresAt.Value)
        {
            return false;
        }
        return true;
    }

    /// <summary>Lấy 4 số cuối của số CCCD; rỗng nếu không đủ 4 chữ số ở cuối.</summary>
    public static string GetCitizenIdLast4(string idNumber)
    {
        if (idNumber.Length < 4)
        {
            return string.Empty;
        }
        string last4 = idNumber.Substring(idNumber.Length - 4);
        foreach (char character in last4)
        {
            if (!char.IsAsciiDigit(character))
            {
                return string.Empty;
            }
        }
        return last4;
    }
}

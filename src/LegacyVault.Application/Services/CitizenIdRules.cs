using System.Text;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>Chuẩn hóa và kiểm tra số CCCD, dùng chung cho người nhận và hồ sơ chứng tử.</summary>
public static class CitizenIdRules
{
    /// <summary>EKYC-08: CCCD gồm đúng 12 chữ số.</summary>
    public const int CitizenIdLength = 12;

    /// <summary>
    /// EKYC-08: bỏ khoảng trắng; phải đúng 12 chữ số 0-9; giữ dạng chuỗi để không mất số 0 đầu.
    /// Sai thì lỗi 400 VALIDATION_ERROR gắn với trường field.
    /// </summary>
    public static string NormalizeCitizenId(string? citizenId, string field)
    {
        StringBuilder builder = new StringBuilder();
        if (citizenId != null)
        {
            foreach (char c in citizenId)
            {
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
            }
        }

        string normalized = builder.ToString();
        if (!IsDigits(normalized, CitizenIdLength, CitizenIdLength))
        {
            throw ValidationErrors.For(field, "Số CCCD phải gồm đúng 12 chữ số.");
        }
        return normalized;
    }

    /// <summary>Chuỗi chỉ gồm chữ số 0-9, độ dài trong [minLength, maxLength].</summary>
    public static bool IsDigits(string value, int minLength, int maxLength)
    {
        if (value.Length < minLength || value.Length > maxLength)
        {
            return false;
        }
        foreach (char c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>DATA-02: 4 số cuối của CCCD đã chuẩn hóa, để trả cho FE thay số đầy đủ.</summary>
    public static string Last4(string normalizedCitizenId)
    {
        return normalizedCitizenId.Substring(normalizedCitizenId.Length - 4);
    }
}

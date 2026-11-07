using System.Text;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>Chuẩn hóa và kiểm tra số điện thoại di động Việt Nam của tài khoản.</summary>
public static class PhoneRules
{
    /// <summary>Số di động Việt Nam sau khi chuẩn hóa gồm đúng 10 chữ số, bắt đầu bằng 0.</summary>
    public const int PhoneLength = 10;

    public const string InvalidMessage =
        "Số điện thoại phải là số di động Việt Nam gồm 10 chữ số, bắt đầu bằng 03, 05, 07, 08 hoặc 09.";

    /// <summary>
    /// Bỏ khoảng trắng, dấu chấm, gạch nối và ngoặc; đổi đầu số +84 hoặc 84 thành 0.
    /// Kết quả phải là 10 chữ số với đầu số di động 03, 05, 07, 08 hoặc 09; sai thì lỗi 400 VALIDATION_ERROR gắn với trường field.
    /// </summary>
    public static string NormalizeVietnamPhone(string? phone, string field)
    {
        StringBuilder builder = new StringBuilder();
        if (phone != null)
        {
            foreach (char c in phone)
            {
                if (char.IsWhiteSpace(c) || c == '.' || c == '-' || c == '(' || c == ')')
                {
                    continue;
                }
                builder.Append(c);
            }
        }

        string value = builder.ToString();
        if (value.StartsWith("+84", StringComparison.Ordinal))
        {
            value = "0" + value.Substring(3);
        }
        else if (value.StartsWith("84", StringComparison.Ordinal) && value.Length == PhoneLength + 1)
        {
            value = "0" + value.Substring(2);
        }

        if (!CitizenIdRules.IsDigits(value, PhoneLength, PhoneLength) || !HasMobilePrefix(value))
        {
            throw ValidationErrors.For(field, InvalidMessage);
        }
        return value;
    }

    private static bool HasMobilePrefix(string value)
    {
        if (value[0] != '0')
        {
            return false;
        }
        char second = value[1];
        if (second == '3' || second == '5' || second == '7' || second == '8' || second == '9')
        {
            return true;
        }
        return false;
    }
}

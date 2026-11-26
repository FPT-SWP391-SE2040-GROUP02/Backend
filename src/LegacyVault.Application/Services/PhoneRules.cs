using System.Text;
using System.Text.RegularExpressions;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>
/// Chuẩn hóa số điện thoại liên hệ của tài khoản (đăng ký, Google): không kiểm định dạng, gõ số nào cũng nhận (đồ án,
/// demo dễ nhập). Chỗ nhập số của người khác (tài khoản nhân sự do Admin tạo, người nhận trong hồ sơ báo tin) dùng
/// <see cref="NormalizeVietnamesePhone"/> để bắt số sai định dạng.
/// </summary>
public static partial class PhoneRules
{
    public const int MaxLength = 20;

    public const string InvalidFormatMessage = "Số điện thoại không đúng định dạng (ví dụ 0912345678 hoặc +84912345678).";

    public const string RequiredMessage = "Số điện thoại là bắt buộc.";

    public const string AlreadyUsedMessage = "Số điện thoại đã được dùng.";

    /// <summary>Lỗi 409 PHONE_ALREADY_EXISTS gắn với trường field: số đã thuộc một tài khoản khác.</summary>
    public static AppException AlreadyUsed(string field)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["field"] = field;
        return new AppException(AppErrorType.Conflict, ErrorCode.PhoneAlreadyExists, AlreadyUsedMessage, extensions);
    }

    /// <summary>
    /// Bỏ khoảng trắng, dấu chấm, gạch nối và ngoặc. Không được trống và không dài quá <see cref="MaxLength"/> ký tự;
    /// không kiểm đầu số hay độ dài tối thiểu. Sai thì lỗi 400 VALIDATION_ERROR gắn với trường field.
    /// </summary>
    public static string NormalizeContactPhone(string? phone, string field)
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
        if (value.Length == 0)
        {
            throw ValidationErrors.For(field, RequiredMessage);
        }
        if (value.Length > MaxLength)
        {
            throw ValidationErrors.For(field, "Số điện thoại tối đa 20 ký tự.");
        }
        return value;
    }

    /// <summary>
    /// Như <see cref="NormalizeContactPhone"/> rồi bắt buộc đúng số di động Việt Nam: 0 hoặc +84, tiếp theo là đầu số
    /// 3, 5, 7, 8 hoặc 9 và 8 chữ số nữa (ví dụ 0912345678, +84912345678). Sai định dạng thì 400 VALIDATION_ERROR gắn
    /// với trường field.
    /// </summary>
    public static string NormalizeVietnamesePhone(string? phone, string field)
    {
        string value = NormalizeContactPhone(phone, field);
        if (!IsVietnamesePhone(value))
        {
            throw ValidationErrors.For(field, InvalidFormatMessage);
        }
        return value;
    }

    /// <summary>Số đã bỏ ký tự phân cách có đúng dạng số di động Việt Nam không.</summary>
    public static bool IsVietnamesePhone(string normalized)
    {
        return VietnamesePhonePattern().IsMatch(normalized);
    }

    [GeneratedRegex("^(0|\\+84)[35789][0-9]{8}$")]
    private static partial Regex VietnamesePhonePattern();
}

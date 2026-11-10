using System.Text;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

/// <summary>Chuẩn hóa số điện thoại liên hệ của tài khoản. Không kiểm định dạng: gõ số nào cũng nhận (đồ án, demo dễ nhập).</summary>
public static class PhoneRules
{
    public const int MaxLength = 20;

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
}

using System.Globalization;
using System.Text.RegularExpressions;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.ServiceConfig;

/// <summary>
/// Kiểm tra giá trị Admin gửi lên cho cấu hình dịch vụ ngoài. Ném AppException loại Validation (400) với tên trường sai.
/// Không có giá trị bí mật nào xuất hiện trong thông báo lỗi.
/// </summary>
public static class SystemConfigValidator
{
    public const int MaxReasonLength = 500;

    /// <summary>
    /// Mọi trường thường bắt buộc phải có và hợp lệ; tên lạ bị từ chối. Giá trị được so sau khi bỏ khoảng trắng hai đầu,
    /// người gọi phải lưu đúng giá trị đã bỏ khoảng trắng.
    /// </summary>
    public static void ValidateSettings(string serviceKey, IReadOnlyDictionary<string, string?> settings)
    {
        IReadOnlyList<ConfigFieldSpec> fields = SystemConfigCatalog.Fields(serviceKey);
        foreach (string name in settings.Keys)
        {
            if (FindField(fields, name, false) == null)
            {
                throw ValidationErrors.For("settings." + name, "Trường này không được hỗ trợ.");
            }
        }

        foreach (ConfigFieldSpec field in fields)
        {
            if (field.Secret)
            {
                continue;
            }
            string path = "settings." + field.Name;
            string? raw = null;
            settings.TryGetValue(field.Name, out raw);
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                if (field.Required)
                {
                    throw ValidationErrors.For(path, "Trường này là bắt buộc.");
                }
                continue;
            }
            CheckValue(field, value, path);
        }
    }

    /// <summary>Tên bí mật phải có trong danh mục và không dài quá giới hạn; giá trị rỗng nghĩa là giữ nguyên nên được chấp nhận.</summary>
    public static void ValidateSecrets(string serviceKey, IReadOnlyDictionary<string, string?> secrets)
    {
        IReadOnlyList<ConfigFieldSpec> fields = SystemConfigCatalog.Fields(serviceKey);
        foreach (KeyValuePair<string, string?> entry in secrets)
        {
            string path = "secrets." + entry.Key;
            ConfigFieldSpec? field = FindField(fields, entry.Key, true);
            if (field == null)
            {
                throw ValidationErrors.For(path, "Trường này không được hỗ trợ.");
            }
            string value = (entry.Value ?? string.Empty).Trim();
            if (value.Length > field.Max)
            {
                throw ValidationErrors.For(path, $"Tối đa {field.Max} ký tự.");
            }
        }
    }

    /// <summary>Lý do bắt buộc, bỏ khoảng trắng hai đầu, tối đa 500 ký tự. Trả lý do đã chuẩn hóa.</summary>
    public static string RequireReason(string? reason)
    {
        string text = (reason ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw ValidationErrors.For("reason", "Vui lòng nhập lý do thay đổi.");
        }
        if (text.Length > MaxReasonLength)
        {
            throw ValidationErrors.For("reason", $"Lý do tối đa {MaxReasonLength} ký tự.");
        }
        return text;
    }

    private static ConfigFieldSpec? FindField(IReadOnlyList<ConfigFieldSpec> fields, string name, bool secret)
    {
        foreach (ConfigFieldSpec field in fields)
        {
            if (field.Secret == secret && string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                return field;
            }
        }
        return null;
    }

    private static void CheckValue(ConfigFieldSpec field, string value, string path)
    {
        if (field.Kind == ConfigFieldKind.Integer)
        {
            int number = 0;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                throw ValidationErrors.For(path, "Phải là số nguyên.");
            }
            if (number < field.Min || number > field.Max)
            {
                throw ValidationErrors.For(path, $"Phải từ {field.Min} đến {field.Max}.");
            }
            return;
        }

        if (field.Pattern != null)
        {
            if (!Regex.IsMatch(value, field.Pattern))
            {
                throw ValidationErrors.For(path, "Giá trị không hợp lệ.");
            }
            return;
        }
        if (field.Max > 0 && value.Length > field.Max)
        {
            throw ValidationErrors.For(path, $"Tối đa {field.Max} ký tự.");
        }
    }
}

namespace LegacyVault.Application.Notifications;

/// <summary>Các phần chữ Admin được sửa của một thư thông báo.</summary>
public sealed record EmailParts(string Subject, string Title, string Body, string ButtonText, string Note);

/// <summary>Chữ Admin đặt cho một loại thư; trường null giữ chữ gốc.</summary>
public sealed record EmailTemplateOverride(
    bool Enabled, string? Subject, string? Title, string? Body, string? ButtonText, string? Note);

/// <summary>
/// Bản chụp chữ Admin đã đặt cho từng loại thư, để các hàm dựng thư (hàm thuần, không có DB) dùng được.
/// Được nạp lúc khởi động, làm mới định kỳ và ngay sau khi Admin lưu. Chưa nạp thì mọi thư dùng chữ gốc.
/// </summary>
public static class EmailTemplateOverrides
{
    private static volatile IReadOnlyDictionary<string, EmailTemplateOverride> snapshot =
        new Dictionary<string, EmailTemplateOverride>();

    public static void Replace(IReadOnlyDictionary<string, EmailTemplateOverride> next)
    {
        snapshot = next;
    }

    /// <summary>false khi Admin đã tắt loại thư này và loại thư cho phép tắt.</summary>
    public static bool IsEnabled(string key)
    {
        EmailTemplateOverride? found;
        if (!snapshot.TryGetValue(key, out found))
        {
            return true;
        }
        return found.Enabled || !EmailTemplateCatalog.CanDisable(key);
    }

    /// <summary>Thay trường Admin đã đặt vào chữ gốc; trong chữ của Admin, {TEN_BIEN} được thay bằng giá trị trong tokens.</summary>
    public static EmailParts Apply(string key, EmailParts defaults, IReadOnlyDictionary<string, string> tokens)
    {
        EmailTemplateOverride? found;
        if (!snapshot.TryGetValue(key, out found))
        {
            return defaults;
        }
        bool bodyEditable = EmailTemplateCatalog.HasEditableBody(key);
        return new EmailParts(
            Pick(found.Subject, defaults.Subject, tokens),
            Pick(found.Title, defaults.Title, tokens),
            bodyEditable ? Pick(found.Body, defaults.Body, tokens) : defaults.Body,
            Pick(found.ButtonText, defaults.ButtonText, tokens),
            Pick(found.Note, defaults.Note, tokens));
    }

    /// <summary>Thay {TEN_BIEN} trong chữ bằng giá trị trong một lượt (giá trị chèn vào không bị thay tiếp); biến lạ giữ nguyên.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> tokens)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\{([A-Z0-9_]+)\}", match =>
        {
            string? value;
            if (tokens.TryGetValue(match.Groups[1].Value, out value))
            {
                return value;
            }
            return match.Value;
        });
    }

    private static string Pick(string? custom, string fallback, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(custom))
        {
            return fallback;
        }
        return Fill(custom.Trim(), tokens);
    }
}

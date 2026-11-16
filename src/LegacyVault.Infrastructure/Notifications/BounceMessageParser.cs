using System.Text.RegularExpressions;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Đọc một thư báo lỗi (DSN, RFC 3464) và lấy ra các người nhận bị từ chối vĩnh viễn kèm mã SMTP.</summary>
public static partial class BounceMessageParser
{
    public sealed record FailedRecipient(string Email, string Code);

    private const string DefaultCode = "5.0.0";

    [GeneratedRegex(@"^5\.\d{1,3}\.\d{1,3}$")]
    private static partial Regex PermanentStatus();

    /// <summary>Danh sách rỗng nếu thư không phải bounce vĩnh viễn. Nội dung thư không đáng tin: chỉ lấy email và mã đã kiểm dạng.</summary>
    public static IReadOnlyList<FailedRecipient> Parse(MimeMessage message)
    {
        List<FailedRecipient> found = new();
        foreach (MimeEntity part in message.BodyParts)
        {
            if (part is not MessageDeliveryStatus status)
            {
                continue;
            }
            foreach (HeaderList group in status.StatusGroups)
            {
                string? action = group["Action"]?.Trim();
                if (!string.Equals(action, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // delayed/delivered không phải lỗi vĩnh viễn
                }
                string? email = EmailOf(group["Final-Recipient"] ?? group["Original-Recipient"]);
                if (email == null)
                {
                    continue;
                }
                string code = group["Status"]?.Trim() ?? DefaultCode;
                found.Add(new FailedRecipient(email, PermanentStatus().IsMatch(code) ? code : DefaultCode));
            }
        }
        if (found.Count == 0)
        {
            // Một số máy chủ chỉ ghi X-Failed-Recipients trên thư báo lỗi.
            string? failed = message.Headers["X-Failed-Recipients"];
            foreach (string item in (failed ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string? email = EmailOf(item);
                if (email != null)
                {
                    found.Add(new FailedRecipient(email, DefaultCode));
                }
            }
        }
        return found;
    }

    /// <summary>"rfc822; a@b.com" hoặc "a@b.com" → "a@b.com" (chữ thường); không hợp lệ thì null.</summary>
    private static string? EmailOf(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        int semicolon = raw.IndexOf(';');
        string address = (semicolon >= 0 ? raw[(semicolon + 1)..] : raw).Trim();
        return MailboxAddress.TryParse(address, out MailboxAddress? mailbox)
            ? mailbox.Address.Trim().ToLowerInvariant()
            : null;
    }
}

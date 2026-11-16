using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Notifications;

/// <summary>
/// Kiểm tra phần tên miền của email có tồn tại không (lớp chặn sớm trước khi gửi thư).
/// Chỉ trả false khi chắc chắn tên miền không có; lỗi mạng hoặc DNS tạm lỗi coi như có để không chặn nhầm.
/// </summary>
public interface IEmailDomainChecker
{
    Task<bool> DomainExistsAsync(string email, CancellationToken ct);
}

public static class EmailDomainGuard
{
    public const string Message = "Email này không tồn tại hoặc không nhận được thư. Vui lòng kiểm tra lại địa chỉ.";

    /// <summary>Ném lỗi 400 trên trường <paramref name="field"/> khi tên miền email không tồn tại.</summary>
    public static async Task EnsureAsync(IEmailDomainChecker checker, string email, string field, CancellationToken ct)
    {
        if (!await checker.DomainExistsAsync(email, ct))
        {
            throw ValidationErrors.For(field, Message);
        }
    }
}

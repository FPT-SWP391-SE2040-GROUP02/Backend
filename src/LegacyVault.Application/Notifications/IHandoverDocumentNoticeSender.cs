namespace LegacyVault.Application.Notifications;

/// <summary>Gửi thư báo khi biên bản chuyển bước. Cài đặt ở Infrastructure (email hoặc chỉ ghi log).</summary>
public interface IHandoverDocumentNoticeSender
{
    Task SendAsync(HandoverDocumentNotice notice, CancellationToken ct);
}

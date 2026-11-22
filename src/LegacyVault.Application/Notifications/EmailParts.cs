namespace LegacyVault.Application.Notifications;

/// <summary>Các phần chữ của một thư thông báo: tiêu đề thư, tiêu đề trong thân, đoạn chính, chữ nút và ghi chú.</summary>
public sealed record EmailParts(string Subject, string Title, string Body, string ButtonText, string Note);

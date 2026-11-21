namespace LegacyVault.Application.Notifications;

/// <summary>Thư báo có yêu cầu nhận tài sản, gửi Chủ kho, Người thực thi hoặc luật sư của biên bản.</summary>
public sealed class HandoverRequestNotice
{
    public const string Owner = "OWNER";

    public const string Executor = "EXECUTOR";

    public const string Verifier = "VERIFIER";

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>OWNER, EXECUTOR hoặc VERIFIER: quyết định lời thư và đường dẫn.</summary>
    public string Audience { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Họ tên người nhận đã gửi yêu cầu.</summary>
    public string RequesterName { get; set; } = string.Empty;
}

/// <summary>Gửi thư báo có yêu cầu nhận tài sản. Gửi lỗi thì ném exception; người gọi tự bỏ qua.</summary>
public interface IHandoverRequestNoticeSender
{
    Task SendAsync(HandoverRequestNotice notice, CancellationToken ct);
}

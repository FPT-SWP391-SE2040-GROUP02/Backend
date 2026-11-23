namespace LegacyVault.Application.Notifications;

/// <summary>Thư báo có yêu cầu nhận tài sản, gửi Chủ kho, Người thực thi hoặc luật sư của biên bản.</summary>
public sealed class HandoverRequestNotice
{
    public const string Owner = "OWNER";

    public const string Executor = "EXECUTOR";

    public const string Verifier = "VERIFIER";

    /// <summary>Người nhận của biên bản (chỉ dùng với thư báo chuyển giao).</summary>
    public const string Recipient = "RECIPIENT";

    /// <summary>Thư báo có yêu cầu nhận tài sản (mặc định).</summary>
    public const string StageRequest = "REQUEST";

    /// <summary>Thư báo luật sư đã ký biên bản: Người thực thi bắt đầu giao.</summary>
    public const string StageTransfer = "TRANSFER";

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>OWNER, EXECUTOR, VERIFIER hoặc RECIPIENT: quyết định lời thư và đường dẫn.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>REQUEST (có yêu cầu nhận tài sản) hoặc TRANSFER (luật sư đã ký biên bản, bắt đầu giao).</summary>
    public string Stage { get; set; } = StageRequest;

    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Họ tên người nhận đã gửi yêu cầu.</summary>
    public string RequesterName { get; set; } = string.Empty;
}

/// <summary>Gửi thư báo có yêu cầu nhận tài sản. Gửi lỗi thì ném exception; người gọi tự bỏ qua.</summary>
public interface IHandoverRequestNoticeSender
{
    Task SendAsync(HandoverRequestNotice notice, CancellationToken ct);
}

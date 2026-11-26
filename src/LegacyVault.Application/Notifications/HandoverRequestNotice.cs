namespace LegacyVault.Application.Notifications;

/// <summary>Thư báo có yêu cầu nhận tài sản, gửi Chủ tài sản, Người thực thi hoặc luật sư của biên bản.</summary>
public sealed class HandoverRequestNotice
{
    public const string Owner = "OWNER";

    public const string Executor = "EXECUTOR";

    public const string Verifier = "VERIFIER";

    /// <summary>Người nhận của biên bản (chỉ dùng với thư báo tới bước ký).</summary>
    public const string Recipient = "RECIPIENT";

    /// <summary>Thư báo có yêu cầu nhận tài sản (mặc định).</summary>
    public const string StageRequest = "REQUEST";

    /// <summary>
    /// Thư báo tới lượt ký phần của một người nhận: luật sư ký phần X thì báo Người thực thi; Người thực thi ký phần X
    /// (danh tính X đã xong) thì báo người nhận X.
    /// </summary>
    public const string StageSigning = "SIGNING";

    /// <summary>Thư báo luật sư có hồ sơ báo tin Chủ tài sản đã mất cần xét; OwnerName là tên người mất do người nộp khai.</summary>
    public const string StageReport = "REPORT";

    /// <summary>Thư báo luật sư đã hủy biên bản đang lập: kho mở khóa.</summary>
    public const string StageCancelled = "CANCELLED";

    /// <summary>Thư báo Chủ tài sản và Người thực thi: luật sư đã chấp nhận giấy tờ nên kho đã khóa.</summary>
    public const string StageLocked = "LOCKED";

    /// <summary>Người thực thi cũ (chỉ dùng với thư đổi Người thực thi).</summary>
    public const string FormerExecutor = "FORMER_EXECUTOR";

    /// <summary>Thư báo Admin đổi Người thực thi khi đang bàn giao: người mới nhận việc ngay, người cũ thôi vai trò.</summary>
    public const string StageExecutorChanged = "EXECUTOR_CHANGED";

    /// <summary>Thư luật sư nhắc Người thực thi ký biên bản đã chốt.</summary>
    public const string StageExecutorReminder = "EXECUTOR_REMINDER";

    /// <summary>Thư báo luật sư: biên bản đã chốt, chỉ còn thiếu chữ ký của luật sư để mở quyền tải cho người nhận.</summary>
    public const string StageVerifierSignNeeded = "VERIFIER_SIGN_NEEDED";

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>OWNER, EXECUTOR, VERIFIER hoặc RECIPIENT: quyết định lời thư và đường dẫn.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// REQUEST (có yêu cầu nhận tài sản), SIGNING (biên bản tới bước ký), REPORT (có hồ sơ báo tin), CANCELLED (luật sư hủy)
    /// hoặc LOCKED (luật sư chấp nhận giấy tờ, kho đã khóa).
    /// </summary>
    public string Stage { get; set; } = StageRequest;

    public string OwnerName { get; set; } = string.Empty;

    /// <summary>Họ tên người nhận đã gửi yêu cầu; với thư SIGNING gửi Người thực thi là họ tên người nhận có phần cần ký.</summary>
    public string RequesterName { get; set; } = string.Empty;
}

/// <summary>Gửi thư báo có yêu cầu nhận tài sản. Gửi lỗi thì ném exception; người gọi tự bỏ qua.</summary>
public interface IHandoverRequestNoticeSender
{
    Task SendAsync(HandoverRequestNotice notice, CancellationToken ct);
}

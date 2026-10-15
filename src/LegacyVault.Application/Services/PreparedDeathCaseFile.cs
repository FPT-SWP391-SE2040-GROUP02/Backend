namespace LegacyVault.Application.Services;

/// <summary>
/// Một trang giấy chứng tử đã qua kiểm quyền và kiểm toàn vẹn, sẵn sàng ghi ra response.
/// Dùng chung cho mọi API xem trang chứng tử (TODO: Executor xem lại trang của chính hồ sơ).
/// </summary>
public sealed class PreparedDeathCaseFile
{
    public Guid CaseId { get; set; }

    public Guid FileId { get; set; }

    /// <summary>Người xem (lấy từ token), để controller ghi log "đã xem trang" (DATA-06).</summary>
    public Guid ViewerUserId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Length { get; set; }

    /// <summary>Giải mã lần hai và ghi thẳng vào luồng đích (response body).</summary>
    public Func<Stream, CancellationToken, Task> WriteToAsync { get; set; } = (stream, token) => Task.CompletedTask;
}

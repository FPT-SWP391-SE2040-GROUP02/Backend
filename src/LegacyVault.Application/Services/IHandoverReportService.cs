using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Hồ sơ báo tin Chủ tài sản đã mất: người thân nộp, luật sư xét rồi mở biên bản bàn giao.</summary>
public interface IHandoverReportService
{
    /// <summary>
    /// Nộp hồ sơ. Luôn báo đã tiếp nhận dù có tìm thấy kho hay không. Lỗi: 400 dữ liệu/tệp sai, 403 email chưa xác minh
    /// hoặc không phải tài khoản người dùng, 409 ALREADY_REPORTED, TOO_MANY_REPORTS, REPORT_LIMIT_REACHED, OWNER_CANNOT_CLAIM, NO_VERIFIER.
    /// </summary>
    Task<MyHandoverReportResponse> SubmitAsync(
        SubmitHandoverReportRequest request, HandoverReportUploads uploads, CancellationToken ct);

    Task<List<MyHandoverReportResponse>> ListMineAsync(CancellationToken ct);

    /// <summary>Nộp thêm giấy tờ; chỉ khi luật sư đang yêu cầu bổ sung.</summary>
    Task<MyHandoverReportResponse> SupplementAsync(
        Guid reportId, string? note, List<UploadedFile> files, CancellationToken ct);

    Task<PreparedDownload> PrepareMyFileAsync(Guid reportId, Guid fileId, CancellationToken ct);

    Task<List<VerifierHandoverReportResponse>> ListForVerifierAsync(CancellationToken ct);

    /// <summary>
    /// Luật sư xét: ACCEPT (tạo hoặc gắn vào biên bản, khóa kho, báo Chủ tài sản; có cảnh báo thì
    /// phải xác nhận đã đối chiếu giấy tờ và ghi lý do), REQUEST_SUPPLEMENT hoặc REJECT (kèm lý do).
    /// </summary>
    Task<VerifierHandoverReportResponse> ReviewAsync(
        Guid reportId, ReviewHandoverReportRequest request, CancellationToken ct);

    Task<PreparedDownload> PrepareVerifierFileAsync(Guid reportId, Guid fileId, CancellationToken ct);
}

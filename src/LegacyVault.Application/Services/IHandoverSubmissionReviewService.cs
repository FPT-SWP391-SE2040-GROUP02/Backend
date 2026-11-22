using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Người xét duyệt kiểm tra hồ sơ người nhận: xem danh sách và ảnh, gửi biên bản đã ký hoặc yêu cầu bổ sung.</summary>
public interface IHandoverSubmissionReviewService
{
    /// <summary>Hồ sơ người nhận đang chờ tôi duyệt (chỉ kho mà tôi đã duyệt hồ sơ chứng tử).</summary>
    Task<List<VerifierHandoverSubmissionSummaryResponse>> ListPendingAsync(CancellationToken ct);

    Task<VerifierHandoverSubmissionResponse> GetAsync(Guid submissionId, CancellationToken ct);

    /// <summary>Ảnh căn cước một mặt ("front" hoặc "back"), giải mã khi đọc, có kiểm toàn vẹn.</summary>
    Task<PreparedDownload> PrepareImageAsync(Guid submissionId, string side, CancellationToken ct);

    /// <summary>Duyệt: chỉ khi cả ba trường khớp. Lượt sang bước quyết định (READY_TO_ACCEPT).</summary>
    Task<VerifierHandoverSubmissionResponse> ApproveAsync(Guid submissionId, CancellationToken ct);

    /// <summary>Yêu cầu người nhận bổ sung kèm lý do; người nhận nộp lại bản mới.</summary>
    Task<VerifierHandoverSubmissionResponse> RequestSupplementAsync(
        Guid submissionId, RequestHandoverSupplementRequest request, CancellationToken ct);
}

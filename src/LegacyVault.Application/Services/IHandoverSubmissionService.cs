using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Người nhận nộp hồ sơ (họ tên, căn cước, ảnh hai mặt) và ký biên bản, hoặc xem hồ sơ mới nhất của mình.</summary>
public interface IHandoverSubmissionService
{
    /// <summary>
    /// Người nhận nộp hồ sơ (Chấp nhận hoặc Từ chối, thông tin khai trên văn bản mẫu và ảnh căn cước hai mặt) cho Người xét duyệt.
    /// Chỉ khi lượt đang ở bước xác minh danh tính. Nộp lại thì bản cũ bị thay thế.
    /// </summary>
    Task<HandoverSubmissionResponse> SubmitAsync(
        Guid turnId, SubmitHandoverSubmissionRequest request, byte[] frontImage, byte[] backImage, CancellationToken ct);

    /// <summary>Hồ sơ mới nhất của lượt này; null khi chưa nộp.</summary>
    Task<HandoverSubmissionResponse?> GetLatestAsync(Guid turnId, CancellationToken ct);
}

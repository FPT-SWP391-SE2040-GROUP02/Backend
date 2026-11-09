using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

public interface IHandoverSubmissionRepository
{
    void Add(HandoverSubmission submission);

    /// <summary>Hồ sơ mới nhất của một lượt (kể cả đã bị yêu cầu bổ sung hoặc đã duyệt); null khi chưa nộp.</summary>
    Task<HandoverSubmission?> FindLatestByTurnAsync(Guid turnId, CancellationToken ct);

    /// <summary>Hồ sơ theo id, kèm lượt bàn giao của nó.</summary>
    Task<HandoverSubmission?> FindByIdWithTurnAsync(Guid submissionId, CancellationToken ct);

    /// <summary>
    /// Hồ sơ đang chờ duyệt của các kho mà Người xét duyệt này là người đã duyệt hồ sơ chứng tử,
    /// cũ nhất trước. Hồ sơ của kho khác không bao giờ hiện cho người này.
    /// </summary>
    Task<List<HandoverSubmission>> ListPendingForVerifierAsync(Guid verifierUserId, CancellationToken ct);

    /// <summary>true khi Người xét duyệt này đã duyệt hồ sơ chứng tử của kho (căn cứ để được duyệt hồ sơ người nhận).</summary>
    /// <summary>Hồ sơ của các lượt đã dọn theo thời hạn lưu, để xóa ảnh căn cước.</summary>
    Task<List<HandoverSubmission>> ListByTurnIdsAsync(List<Guid> turnIds, CancellationToken ct);

    /// <summary>Người xét duyệt đã duyệt hồ sơ chứng tử của kho (họ tên, số thẻ luật sư); null khi chưa có.</summary>
    Task<(string FullName, string? LawyerCardNumber)?> FindApprovingVerifierAsync(Guid vaultId, CancellationToken ct);

    Task<bool> IsApprovingVerifierAsync(Guid verifierUserId, Guid vaultId, CancellationToken ct);
}

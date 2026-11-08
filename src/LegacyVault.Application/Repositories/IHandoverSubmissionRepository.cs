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
    Task<bool> IsApprovingVerifierAsync(Guid verifierUserId, Guid vaultId, CancellationToken ct);
}

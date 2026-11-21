using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Đọc và ghi biên bản bàn giao. Không chứa luật nghiệp vụ.</summary>
public interface IHandoverRecordRepository
{
    /// <summary>Biên bản kèm dòng và chữ ký, có theo dõi thay đổi; null khi không có.</summary>
    Task<HandoverRecord?> FindByIdAsync(Guid recordId, CancellationToken ct);

    /// <summary>Biên bản của kho kèm dòng và chữ ký, có theo dõi thay đổi; null khi kho chưa mở bàn giao.</summary>
    Task<HandoverRecord?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Các biên bản được giao cho một luật sư, mới nhất trước, kèm dòng và chữ ký.</summary>
    Task<List<HandoverRecord>> ListByVerifierAsync(Guid verifierUserId, CancellationToken ct);

    /// <summary>
    /// Luật sư đang hoạt động được giao ít gần đây nhất (chưa từng được giao đứng đầu), bỏ các tài khoản trong danh sách
    /// loại trừ và tài khoản có email trùng người nhận. Null khi không còn ai.
    /// </summary>
    Task<Guid?> FindVerifierCandidateAsync(List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct);

    /// <summary>Mọi tài sản còn nội dung của kho, theo ngày tạo. Chỉ đọc.</summary>
    Task<List<Asset>> ListVaultAssetsAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Mọi hồ sơ người nhận của kho, theo ngày tạo. Chỉ đọc.</summary>
    Task<List<Beneficiary>> ListVaultBeneficiariesAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Các lời mời Người thực thi đã chấp nhận của tài khoản, kèm kho, Chủ kho và kế hoạch. Chỉ đọc.</summary>
    Task<List<Executor>> ListAcceptedExecutorsAsync(Guid userId, CancellationToken ct);

    void Add(HandoverRecord record);
    /// <summary>Các lượt do biên bản tạo, kèm tài sản đã ghim, có theo dõi thay đổi.</summary>
    Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid recordId, CancellationToken ct);

    void RemoveSnapshotItem(HandoverSnapshotItem item);
    void AddSnapshotItem(HandoverSnapshotItem item);
    void RemoveLine(HandoverRecordLine line);
    void AddLine(HandoverRecordLine line);
    void AddSignature(HandoverRecordSignature signature);
}

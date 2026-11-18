using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng HandoverDocuments. Chỉ đánh dấu Add/sửa; service lưu một lần bằng IUnitOfWork.</summary>
public interface IHandoverDocumentRepository
{
    void Add(HandoverDocument document);

    /// <summary>Biên bản theo id, kèm lượt bàn giao (có theo dõi). Không có thì null.</summary>
    Task<HandoverDocument?> FindByIdAsync(Guid documentId, CancellationToken ct);

    /// <summary>Biên bản của một lượt, kèm lượt (có theo dõi). Chưa có thì null.</summary>
    Task<HandoverDocument?> FindByTurnIdAsync(Guid turnId, CancellationToken ct);

    /// <summary>Biên bản của các lượt mà tài khoản này đã nhận, mới nhất trước.</summary>
    Task<List<HandoverDocument>> ListByRecipientAsync(Guid userId, CancellationToken ct);

    /// <summary>Biên bản của các kho cho trước, mới nhất trước.</summary>
    Task<List<HandoverDocument>> ListByVaultIdsAsync(List<Guid> vaultIds, CancellationToken ct);

    /// <summary>Biên bản của các kho mà Người xét duyệt này đã duyệt hồ sơ chứng tử, mới nhất trước.</summary>
    Task<List<HandoverDocument>> ListForVerifierAsync(Guid verifierUserId, CancellationToken ct);

    /// <summary>
    /// Số liệu kèm theo danh sách biên bản của Người xét duyệt: ngày duyệt hồ sơ báo tin, tổng người nhận của kho,
    /// thời điểm nộp của từng hồ sơ và số lần nộp của từng lượt. Chỉ đọc.
    /// </summary>
    Task<VerifierDocumentContext> LoadVerifierContextAsync(
        Guid verifierUserId, List<Guid> vaultIds, List<Guid> turnIds, CancellationToken ct);
}

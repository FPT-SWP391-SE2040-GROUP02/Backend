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

/// <summary>Số liệu đọc thêm cho danh sách biên bản của Người xét duyệt (xem LoadVerifierContextAsync).</summary>
public sealed class VerifierDocumentContext
{
    /// <summary>Kho -> lúc Người xét duyệt này duyệt hồ sơ báo tin gần nhất của kho.</summary>
    public Dictionary<Guid, DateTimeOffset> CaseApprovedAtByVault { get; } = new Dictionary<Guid, DateTimeOffset>();

    /// <summary>Kho -> số người nhận (số chỉ định khác nhau có lượt bàn giao, không tính lượt đã hủy hoặc đã dọn).</summary>
    public Dictionary<Guid, int> RecipientTotalByVault { get; } = new Dictionary<Guid, int>();

    /// <summary>Id hồ sơ người nhận -> lúc người nhận nộp hồ sơ đó.</summary>
    public Dictionary<Guid, DateTimeOffset> SubmittedAtBySubmission { get; } = new Dictionary<Guid, DateTimeOffset>();

    /// <summary>Lượt -> số lần người nhận đã nộp hồ sơ (lớn hơn 1 nghĩa là đã nộp lại).</summary>
    public Dictionary<Guid, int> SubmissionCountByTurn { get; } = new Dictionary<Guid, int>();
}

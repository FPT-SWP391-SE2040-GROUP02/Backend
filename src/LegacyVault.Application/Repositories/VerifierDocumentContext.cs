namespace LegacyVault.Application.Repositories;

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

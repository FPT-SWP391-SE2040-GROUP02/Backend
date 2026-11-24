using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Đọc và ghi hồ sơ báo tin Chủ kho đã mất. Không chứa luật nghiệp vụ.</summary>
public interface IHandoverReportRepository
{
    /// <summary>Hồ sơ kèm tệp, có theo dõi thay đổi; null khi không có.</summary>
    Task<HandoverReport?> FindByIdAsync(Guid reportId, CancellationToken ct);

    /// <summary>Hồ sơ tài khoản này đã nộp, mới nhất trước, kèm tệp. Chỉ đọc.</summary>
    Task<List<HandoverReport>> ListBySubmitterAsync(Guid userId, CancellationToken ct);

    /// <summary>Hồ sơ được giao cho luật sư, mới nhất trước, kèm tệp. Chỉ đọc.</summary>
    Task<List<HandoverReport>> ListByVerifierAsync(Guid verifierUserId, CancellationToken ct);

    /// <summary>Hồ sơ đã gắn vào biên bản, cũ nhất trước, kèm tệp. Chỉ đọc.</summary>
    Task<List<HandoverReport>> ListByRecordAsync(Guid recordId, CancellationToken ct);

    /// <summary>Số hồ sơ báo tin của kho đang chờ xét (chờ xét hoặc chờ bổ sung), bỏ qua hồ sơ exceptReportId nếu có.</summary>
    Task<int> CountPendingByVaultAsync(Guid vaultId, Guid? exceptReportId, CancellationToken ct);

    void Add(HandoverReport report);

    void AddFile(HandoverReportFile file);
}

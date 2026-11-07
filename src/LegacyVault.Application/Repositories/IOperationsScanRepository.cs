using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy vấn của job quét sự cố vận hành: tìm các hồ sơ và kho đang rơi vào tình huống cần Admin chú ý.</summary>
public interface IOperationsScanRepository
{
    /// <summary>Hồ sơ đang xét hoặc cần bổ sung mà phân công còn hiệu lực đã quá hạn xét duyệt.</summary>
    Task<List<StaleCaseRow>> ListStaleCasesAsync(DateTimeOffset now, int take, CancellationToken ct);

    /// <summary>Hồ sơ đang xét hoặc cần bổ sung đã bị yêu cầu bổ sung từ minCount lần trở lên.</summary>
    Task<List<CaseRow>> ListSupplementLoopCasesAsync(int minCount, int take, CancellationToken ct);

    /// <summary>Kho có gói trả phí còn hiệu lực nhưng hết kỳ dịch vụ trong khoảng (now, until].</summary>
    Task<List<Guid>> ListExpiringVaultIdsAsync(DateTimeOffset now, DateTimeOffset until, int take, CancellationToken ct);
}

public sealed record StaleCaseRow(Guid CaseId, Guid VaultId, Guid VerifierUserId);

public sealed record CaseRow(Guid CaseId, Guid VaultId);

using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IOperationsScanner
{
    /// <summary>
    /// Quét một vòng: mở sự cố cho hồ sơ quá hạn xét, hồ sơ bị yêu cầu bổ sung nhiều lần và kho sắp hết gói;
    /// đóng sự cố đã hết đúng. Chạy lặp an toàn (không mở trùng). Trả số sự cố mở mới và số sự cố đã đóng.
    /// </summary>
    Task<(int Opened, int Resolved)> ScanAsync(CancellationToken ct);
}

/// <remarks>
/// - Sự cố hồ sơ quá hạn xét giao thẳng cho Verifier đang giữ hồ sơ; hai loại còn lại do Admin ít việc nhất nhận.
/// - Mỗi vòng giới hạn số dòng để không quét quá nhiều; vòng sau tiếp tục.
/// - Sự cố chỉ mang loại, mã đối tượng và câu tóm tắt chung, không có nội dung hồ sơ hay định danh (SEC-07).
/// </remarks>
public sealed class OperationsScanner : IOperationsScanner
{
    public const string TargetVault = "VAULT";

    private const int BatchSize = 100;

    private readonly IOperationsScanRepository scan;
    private readonly IIncidentRepository incidents;
    private readonly IIncidentService incidentService;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public OperationsScanner(
        IOperationsScanRepository scan,
        IIncidentRepository incidents,
        IIncidentService incidentService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.scan = scan;
        this.incidents = incidents;
        this.incidentService = incidentService;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    /// <summary>Số lần yêu cầu bổ sung từ đó coi là vòng lặp bổ sung.</summary>
    public static int SupplementLoopThreshold => RuntimeSettings.Int("Incident.SupplementLoopCount", 3);

    /// <summary>Số ngày trước khi hết gói thì báo Admin.</summary>
    public static int PlanExpiringDays => RuntimeSettings.Int("Incident.PlanExpiringDays", 30);

    public async Task<(int Opened, int Resolved)> ScanAsync(CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        int opened = 0;
        int resolved = 0;

        // Hồ sơ quá hạn xét duyệt: giao cho Verifier đang giữ hồ sơ.
        List<StaleCaseRow> stale = await scan.ListStaleCasesAsync(now, BatchSize, ct);
        List<Guid> openStale = await incidents.ListOpenTargetIdsAsync(IncidentType.StaleCase, ct);
        foreach (StaleCaseRow row in stale)
        {
            if (!openStale.Contains(row.CaseId))
            {
                await incidentService.RaiseAsync(IncidentType.StaleCase, row.VaultId, IncidentRules.TargetDeathCase, row.CaseId,
                    "Hồ sơ chứng tử quá hạn xét duyệt.", row.VerifierUserId, ct);
                opened++;
            }
        }
        resolved += await ResolveGoneAsync(
            IncidentType.StaleCase, IncidentRules.TargetDeathCase, openStale, stale.Select(r => r.CaseId), "Hồ sơ đã được xét xong hoặc còn trong hạn.", ct);

        // Hồ sơ bị yêu cầu bổ sung nhiều lần.
        List<CaseRow> loops = await scan.ListSupplementLoopCasesAsync(SupplementLoopThreshold, BatchSize, ct);
        List<Guid> openLoops = await incidents.ListOpenTargetIdsAsync(IncidentType.SupplementLoop, ct);
        foreach (CaseRow row in loops)
        {
            if (!openLoops.Contains(row.CaseId))
            {
                await incidentService.RaiseAsync(IncidentType.SupplementLoop, row.VaultId, IncidentRules.TargetDeathCase, row.CaseId,
                    "Hồ sơ chứng tử bị yêu cầu bổ sung nhiều lần, cần xem vì sao chưa xong.", null, ct);
                opened++;
            }
        }
        resolved += await ResolveGoneAsync(
            IncidentType.SupplementLoop, IncidentRules.TargetDeathCase, openLoops, loops.Select(r => r.CaseId), "Hồ sơ đã có kết luận.", ct);

        // Kho sắp hết gói trả phí.
        List<Guid> expiring = await scan.ListExpiringVaultIdsAsync(now, now.AddDays(PlanExpiringDays), BatchSize, ct);
        List<Guid> openExpiring = await incidents.ListOpenTargetIdsAsync(IncidentType.PlanExpiring, ct);
        foreach (Guid vaultId in expiring)
        {
            if (!openExpiring.Contains(vaultId))
            {
                await incidentService.RaiseAsync(IncidentType.PlanExpiring, vaultId, TargetVault, vaultId,
                    "Gói trả phí của kho sắp hết hạn.", null, ct);
                opened++;
            }
        }
        resolved += await ResolveGoneAsync(
            IncidentType.PlanExpiring, TargetVault, openExpiring, expiring, "Gói đã gia hạn hoặc không còn sắp hết hạn.", ct);

        if (opened > 0 || resolved > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        return (opened, resolved);
    }

    private async Task<int> ResolveGoneAsync(
        IncidentType type, string targetType, List<Guid> openTargets, IEnumerable<Guid> stillMatching, string note, CancellationToken ct)
    {
        HashSet<Guid> matching = new HashSet<Guid>(stillMatching);
        int count = 0;
        foreach (Guid target in openTargets)
        {
            if (!matching.Contains(target))
            {
                await incidentService.ResolveByTargetAsync(type, targetType, target, note, ct);
                count++;
            }
        }
        return count;
    }
}

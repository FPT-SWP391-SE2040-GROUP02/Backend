using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
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

    public const string TargetPlan = "PLAN";
    public const string TargetService = "SERVICE";

    private static readonly string[] WatchedServices =
    {
        SystemConfigServices.Ekyc, SystemConfigServices.Smtp, SystemConfigServices.SePay, SystemConfigServices.R2,
    };

    /// <summary>Mã đối tượng cố định cho một dịch vụ ngoài, để cùng một dịch vụ không mở trùng sự cố.</summary>
    public static Guid ServiceTargetId(string serviceKey)
    {
        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes("service:" + serviceKey)));
    }

    private readonly IOperationsScanRepository scan;
    private readonly IServiceStatusTracker serviceStatus;
    private readonly IExecutorReader executorReader;
    private readonly ICheckInRepository checkIns;
    private readonly IIncidentRepository incidents;
    private readonly IIncidentService incidentService;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public OperationsScanner(
        IOperationsScanRepository scan,
        IServiceStatusTracker serviceStatus,
        IExecutorReader executorReader,
        ICheckInRepository checkIns,
        IIncidentRepository incidents,
        IIncidentService incidentService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.scan = scan;
        this.serviceStatus = serviceStatus;
        this.executorReader = executorReader;
        this.checkIns = checkIns;
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
                    "Hồ sơ chứng tử bị yêu cầu bổ sung nhiều lần, cần xem vì sao chưa xong.",
                    await ExecutorOfAsync(row.VaultId, ct), ct);
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
                    "Gói trả phí của kho sắp hết hạn.", await ExecutorOfAsync(vaultId, ct), ct);
                opened++;
            }
        }
        resolved += await ResolveGoneAsync(
            IncidentType.PlanExpiring, TargetVault, openExpiring, expiring, "Gói đã gia hạn hoặc không còn sắp hết hạn.", ct);

        // Hết lượt điểm danh thay: Executor không điểm danh hộ được nữa nên kho kẹt ở trạng thái quá hạn.
        List<PlanRow> overdue = await scan.ListOverduePlansAsync(BatchSize, ct);
        List<Guid> openProxy = await incidents.ListOpenTargetIdsAsync(IncidentType.ProxyLimit, ct);
        List<Guid> reached = new List<Guid>();
        int limit = CheckInRules.ExecutorProxyLimit;
        foreach (PlanRow row in overdue)
        {
            if (await checkIns.CountConsecutiveExecutorPeriodsAsync(row.PlanId, ct) < limit)
            {
                continue;
            }
            reached.Add(row.PlanId);
            if (!openProxy.Contains(row.PlanId))
            {
                await incidentService.RaiseAsync(IncidentType.ProxyLimit, row.VaultId, TargetPlan, row.PlanId,
                    "Executor đã dùng hết lượt điểm danh thay mà chủ kho vẫn quá hạn.", await ExecutorOfAsync(row.VaultId, ct), ct);
                opened++;
            }
        }
        resolved += await ResolveGoneAsync(
            IncidentType.ProxyLimit, TargetPlan, openProxy, reached, "Chủ kho đã điểm danh hoặc kế hoạch không còn quá hạn.", ct);

        // Dịch vụ ngoài lỗi: lần gọi gần nhất thất bại thì mở sự cố cho Admin, thành công lại thì đóng.
        List<Guid> openService = await incidents.ListOpenTargetIdsAsync(IncidentType.Integration, ct);
        List<Guid> failing = new List<Guid>();
        foreach (string key in WatchedServices)
        {
            ServiceStatus status = serviceStatus.Get(key);
            if (status.LastSucceeded != false)
            {
                continue;
            }
            Guid target = ServiceTargetId(key);
            failing.Add(target);
            if (!openService.Contains(target))
            {
                string code = string.IsNullOrWhiteSpace(status.LastErrorCode) ? "không rõ mã lỗi" : status.LastErrorCode;
                await incidentService.RaiseAsync(IncidentType.Integration, null, TargetService, target,
                    "Dịch vụ ngoài " + key + " gọi lỗi (" + code + ").", null, ct);
                opened++;
            }
        }
        resolved += await ResolveGoneAsync(
            IncidentType.Integration, TargetService, openService, failing, "Dịch vụ ngoài đã gọi lại được.", ct);

        if (opened > 0 || resolved > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        return (opened, resolved);
    }

    /// <summary>Executor đang hoạt động đầu tiên của kho; không có thì null và sự cố rơi về Admin ít việc nhất.</summary>
    private async Task<Guid?> ExecutorOfAsync(Guid vaultId, CancellationToken ct)
    {
        List<Executor> active = await executorReader.GetAvailableAsync(vaultId, ct);
        if (active.Count == 0)
        {
            return null;
        }
        return active[0].UserId;
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

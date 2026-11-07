using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Payment;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Job quét sự cố vận hành: hồ sơ quá hạn xét, bổ sung nhiều lần, gói sắp hết; chạy trên SQL Server database tạm.</summary>
public sealed class OperationsScannerTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
        // Cần ít nhất một Admin để nhận sự cố chưa có người phụ trách.
        await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private async Task<(int Opened, int Resolved)> ScanAsync()
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            return await scope.ServiceProvider.GetRequiredService<IOperationsScanner>().ScanAsync(CancellationToken.None);
        }
    }

    private async Task<List<Incident>> IncidentsOfAsync(IncidentType type)
    {
        List<Incident> found = new List<Incident>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            found = await db.Set<Incident>().AsNoTracking().Where(i => i.Type == type).ToListAsync();
        });
        return found;
    }

    [SqlFact]
    public async Task StaleCase_OpensForOverdueAssignment_AssignsVerifier_AndResolvesWhenInTime()
    {
        // Chuẩn bị: hồ sơ đang xét, hạn xét của phân công đã qua
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseAssignment>().Where(a => a.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DueAt, DateTimeOffset.UtcNow.AddHours(-2)));
        });

        // Hành động
        (int opened, int _) = await ScanAsync();
        (int openedAgain, int _) = await ScanAsync();

        // Kiểm tra: mở một sự cố giao Verifier, vòng sau không mở trùng
        Assert.Equal(1, opened);
        Assert.Equal(0, openedAgain);
        Incident incident = Assert.Single(await IncidentsOfAsync(IncidentType.StaleCase));
        Assert.Equal(submitted.CaseId, incident.TargetId);
        Assert.Equal(submitted.Verifier.User.Id, incident.AssigneeUserId);
        Assert.Equal(IncidentStatus.Open, incident.Status);

        // Hạn dời ra tương lai: vòng sau đóng sự cố
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseAssignment>().Where(a => a.CaseId == submitted.CaseId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DueAt, DateTimeOffset.UtcNow.AddDays(3)));
        });
        (int _, int resolved) = await ScanAsync();
        Assert.Equal(1, resolved);
        Assert.Equal(IncidentStatus.Resolved, Assert.Single(await IncidentsOfAsync(IncidentType.StaleCase)).Status);
    }

    [SqlFact]
    public async Task SupplementLoop_OpensAfterThreeSupplementRequests_AssignedToAdmin()
    {
        // Chuẩn bị: hồ sơ đang xét đã bị yêu cầu bổ sung ba lần
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseAssignment assignment = await db.Set<DeathCaseAssignment>().AsNoTracking()
                .FirstAsync(x => x.CaseId == submitted.CaseId);
            for (int version = 1; version <= 3; version++)
            {
                DeathCaseDecision decision = new DeathCaseDecision();
                decision.CaseId = submitted.CaseId;
                decision.VersionNo = version;
                decision.Kind = DeathCaseDecisionKind.SupplementRequested;
                decision.Reason = "Cần bổ sung.";
                decision.VerifierUserId = submitted.Verifier.User.Id;
                decision.AssignmentId = assignment.Id;
                decision.DecidedAt = DateTimeOffset.UtcNow.AddHours(-version);
                db.Set<DeathCaseDecision>().Add(decision);
            }
            await db.SaveChangesAsync();
        });

        // Hành động
        (int opened, int _) = await ScanAsync();

        // Kiểm tra: một sự cố, người phụ trách là Admin chứ không phải Verifier
        Assert.Equal(1, opened);
        Incident incident = Assert.Single(await IncidentsOfAsync(IncidentType.SupplementLoop));
        Assert.Equal(submitted.CaseId, incident.TargetId);
        Assert.NotEqual(submitted.Verifier.User.Id, incident.AssigneeUserId);
        Assert.NotNull(incident.AssigneeUserId);
    }

    [SqlFact]
    public async Task CaseInTime_OpensNothing()
    {
        // Chuẩn bị: hồ sơ mới gửi, còn trong hạn
        await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);

        // Hành động
        (int opened, int resolved) = await ScanAsync();

        // Kiểm tra
        Assert.Equal(0, opened);
        Assert.Equal(0, resolved);
    }

    [SqlFact]
    public async Task PlanExpiring_OpensForPaidVaultEndingSoon_AndResolvesAfterRenewal()
    {
        // Chuẩn bị: kho trả phí còn 10 ngày
        (HttpClient _, Guid vaultId, Guid _) = await BillingTestData.CreateFreeVaultAsync(factory, 1);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<VaultEntitlement>().Where(e => e.VaultId == vaultId).ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EntitlementStatus.PaidActive)
                .SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(10)));
        });

        // Hành động
        (int opened, int _) = await ScanAsync();

        // Kiểm tra
        Assert.Equal(1, opened);
        Incident incident = Assert.Single(await IncidentsOfAsync(IncidentType.PlanExpiring));
        Assert.Equal(vaultId, incident.VaultId);

        // Gia hạn thêm một năm: sự cố được đóng
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<VaultEntitlement>().Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(365)));
        });
        (int _, int resolved) = await ScanAsync();
        Assert.Equal(1, resolved);
    }

    [SqlFact]
    public async Task FreeVault_IsNeverExpiring()
    {
        // Chuẩn bị: kho miễn phí không có kỳ dịch vụ
        await BillingTestData.CreateFreeVaultAsync(factory, 1);

        // Hành động
        (int opened, int _) = await ScanAsync();

        // Kiểm tra
        Assert.Equal(0, opened);
    }
}

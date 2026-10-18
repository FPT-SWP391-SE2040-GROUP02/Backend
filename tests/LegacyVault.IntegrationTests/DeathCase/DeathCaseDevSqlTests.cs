using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Chạy thật file http/death-case-dev.sql trên database tạm để chắc script thử đúng: dọn được cả quyết định và
/// dữ liệu bàn giao sau khi đã duyệt (chạy lại được), và các lệnh "Cho phép duyệt" trong phần ghi chú chạy được.
/// </summary>
public sealed class DeathCaseDevSqlTests : IAsyncLifetime
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

    private static string FindScriptPath()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "http", "death-case-dev.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Không tìm thấy http/death-case-dev.sql");
    }

    private async Task RunSqlAsync(string sql)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            using SqlConnection connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            using SqlCommand command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        });
    }

    [SqlFact]
    public async Task DevScript_CleansDecisionsAndHandoverAfterApproveAndAllowsApproveAgain()
    {
        // Chuẩn bị: hồ sơ đã được duyệt (có quyết định, lượt bàn giao, kế hoạch HANDOVER_IN_PROGRESS)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        HttpResponseMessage approved = await DeathCaseReviewTestData.ApproveNowAsync(submitted, "Duyệt thử.");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        string[] lines = await File.ReadAllLinesAsync(FindScriptPath());
        string script = string.Join("\n", lines)
            .Replace("<owner@email>", submitted.Vault.Owner.Email)
            .Replace("<executor@email>", submitted.Executor.User.Email)
            .Replace("<verifier@email>", submitted.Verifier.User.Email);

        // Hành động: chạy script (dọn hồ sơ, quyết định, lượt bàn giao cũ rồi dựng lại dữ liệu thử)
        await RunSqlAsync(script);

        // Kiểm tra: không còn hồ sơ, quyết định, lượt; kế hoạch ACTIVE; Executor mở được hồ sơ mới
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.Set<DeathCase>().CountAsync(c => c.VaultId == submitted.Vault.VaultId));
            Assert.Equal(0, await db.Set<DeathCaseDecision>().CountAsync());
            Assert.Equal(0, await db.Set<HandoverTurn>().CountAsync(t => t.VaultId == submitted.Vault.VaultId));
        });
        HandoverPlan plan = await DeathCaseReviewTestData.LoadPlanAsync(factory, submitted.Vault.VaultId);
        Assert.Equal(PlanStatus.Active, plan.Status);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            submitted.Executor.Client, submitted.Vault.VaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement resubmitted = await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, prepared);
        Guid newCaseId = resubmitted.GetProperty("id").GetGuid();
        Assert.NotEqual(submitted.CaseId, newCaseId);

        // Các lệnh "Cho phép duyệt" trong phần ghi chú của script chạy được và làm hồ sơ mới duyệt được
        string vaultLiteral = "DECLARE @vaultId uniqueidentifier = '" + submitted.Vault.VaultId + "';\n";
        foreach (string line in lines)
        {
            bool isAllowApprove = line.StartsWith("--   UPDATE n SET Status = 'Delivered'", StringComparison.Ordinal)
                || line.StartsWith("--   UPDATE DeathCases SET ObjectionStartsAt = DATEADD(HOUR, -80", StringComparison.Ordinal);
            if (isAllowApprove)
            {
                await RunSqlAsync(vaultLiteral + line.Substring("--   ".Length));
            }
        }
        JsonElement detail = await DeathCaseReviewTestData.GetDetailAsync(submitted.Verifier.Client, newCaseId);
        Assert.True(detail.GetProperty("canApprove").GetBoolean());
        HttpResponseMessage secondApprove = await DeathCaseReviewTestData.ApproveAsync(
            submitted.Verifier.Client, newCaseId, detail.GetProperty("etag").GetString(), "Duyệt lần hai.");
        Assert.Equal(HttpStatusCode.OK, secondApprove.StatusCode);
    }
}

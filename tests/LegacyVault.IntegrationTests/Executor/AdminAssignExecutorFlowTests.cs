using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.ExecutorAssign;

/// <summary>Admin giao Người thực thi từ đầu đến cuối: API thật, lời mời dùng lời của quản trị viên, nhật ký, quyền.</summary>
public sealed class AdminAssignExecutorFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private readonly Mail mail = new Mail();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IExecutorInvitationSender>();
            services.AddSingleton<IExecutorInvitationSender>(mail);
        };
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

    /// <summary>Ghi lại đã gọi lối gửi nào để kiểm tra lời mời do Admin giao không đi lối "Chủ kho mời".</summary>
    private sealed class Mail : IExecutorInvitationSender
    {
        public int Normal { get; private set; }

        public int ByAdmin { get; private set; }

        public string Token { get; private set; } = string.Empty;

        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Normal++;
            Token = token;
            return Task.FromResult(true);
        }

        public Task<bool> SendAssignedByAdminAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            ByAdmin++;
            Token = token;
            return Task.FromResult(true);
        }
    }

    private async Task<HttpClient> ClientForAsync(UserRole role)
    {
        User user = await DeathCaseTestData.AddUserAsync(factory, role, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    [SqlFact]
    public async Task Admin_AssignsExecutor_InvitesWithAdminWording_AndWritesAudit()
    {
        // Chuẩn bị: kho đang chạy chưa có Người thực thi, và một tài khoản thường được chọn
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User target = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient admin = await ClientForAsync(UserRole.Admin);

        // Hành động
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/admin/vaults/" + vault.VaultId + "/executor",
            new { userId = target.Id, reason = "Người cũ không còn liên lạc được." });

        // Kiểm tra: lời mời đã tạo, đi lối thư của Admin (không phải thư "Chủ kho mời"), có nhật ký
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(target.Email, body.GetProperty("inviteeEmail").GetString());
        Assert.Equal(1, mail.ByAdmin);
        Assert.Equal(0, mail.Normal);
        Assert.False(string.IsNullOrEmpty(mail.Token));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.ExecutorAssignedByAdmin));
            Assert.Equal(1, await db.Set<Executor>().CountAsync(e => e.VaultId == vault.VaultId));
        });
    }

    [SqlFact]
    public async Task Assign_RejectsOwnerAndMissingReason_AndNonAdminGets404()
    {
        // Chuẩn bị
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        User target = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient admin = await ClientForAsync(UserRole.Admin);
        HttpClient member = await ClientForAsync(UserRole.User);
        string url = "/api/v1/admin/vaults/" + vault.VaultId + "/executor";

        // Hành động
        HttpResponseMessage toOwner = await admin.PostAsJsonAsync(url, new { userId = vault.Owner.Id, reason = "thử" });
        HttpResponseMessage noReason = await admin.PostAsJsonAsync(url, new { userId = target.Id, reason = "" });
        HttpResponseMessage nonAdmin = await member.PostAsJsonAsync(url, new { userId = target.Id, reason = "thử" });

        // Kiểm tra: không giao cho chủ kho (409), bắt buộc lý do, người thường không thấy khu Admin; không gửi thư nào
        Assert.Equal(HttpStatusCode.Conflict, toOwner.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nonAdmin.StatusCode);
        Assert.Equal(0, mail.ByAdmin + mail.Normal);
    }
}

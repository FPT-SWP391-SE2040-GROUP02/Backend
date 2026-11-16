using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// Người thực thi "ký" nhận vai bằng mật khẩu tài khoản khi chấp nhận lời mời (từ chối không cần mật khẩu).
/// Dùng chung ISigningPasswordChecker với Verifier: sai 5 lần thì khóa, bộ đếm vẫn được lưu dù request trả lỗi.
/// </summary>
public sealed class ExecutorSigningPasswordTests : IAsyncLifetime
{
    private const string ExecutorEmail = "ky.nhan.vai@example.test";
    private const string WrongPassword = "Sai-mat-khau-9";

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

    private sealed class Mail : IExecutorInvitationSender
    {
        public string Token { get; private set; } = string.Empty;

        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Token = token;
            return Task.FromResult(true);
        }
    }

    private sealed record Setup(HttpClient Executor, Guid InvitationId, Guid VaultId, Guid ExecutorUserId);

    private async Task<Setup> PrepareAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(365)));
        });
        HttpClient executor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, ExecutorEmail);
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, ExecutorEmail);
        HttpResponseMessage invite = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email = ExecutorEmail });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        ExecutorInvitationResponse? invitation = await invite.Content.ReadFromJsonAsync<ExecutorInvitationResponse>();
        Assert.NotNull(invitation);

        Guid executorUserId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            User user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == ExecutorEmail);
            executorUserId = user.Id;
        });
        return new Setup(executor, invitation.Id, vaultId, executorUserId);
    }

    private static Task<HttpResponseMessage> RespondByIdAsync(Setup setup, bool accept, string? password)
    {
        return setup.Executor.PostAsJsonAsync($"/api/v1/executor-invitations/{setup.InvitationId}/respond",
            new { accept, acknowledgeScope = accept, scopeVersion = PlanSetupService.ScopeVersion, password });
    }

    private async Task<ExecutorStatus> LoadStatusAsync(Guid invitationId)
    {
        ExecutorStatus status = ExecutorStatus.Invited;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Executor item = await db.Set<Executor>().AsNoTracking().SingleAsync(e => e.Id == invitationId);
            status = item.Status;
        });
        return status;
    }

    [SqlFact]
    public async Task Accept_WithoutPassword_Returns400OnPasswordField()
    {
        Setup setup = await PrepareAsync();

        HttpResponseMessage missing = await RespondByIdAsync(setup, true, null);
        HttpResponseMessage blank = await RespondByIdAsync(setup, true, "   ");

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(missing, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("Nhập mật khẩu tài khoản để ký nhận vai Người thực thi.",
            problem.GetProperty("errors").GetProperty("password")[0].GetString());
        await ApiTestHelpers.AssertProblemAsync(blank, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(ExecutorStatus.Invited, await LoadStatusAsync(setup.InvitationId));
    }

    [SqlFact]
    public async Task Accept_WrongPassword_Returns403WithAttemptsAndStaysInvited()
    {
        Setup setup = await PrepareAsync();

        HttpResponseMessage first = await RespondByIdAsync(setup, true, WrongPassword);
        HttpResponseMessage second = await RespondByIdAsync(setup, true, WrongPassword);

        JsonElement firstProblem = await ApiTestHelpers.AssertProblemAsync(first, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.Equal(4, firstProblem.GetProperty("attemptsRemaining").GetInt32());
        // Bộ đếm phải được lưu dù request lỗi (kiểm mật khẩu nằm ngoài giao dịch serializable).
        JsonElement secondProblem = await ApiTestHelpers.AssertProblemAsync(second, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.Equal(3, secondProblem.GetProperty("attemptsRemaining").GetInt32());
        Assert.Equal(ExecutorStatus.Invited, await LoadStatusAsync(setup.InvitationId));
    }

    [SqlFact]
    public async Task Accept_FiveWrongPasswords_LocksEvenCorrectPassword()
    {
        Setup setup = await PrepareAsync();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            await ApiTestHelpers.AssertProblemAsync(await RespondByIdAsync(setup, true, WrongPassword),
                HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        }
        HttpResponseMessage fifth = await RespondByIdAsync(setup, true, WrongPassword);
        HttpResponseMessage correctWhileLocked = await RespondByIdAsync(setup, true, ApiTestHelpers.ExecutorPassword);

        JsonElement locked = await ApiTestHelpers.AssertProblemAsync(fifth, (HttpStatusCode)423, "SIGNING_LOCKED");
        Assert.True(locked.TryGetProperty("lockedUntil", out JsonElement _));
        await ApiTestHelpers.AssertProblemAsync(correctWhileLocked, (HttpStatusCode)423, "SIGNING_LOCKED");
        Assert.Equal(ExecutorStatus.Invited, await LoadStatusAsync(setup.InvitationId));
    }

    [SqlFact]
    public async Task Accept_CorrectPassword_AcceptsAndWritesAudit()
    {
        Setup setup = await PrepareAsync();

        HttpResponseMessage response = await RespondByIdAsync(setup, true, ApiTestHelpers.ExecutorPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExecutorStatus.Accepted, await LoadStatusAsync(setup.InvitationId));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            int count = await db.AuditLogs.CountAsync(l => l.VaultId == setup.VaultId
                && l.ActorUserId == setup.ExecutorUserId && l.Action == AuditAction.ExecutorAccepted);
            Assert.Equal(1, count);
        });
    }

    [SqlFact]
    public async Task Accept_ByToken_WrongPasswordRejected_CorrectAccepted()
    {
        Setup setup = await PrepareAsync();
        string token = mail.Token;

        HttpResponseMessage wrong = await setup.Executor.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion, password = WrongPassword });
        HttpResponseMessage correct = await setup.Executor.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion, password = ApiTestHelpers.ExecutorPassword });

        await ApiTestHelpers.AssertProblemAsync(wrong, HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Equal(ExecutorStatus.Accepted, await LoadStatusAsync(setup.InvitationId));
    }

    [SqlFact]
    public async Task Decline_WithoutPassword_Works()
    {
        Setup setup = await PrepareAsync();

        HttpResponseMessage response = await RespondByIdAsync(setup, false, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExecutorStatus.Declined, await LoadStatusAsync(setup.InvitationId));
    }
}

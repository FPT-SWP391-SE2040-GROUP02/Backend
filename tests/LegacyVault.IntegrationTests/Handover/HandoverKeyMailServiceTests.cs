using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.Ekyc;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Người thực hiện gửi khóa mở gói cho người nhận qua email. Dùng fake, không cần SQL.</summary>
public sealed class HandoverKeyMailServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeProgressExecutorReader executors = new FakeProgressExecutorReader();
    private readonly FakeHandoverProgressReader reader = new FakeHandoverProgressReader();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeShards shards = new FakeShards();
    private readonly FakeKeySender sender = new FakeKeySender();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverKeyMailService service;
    private readonly Guid executorUserId = Guid.NewGuid();
    private readonly DeathCase deathCase;
    private readonly Container container;
    private readonly Designation designation;
    private readonly string key = ExecutorKeyRules.GenerateShard();

    public HandoverKeyMailServiceTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = executorUserId;

        Executor executor = new Executor();
        executor.VaultId = data.Vault.Id;
        executor.UserId = executorUserId;
        executor.Status = ExecutorStatus.Accepted;
        executors.Executors.Add(executor);

        container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        protector.Data[designation.BeneficiaryId] = data.People[designation.BeneficiaryId];

        deathCase = new DeathCase();
        deathCase.VaultId = data.Vault.Id;
        deathCase.PlanId = data.Plan.Id;
        deathCase.Status = DeathCaseStatus.ApprovedForHandover;
        reader.Cases.Add(deathCase);

        ExecutorKeyShard shard = new ExecutorKeyShard();
        shard.VaultId = data.Vault.Id;
        shard.Version = 1;
        shard.ShardHash = ExecutorKeyRules.Hash(data.Vault.Id, 1, key);
        shard.IssuedAt = Now;
        shards.Shard = shard;

        service = new HandoverKeyMailService(
            currentUser, vaults, executors, reader, repository, beneficiaries, protector, shards, sender, time);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, "old-token", null);
        repository.Turns.Add(turn);
        return turn;
    }

    private async Task<string?> ReasonOfRejectionAsync(Guid turnId)
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.SendAsync(deathCase.Id, turnId, key, CancellationToken.None));
        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        return error.Extensions["reason"] as string;
    }

    [Fact]
    public async Task RightKey_IsMailedToTheRecipient()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited);

        SendHandoverKeyResponse result = await service.SendAsync(
            deathCase.Id, turn.Id, key.ToLowerInvariant(), CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("nguoi-nhan@example.com", sender.Sent[0].ToEmail);
        Assert.Equal(key.ToLowerInvariant(), sender.Sent[0].Key);
        Assert.Equal(turn.Id, result.TurnId);
        Assert.Equal(Now, result.SentAt);
    }

    [Fact]
    public async Task WrongKey_IsForbiddenAndNothingIsSent()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.SendAsync(deathCase.Id, turn.Id, ExecutorKeyRules.GenerateShard(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverKeyInvalid, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task VaultWithoutKey_IsRejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited);
        shards.Shard = null;

        Assert.Equal("NO_KEY", await ReasonOfRejectionAsync(turn.Id));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task RecipientWithoutEmail_IsRejected()
    {
        protector.Data[designation.BeneficiaryId] = new BeneficiaryPersonalData
        {
            CitizenId = "012345671234", FullName = "Người Nhận", DateOfBirth = new DateOnly(1990, 1, 1), Email = null
        };
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited);

        Assert.Equal("NO_EMAIL", await ReasonOfRejectionAsync(turn.Id));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task FinishedTurn_IsRejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.HandoverCommitted);

        Assert.Equal("WRONG_STATE", await ReasonOfRejectionAsync(turn.Id));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task TurnWhereTheRecipientAlreadyEnteredTheKey_IsRejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept);
        turn.KeyVerifiedAt = Now;

        Assert.Equal("WRONG_STATE", await ReasonOfRejectionAsync(turn.Id));
    }

    [Fact]
    public async Task SendFailure_ReturnsServiceUnavailable()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited);
        sender.FailNext = true;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.SendAsync(deathCase.Id, turn.Id, key, CancellationToken.None));

        Assert.Equal(AppErrorType.ServiceUnavailable, error.Type);
        Assert.Equal("KEY_SEND_FAILED", error.Extensions["reason"]);
    }

    [Fact]
    public async Task OwnerOrStranger_GetsNotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited);
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.SendAsync(deathCase.Id, turn.Id, key, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
        Assert.Empty(sender.Sent);
    }

    private sealed class FakeShards : IExecutorKeyShardRepository
    {
        public ExecutorKeyShard? Shard { get; set; }

        public Task<ExecutorKeyShard?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
        {
            return Task.FromResult(Shard);
        }

        public void Add(ExecutorKeyShard shard)
        {
            Shard = shard;
        }

        public bool IsUniqueViolation(Exception exception)
        {
            return false;
        }

        public void DiscardPendingChanges()
        {
        }
    }

    private sealed class FakeKeySender : IHandoverKeySender
    {
        public List<HandoverKeyMail> Sent { get; } = new List<HandoverKeyMail>();

        public bool FailNext { get; set; }

        public Task SendAsync(HandoverKeyMail mail, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("smtp down");
            }
            Sent.Add(mail);
            return Task.CompletedTask;
        }
    }
}

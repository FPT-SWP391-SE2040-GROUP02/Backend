using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Khóa mở gói: kho đã cấp khóa thì người nhận phải nhập đúng khóa do Người thực hiện đưa trước khi đồng ý nhận.</summary>
public sealed class HandoverKeyTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly FakeKeyShards keyShards = new FakeKeyShards();
    private readonly HandoverService service;
    private readonly Guid claimantId = Guid.NewGuid();
    private readonly HandoverTurn turn;
    private readonly string shardText = ExecutorKeyRules.GenerateShard();

    public HandoverKeyTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = claimantId;
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        Designation designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        service = new HandoverService(
            currentUser, users, vaults, repository, new FakeTokenService(), new FakePolicy(), new NoHoldReaderForTests(),
            unitOfWork, time, keyShards);
        turn = data.BuildTurn(container, designation, HandoverTurnStatus.ReadyToAccept, "t1", claimantId);
        turn.ReadyAt = Now;
        turn.DecisionDeadline = Now.AddDays(7);
        repository.Turns.Add(turn);
    }

    private void IssueKey()
    {
        ExecutorKeyShard shard = new ExecutorKeyShard();
        shard.VaultId = turn.VaultId;
        shard.Version = 1;
        shard.ShardHash = ExecutorKeyRules.Hash(turn.VaultId, 1, shardText);
        shard.IssuedAt = Now;
        keyShards.Shard = shard;
    }

    [Fact]
    public async Task VaultWithoutAKey_DoesNotRequireOneAndAcceptWorks()
    {
        HandoverTurnResponse view = await service.GetTurnAsync(turn.Id, CancellationToken.None);
        HandoverTurnResponse accepted = await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.False(view.KeyRequired);
        Assert.True(view.KeyVerified);
        Assert.Equal("HANDOVER_COMMITTED", accepted.Status);
    }

    [Fact]
    public async Task VaultWithAKey_AcceptDoesNotNeedTheKey_OnlyDownloadDoes()
    {
        IssueKey();

        HandoverTurnResponse view = await service.GetTurnAsync(turn.Id, CancellationToken.None);
        await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.True(view.KeyRequired);
        Assert.False(view.KeyVerified);
        Assert.Single(repository.Grants);
    }

    [Fact]
    public async Task VerifyKey_AfterAccepting_IsAllowed()
    {
        IssueKey();
        await service.AcceptAsync(turn.Id, CancellationToken.None);

        HandoverTurnResponse verified = await service.VerifyKeyAsync(turn.Id, shardText, CancellationToken.None);

        Assert.True(verified.KeyVerified);
    }

    [Fact]
    public async Task CorrectKey_UnlocksAcceptEvenWhenTypedWithLowercaseAndSpaces()
    {
        IssueKey();
        string typed = shardText.ToLowerInvariant().Replace("-", " ");

        HandoverTurnResponse verified = await service.VerifyKeyAsync(turn.Id, typed, CancellationToken.None);
        HandoverTurnResponse accepted = await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.True(verified.KeyVerified);
        Assert.NotNull(turn.KeyVerifiedAt);
        Assert.Equal("HANDOVER_COMMITTED", accepted.Status);
    }

    [Fact]
    public async Task WrongKey_IsForbiddenAndCountsTheAttempt()
    {
        IssueKey();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyKeyAsync(turn.Id, ExecutorKeyRules.GenerateShard(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverKeyInvalid, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
        Assert.Equal(HandoverService.MaxKeyFailures - 1, error.Extensions["attemptsRemaining"]);
        Assert.Equal(1, turn.KeyFailedCount);
        Assert.Null(turn.KeyVerifiedAt);
    }

    [Fact]
    public async Task TooManyWrongKeys_LockTheTurnEvenForTheRightKey()
    {
        IssueKey();
        for (int attempt = 0; attempt < HandoverService.MaxKeyFailures; attempt++)
        {
            await Assert.ThrowsAsync<AppException>(
                () => service.VerifyKeyAsync(turn.Id, ExecutorKeyRules.GenerateShard(), CancellationToken.None));
        }

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyKeyAsync(turn.Id, shardText, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverKeyLocked, error.Code);
        Assert.Equal(AppErrorType.Locked, error.Type);
        Assert.Null(turn.KeyVerifiedAt);
    }

    [Fact]
    public async Task OtherUsersCannotVerifyTheKeyOfSomeoneElsesTurn()
    {
        IssueKey();
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyKeyAsync(turn.Id, shardText, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    internal sealed class FakeKeyShards : IExecutorKeyShardRepository
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

    internal sealed class NoHoldReaderForTests : IHoldReader
    {
        public Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct)
        {
            return Task.FromResult(false);
        }
    }
}

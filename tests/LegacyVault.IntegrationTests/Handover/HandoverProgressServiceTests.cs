using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
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

/// <summary>Executor theo dõi tiến độ bàn giao: kiểm quyền, gom trạng thái và đánh dấu tài sản đã tải (không cần SQL).</summary>
public sealed class HandoverProgressServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeProgressExecutorReader executors = new FakeProgressExecutorReader();
    private readonly FakeHandoverProgressReader reader = new FakeHandoverProgressReader();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly HandoverProgressService service;
    private readonly Guid executorUserId = Guid.NewGuid();
    private readonly DeathCase deathCase;

    public HandoverProgressServiceTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = executorUserId;

        Executor executor = new Executor();
        executor.VaultId = data.Vault.Id;
        executor.UserId = executorUserId;
        executor.Status = ExecutorStatus.Accepted;
        executors.Executors.Add(executor);

        deathCase = new DeathCase();
        deathCase.VaultId = data.Vault.Id;
        deathCase.PlanId = data.Plan.Id;
        deathCase.Status = DeathCaseStatus.ApprovedForHandover;
        reader.Cases.Add(deathCase);

        service = new HandoverProgressService(
            currentUser, vaults, executors, reader, beneficiaries, protector, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Progress_CountsTurnsByGroupAndMarksDownloadedItems()
    {
        // Chuẩn bị: một gói hai tài sản, hai người nhận; một người đã đồng ý và tải một tài sản
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        Designation first = data.Designate(container, "Người Một", "mot@example.com");
        Designation second = data.Designate(container, "Người Hai", "hai@example.com");
        HandoverTurn accepted = data.BuildTurn(container, first, HandoverTurnStatus.HandoverCommitted, "t1", Guid.NewGuid());
        HandoverTurn waiting = data.BuildTurn(container, second, HandoverTurnStatus.Invited, "t2", null);
        HandoverGrant grant = new HandoverGrant();
        grant.TurnId = accepted.Id;
        grant.Status = HandoverGrantStatus.Active;
        grant.GrantedAt = Now;
        grant.ExpiresAt = Now.AddDays(30);
        accepted.Grant = grant;
        reader.Turns.Add(accepted);
        reader.Turns.Add(waiting);
        reader.FirstDownloads[accepted.Items[0].Id] = Now.AddHours(1);
        reader.CitizenIdLast4[first.BeneficiaryId] = "1111";

        // Hành động
        HandoverProgressResponse result = await service.GetProgressAsync(deathCase.Id, CancellationToken.None);

        // Kiểm tra
        Assert.True(result.HandoverStarted);
        Assert.Equal(Now, result.ServerTime);
        Assert.Equal(2, result.Summary.TotalTurns);
        Assert.Equal(1, result.Summary.Accepted);
        Assert.Equal(1, result.Summary.WaitingResponse);

        HandoverProgressTurnResponse acceptedTurn = result.Turns[0];
        Assert.Equal("HANDOVER_COMMITTED", acceptedTurn.Status);
        Assert.Equal("1111", acceptedTurn.BeneficiaryCitizenIdLast4);
        Assert.Equal("ACTIVE", acceptedTurn.GrantStatus);
        Assert.Equal(2, acceptedTurn.TotalItems);
        Assert.Equal(1, acceptedTurn.DownloadedItems);
        Assert.True(acceptedTurn.Items[0].Downloaded);
        Assert.Equal(Now.AddHours(1), acceptedTurn.Items[0].FirstDownloadedAt);
        Assert.False(acceptedTurn.Items[1].Downloaded);
        Assert.Equal("FILE", acceptedTurn.Items[0].Kind);

        HandoverProgressTurnResponse waitingTurn = result.Turns[1];
        Assert.Equal("INVITED", waitingTurn.Status);
        Assert.Null(waitingTurn.GrantStatus);
        Assert.Equal(0, waitingTurn.DownloadedItems);
    }

    /// <summary>Đưa hồ sơ người nhận của chỉ định vào kho giả kèm họ tên và số điện thoại rõ.</summary>
    private void RegisterBeneficiary(Designation designation, string phone)
    {
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        BeneficiaryPersonalData personal = data.People[designation.BeneficiaryId];
        personal.Phone = phone;
        protector.Data[designation.BeneficiaryId] = personal;
    }

    [Fact]
    public async Task Progress_ShowsNameAndPhoneOnlyForHandedOverTurn()
    {
        // Chuẩn bị: một lượt đã bàn giao (có quyền tải) và một lượt đang xác minh danh tính
        Container container = data.AddContainer("Gói D", ContainerStatus.Ready, 1);
        Designation done = data.Designate(container, "Nguyễn Văn Một", "mot@example.com");
        Designation pending = data.Designate(container, "Trần Thị Hai", "hai@example.com");
        RegisterBeneficiary(done, "0901234567");
        RegisterBeneficiary(pending, "0907654321");
        HandoverTurn handedOver = data.BuildTurn(container, done, HandoverTurnStatus.HandoverCommitted, "d1", Guid.NewGuid());
        HandoverGrant grant = new HandoverGrant();
        grant.TurnId = handedOver.Id;
        grant.Status = HandoverGrantStatus.Active;
        grant.GrantedAt = Now;
        grant.ExpiresAt = Now.AddDays(30);
        handedOver.Grant = grant;
        HandoverTurn verifying = data.BuildTurn(container, pending, HandoverTurnStatus.IdentityInProgress, "d2", Guid.NewGuid());
        reader.Turns.Add(handedOver);
        reader.Turns.Add(verifying);

        HandoverProgressResponse result = await service.GetProgressAsync(deathCase.Id, CancellationToken.None);

        Assert.Equal("Nguyễn Văn Một", result.Turns[0].BeneficiaryFullName);
        Assert.Equal("0901234567", result.Turns[0].BeneficiaryPhone);
        Assert.Null(result.Turns[1].BeneficiaryFullName);
        Assert.Null(result.Turns[1].BeneficiaryPhone);
    }

    [Fact]
    public async Task Progress_CommittedTurnWithoutGrantHidesNameAndPhone()
    {
        Container container = data.AddContainer("Gói E", ContainerStatus.Ready, 1);
        Designation designation = data.Designate(container, "Lê Văn Ba", null);
        RegisterBeneficiary(designation, "0911111111");
        reader.Turns.Add(data.BuildTurn(container, designation, HandoverTurnStatus.HandoverCommitted, "e1", Guid.NewGuid()));

        HandoverProgressResponse result = await service.GetProgressAsync(deathCase.Id, CancellationToken.None);

        Assert.Null(result.Turns[0].BeneficiaryFullName);
        Assert.Null(result.Turns[0].BeneficiaryPhone);
    }

    [Fact]
    public async Task Progress_EndedStatusesAreGroupedTogether()
    {
        Container container = data.AddContainer("Gói B", ContainerStatus.Ready, 1);
        HandoverTurnStatus[] statuses = new HandoverTurnStatus[]
        {
            HandoverTurnStatus.InitialResponseExpired,
            HandoverTurnStatus.IdentityInProgress,
            HandoverTurnStatus.ReadyToAccept,
            HandoverTurnStatus.Declined,
            HandoverTurnStatus.DecisionExpired,
            HandoverTurnStatus.Cancelled,
            HandoverTurnStatus.Lapsed
        };
        int index = 0;
        foreach (HandoverTurnStatus status in statuses)
        {
            Designation designation = data.Designate(container, "Người " + index, null);
            reader.Turns.Add(data.BuildTurn(container, designation, status, "s" + index, null));
            index++;
        }

        HandoverProgressResponse result = await service.GetProgressAsync(deathCase.Id, CancellationToken.None);

        Assert.Equal(7, result.Summary.TotalTurns);
        Assert.Equal(1, result.Summary.WaitingResponse);
        Assert.Equal(2, result.Summary.InVerification);
        Assert.Equal(1, result.Summary.Declined);
        Assert.Equal(3, result.Summary.Ended);
        Assert.Equal(0, result.Summary.Accepted);
    }

    [Fact]
    public async Task Progress_DownloadWithoutGrantIsIgnored()
    {
        // Lượt chưa có quyền tải thì không thể có lần tải; dữ liệu lạ không được tính
        Container container = data.AddContainer("Gói C", ContainerStatus.Ready, 1);
        Designation designation = data.Designate(container, "Người Ba", null);
        HandoverTurn turn = data.BuildTurn(container, designation, HandoverTurnStatus.ReadyToAccept, "t3", Guid.NewGuid());
        reader.Turns.Add(turn);
        reader.FirstDownloads[turn.Items[0].Id] = Now;

        HandoverProgressResponse result = await service.GetProgressAsync(deathCase.Id, CancellationToken.None);

        Assert.False(result.Turns[0].Items[0].Downloaded);
        Assert.Equal(0, result.Turns[0].DownloadedItems);
    }

    [Theory]
    [InlineData(DeathCaseStatus.Draft)]
    [InlineData(DeathCaseStatus.UnderReview)]
    [InlineData(DeathCaseStatus.SupplementRequired)]
    [InlineData(DeathCaseStatus.Rejected)]
    public async Task Progress_CaseNotApproved_ReturnsNotStartedWithoutTurns(DeathCaseStatus status)
    {
        deathCase.Status = status;
        Container container = data.AddContainer("Gói D", ContainerStatus.Ready, 1);
        Designation designation = data.Designate(container, "Người Bốn", null);
        reader.Turns.Add(data.BuildTurn(container, designation, HandoverTurnStatus.Invited, "t4", null));

        HandoverProgressResponse result = await service.GetProgressAsync(deathCase.Id, CancellationToken.None);

        Assert.False(result.HandoverStarted);
        Assert.Empty(result.Turns);
        Assert.Equal(0, result.Summary.TotalTurns);
        Assert.Equal(0, reader.TurnQueryCount);
    }

    [Fact]
    public async Task Progress_OwnerIsRejectedWithNotFound()
    {
        currentUser.UserId = data.Owner.Id;
        executors.Executors[0].UserId = data.Owner.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetProgressAsync(deathCase.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
        Assert.Equal(AppErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task Progress_StrangerIsRejectedWithNotFound()
    {
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetProgressAsync(deathCase.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
    }

    [Fact]
    public async Task Progress_UnknownCaseIsRejectedWithNotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetProgressAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
    }

    [Fact]
    public void ResponseTypes_HaveNoContactOrFullCitizenIdFields()
    {
        // Executor không được nhận liên hệ hay CCCD đầy đủ của người nhận, cũng không nhận nội dung tài sản
        List<Type> types = new List<Type>();
        types.Add(typeof(HandoverProgressResponse));
        types.Add(typeof(HandoverProgressSummaryResponse));
        types.Add(typeof(HandoverProgressTurnResponse));
        types.Add(typeof(HandoverProgressItemResponse));
        string[] forbidden = new string[] { "Email", "Phone", "CitizenId", "FullName", "Content", "StorageKey" };

        foreach (Type type in types)
        {
            foreach (System.Reflection.PropertyInfo property in type.GetProperties())
            {
                foreach (string word in forbidden)
                {
                    Assert.False(string.Equals(property.Name, word, StringComparison.Ordinal),
                        type.Name + "." + property.Name + " không được có trong phản hồi tiến độ.");
                }
            }
        }
    }
}

/// <summary>Executor đang hiệu lực giả, lưu trong List.</summary>
public sealed class FakeProgressExecutorReader : IExecutorReader
{
    public List<Executor> Executors { get; } = new List<Executor>();

    public Task<List<Executor>> GetActiveAsync(Guid vaultId, CancellationToken ct)
    {
        List<Executor> result = new List<Executor>();
        foreach (Executor executor in Executors)
        {
            if (executor.VaultId == vaultId && executor.Status == ExecutorStatus.Accepted)
            {
                result.Add(executor);
            }
        }
        return Task.FromResult(result);
    }

    public Task<List<Executor>> GetAvailableAsync(Guid vaultId, CancellationToken ct)
    {
        throw new NotSupportedException();
    }
}

/// <summary>Nguồn dữ liệu tiến độ giả: hồ sơ, lượt, lần tải đầu và 4 số cuối CCCD nằm trong bộ nhớ.</summary>
public sealed class FakeHandoverProgressReader : IHandoverProgressReader
{
    public List<DeathCase> Cases { get; } = new List<DeathCase>();

    public List<HandoverTurn> Turns { get; } = new List<HandoverTurn>();

    public Dictionary<Guid, DateTimeOffset> FirstDownloads { get; } = new Dictionary<Guid, DateTimeOffset>();

    public Dictionary<Guid, string> CitizenIdLast4 { get; } = new Dictionary<Guid, string>();

    public int TurnQueryCount { get; private set; }

    public Task<DeathCase?> FindCaseAsync(Guid caseId, CancellationToken ct)
    {
        foreach (DeathCase deathCase in Cases)
        {
            if (deathCase.Id == caseId)
            {
                return Task.FromResult<DeathCase?>(deathCase);
            }
        }
        return Task.FromResult<DeathCase?>(null);
    }

    public Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid planId, CancellationToken ct)
    {
        TurnQueryCount++;
        List<HandoverTurn> result = new List<HandoverTurn>();
        foreach (HandoverTurn turn in Turns)
        {
            if (turn.PlanId == planId)
            {
                result.Add(turn);
            }
        }
        return Task.FromResult(result);
    }

    public Task<Dictionary<Guid, DateTimeOffset>> FindFirstDownloadTimesAsync(List<Guid> grantIds, CancellationToken ct)
    {
        return Task.FromResult(FirstDownloads);
    }

    public Task<Dictionary<Guid, string>> FindCitizenIdLast4Async(List<Guid> beneficiaryIds, CancellationToken ct)
    {
        return Task.FromResult(CitizenIdLast4);
    }
}

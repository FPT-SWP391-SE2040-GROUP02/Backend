using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
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
    private readonly HandoverRecord record;

    public HandoverProgressServiceTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = executorUserId;

        Executor executor = new Executor();
        executor.VaultId = data.Vault.Id;
        executor.UserId = executorUserId;
        executor.Status = ExecutorStatus.Accepted;
        executors.Executors.Add(executor);

        record = new HandoverRecord();
        record.VaultId = data.Vault.Id;
        record.PlanId = data.Plan.Id;
        record.Status = HandoverRecordStatus.IdentityCheck;
        record.FinalizedAt = Now;
        reader.Records.Add(record);

        service = new HandoverProgressService(
            currentUser, vaults, executors, reader, beneficiaries, protector, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Progress_CountsTurnsByGroupAndMarksDownloadedItems()
    {
        // Chuẩn bị: hai tài sản gán chung cho hai người nhận; một người đã đồng ý và tải một tài sản
        List<Asset> shared = data.AddAssets("Tài sản", 2);
        Beneficiary first = data.AddRecipient("Người Một", "mot@example.com");
        Beneficiary second = data.AddRecipient("Người Hai", "hai@example.com");
        data.AssignAll(shared, first);
        data.AssignAll(shared, second);
        HandoverTurn accepted = data.BuildTurn(first, HandoverTurnStatus.HandoverCommitted, "t1", Guid.NewGuid());
        HandoverTurn waiting = data.BuildTurn(second, HandoverTurnStatus.Invited, "t2", null);
        HandoverGrant grant = new HandoverGrant();
        grant.TurnId = accepted.Id;
        grant.Status = HandoverGrantStatus.Active;
        grant.GrantedAt = Now;
        grant.ExpiresAt = Now.AddDays(30);
        accepted.Grant = grant;
        reader.Turns.Add(accepted);
        reader.Turns.Add(waiting);
        reader.FirstDownloads[accepted.Items[0].Id] = Now.AddHours(1);
        reader.CitizenIds[first.Id] = "001099001111";

        // Hành động
        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        // Kiểm tra
        Assert.True(result.HandoverStarted);
        Assert.Equal(Now, result.ServerTime);
        Assert.Equal(2, result.Summary.TotalTurns);
        Assert.Equal(1, result.Summary.Accepted);
        Assert.Equal(1, result.Summary.WaitingResponse);

        HandoverProgressTurnResponse acceptedTurn = result.Turns[0];
        Assert.Equal("HANDOVER_COMMITTED", acceptedTurn.Status);
        Assert.Equal("001099001111", acceptedTurn.BeneficiaryCitizenId);
        Assert.Equal(first.Id, acceptedTurn.BeneficiaryId);
        Assert.Equal(second.Id, result.Turns[1].BeneficiaryId);
        Assert.Equal("ACTIVE", acceptedTurn.GrantStatus);
        Assert.Equal(2, acceptedTurn.TotalItems);
        Assert.Equal(1, acceptedTurn.DownloadedItems);
        Assert.True(acceptedTurn.Items[0].Downloaded);
        Assert.Equal(Now.AddHours(1), acceptedTurn.Items[0].FirstDownloadedAt);
        Assert.False(acceptedTurn.Items[1].Downloaded);
        Assert.Equal("FILE", acceptedTurn.Items[0].Kind);
        Assert.Equal(accepted.Items[0].AssetId, acceptedTurn.Items[0].AssetId);

        HandoverProgressTurnResponse waitingTurn = result.Turns[1];
        Assert.Equal("INVITED", waitingTurn.Status);
        Assert.Null(waitingTurn.GrantStatus);
        Assert.Equal(0, waitingTurn.DownloadedItems);
    }

    /// <summary>Đưa hồ sơ người nhận vào kho giả kèm họ tên và số điện thoại rõ.</summary>
    private void RegisterBeneficiary(Beneficiary recipient, string phone)
    {
        beneficiaries.Beneficiaries.Add(recipient);
        BeneficiaryPersonalData personal = data.People[recipient.Id];
        personal.Phone = phone;
        protector.Data[recipient.Id] = personal;
    }

    [Theory]
    [InlineData(HandoverTurnStatus.Invited)]
    [InlineData(HandoverTurnStatus.InitialResponseExpired)]
    [InlineData(HandoverTurnStatus.IdentityInProgress)]
    [InlineData(HandoverTurnStatus.ReadyToAccept)]
    [InlineData(HandoverTurnStatus.HandoverCommitted)]
    public async Task Progress_ShowsNameAndPhoneForActiveOrHandedOverTurn(HandoverTurnStatus status)
    {
        // Executor cần liên hệ người nhận ngay từ lúc mời, không phải chờ tới khi đã bàn giao
        Beneficiary recipient = data.AddRecipient("Nguyễn Văn Một", "mot@example.com");
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        RegisterBeneficiary(recipient, "0901234567");
        reader.Turns.Add(data.BuildTurn(recipient, status, "d1", null));

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        Assert.Equal("Nguyễn Văn Một", result.Turns[0].BeneficiaryFullName);
        Assert.Equal("0901234567", result.Turns[0].BeneficiaryPhone);
        Assert.Equal("mot@example.com", result.Turns[0].BeneficiaryEmail);
    }

    [Theory]
    [InlineData(HandoverTurnStatus.Declined)]
    [InlineData(HandoverTurnStatus.DecisionExpired)]
    [InlineData(HandoverTurnStatus.Cancelled)]
    [InlineData(HandoverTurnStatus.Lapsed)]
    public async Task Progress_HidesNameAndPhoneForEndedTurn(HandoverTurnStatus status)
    {
        Beneficiary recipient = data.AddRecipient("Lê Văn Ba", null);
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        RegisterBeneficiary(recipient, "0911111111");
        reader.Turns.Add(data.BuildTurn(recipient, status, "e1", null));

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        Assert.Null(result.Turns[0].BeneficiaryFullName);
        Assert.Null(result.Turns[0].BeneficiaryPhone);
        Assert.Null(result.Turns[0].BeneficiaryEmail);
    }

    [Fact]
    public async Task Progress_UndecryptableBeneficiaryLeavesContactEmptyInsteadOfFailing()
    {
        // Không giải mã được (thiếu KEK, dữ liệu hỏng) thì để trống, không làm hỏng cả màn tiến độ
        Beneficiary recipient = data.AddRecipient("Người Bốn", null);
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        beneficiaries.Beneficiaries.Add(recipient);
        protector.Failing = true;
        reader.Turns.Add(data.BuildTurn(recipient, HandoverTurnStatus.Invited, "f1", null));

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        Assert.Null(result.Turns[0].BeneficiaryFullName);
        Assert.Null(result.Turns[0].BeneficiaryPhone);
        Assert.Null(result.Turns[0].BeneficiaryEmail);
        Assert.Equal("INVITED", result.Turns[0].Status);
    }

    [Fact]
    public async Task Progress_ReturnsTimesForEachStepOfTheTurn()
    {
        // FE vẽ 5 bước có giờ: đã mời, đồng ý, biên bản được duyệt, nhập mã mở gói, nhận xong
        Beneficiary recipient = data.AddRecipient("Người Năm", "nam@example.com");
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        HandoverTurn turn = data.BuildTurn(recipient, HandoverTurnStatus.HandoverCommitted, "g1", Guid.NewGuid());
        turn.InvitationSentAt = Now.AddDays(-5);
        turn.DecidedAt = Now.AddDays(-4);
        turn.KeyVerifiedAt = Now.AddDays(-2);
        turn.ReceivedAllAt = Now.AddDays(-1);
        reader.Turns.Add(turn);
        turn.ReceiptExecutorSignedAt = Now;
        // Luật sư đã ký biên bản.
        record.CompletedAt = Now.AddDays(-3);

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        HandoverProgressTurnResponse turnResponse = result.Turns[0];
        Assert.Equal(Now.AddDays(-5), turnResponse.InvitationSentAt);
        Assert.Equal(Now.AddDays(-4), turnResponse.DecidedAt);
        Assert.Equal(Now, turnResponse.ReceiptExecutorSignedAt);
        Assert.Equal(Now.AddDays(-3), turnResponse.DocumentApprovedAt);
        Assert.Equal(Now.AddDays(-2), turnResponse.KeyVerifiedAt);
        Assert.Equal(Now.AddDays(-1), turnResponse.ReceivedAllAt);
    }

    [Fact]
    public async Task Progress_TurnWithoutDocumentOrKeyHasNullStepTimes()
    {
        Beneficiary recipient = data.AddRecipient("Người Sáu", null);
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        reader.Turns.Add(data.BuildTurn(recipient, HandoverTurnStatus.Invited, "h1", null));

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        Assert.Null(result.Turns[0].InvitationSentAt);
        Assert.Null(result.Turns[0].ReceiptExecutorSignedAt);
        Assert.Null(result.Turns[0].DocumentApprovedAt);
        Assert.Null(result.Turns[0].KeyVerifiedAt);
        Assert.Null(result.Turns[0].ReceivedAllAt);
    }

    [Fact]
    public async Task Progress_EndedStatusesAreGroupedTogether()
    {
        List<Asset> assets = data.AddAssets("Tài sản", 1);
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
            Beneficiary recipient = data.AddRecipient("Người " + index, null);
            data.AssignAll(assets, recipient);
            reader.Turns.Add(data.BuildTurn(recipient, status, "s" + index, null));
            index++;
        }

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

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
        Beneficiary recipient = data.AddRecipient("Người Ba", null);
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        HandoverTurn turn = data.BuildTurn(recipient, HandoverTurnStatus.ReadyToAccept, "t3", Guid.NewGuid());
        reader.Turns.Add(turn);
        reader.FirstDownloads[turn.Items[0].Id] = Now;

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

        Assert.False(result.Turns[0].Items[0].Downloaded);
        Assert.Equal(0, result.Turns[0].DownloadedItems);
    }

    [Fact]
    public async Task Progress_RecordNeverFinalized_ReturnsNotStartedWithoutTurns()
    {
        record.Status = HandoverRecordStatus.Drafting;
        record.FinalizedAt = null;
        Beneficiary recipient = data.AddRecipient("Người Bốn", null);
        data.AssignAll(data.AddAssets("Tài sản", 1), recipient);
        reader.Turns.Add(data.BuildTurn(recipient, HandoverTurnStatus.Invited, "t4", null));

        HandoverProgressResponse result = await service.GetProgressAsync(record.Id, CancellationToken.None);

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

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetProgressAsync(record.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
        Assert.Equal(AppErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task Progress_StrangerIsRejectedWithNotFound()
    {
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetProgressAsync(record.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task Progress_UnknownCaseIsRejectedWithNotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetProgressAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
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

/// <summary>Nguồn dữ liệu tiến độ giả: biên bản, lượt, lần tải đầu và 4 số cuối CCCD nằm trong bộ nhớ.</summary>
public sealed class FakeHandoverProgressReader : IHandoverProgressReader
{
    public List<HandoverRecord> Records { get; } = new List<HandoverRecord>();

    public List<HandoverTurn> Turns { get; } = new List<HandoverTurn>();

    public Dictionary<Guid, DateTimeOffset> FirstDownloads { get; } = new Dictionary<Guid, DateTimeOffset>();

    public Dictionary<Guid, string> CitizenIds { get; } = new Dictionary<Guid, string>();

    public int TurnQueryCount { get; private set; }

    public Task<HandoverRecord?> FindRecordAsync(Guid recordId, CancellationToken ct)
    {
        foreach (HandoverRecord record in Records)
        {
            if (record.Id == recordId)
            {
                return Task.FromResult<HandoverRecord?>(record);
            }
        }
        return Task.FromResult<HandoverRecord?>(null);
    }

    public Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid planId, CancellationToken ct)
    {
        TurnQueryCount++;
        List<HandoverTurn> result = new List<HandoverTurn>();
        foreach (HandoverTurn turn in Turns)
        {
            if (turn.PlanId == planId)
            {
                // Lượt dựng bằng HandoverTestData chưa gắn biên bản: coi như thuộc biên bản đầu tiên của kho.
                if (turn.RecordId == null && Records.Count > 0)
                {
                    turn.RecordId = Records[0].Id;
                }
                result.Add(turn);
            }
        }
        return Task.FromResult(result);
    }

    public Task<Dictionary<Guid, DateTimeOffset>> FindFirstDownloadTimesAsync(List<Guid> grantIds, CancellationToken ct)
    {
        return Task.FromResult(FirstDownloads);
    }

    public Task<Dictionary<Guid, string>> FindCitizenIdsAsync(List<Guid> beneficiaryIds, CancellationToken ct)
    {
        return Task.FromResult(CitizenIds);
    }
}

using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.Ekyc;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Kiểm thư mời bằng mã hồ sơ và 4 số cuối căn cước (công khai, có giới hạn số lần thử).</summary>
public sealed class InvitationMailCheckServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeDeathCaseRepository deathCases = new FakeDeathCaseRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly InvitationMailCheckService service;
    private readonly string token;
    private readonly string caseCode;

    // Mỗi test dùng lượt mời riêng (token và id khác nhau) vì bộ đếm số lần thử giữ theo lượt trong bộ nhớ.
    public InvitationMailCheckServiceTests()
    {
        data.Owner.FullName = "Nguyễn Văn Chủ";
        users.Users.Add(data.Owner);
        vaults.Vaults.Add(data.Vault);
        Container container = data.AddContainer("Gói gia đình", ContainerStatus.Ready, 1);
        Designation designation = data.Designate(container, "Trần Minh Anh", "minh-anh@example.com");
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        protector.Data[designation.BeneficiaryId] = data.People[designation.BeneficiaryId];

        token = "token-" + Guid.NewGuid().ToString("N");
        repository.Turns.Add(data.BuildTurn(container, designation, HandoverTurnStatus.Invited, token, null));

        DeathCase deathCase = new DeathCase { VaultId = data.Vault.Id, Status = DeathCaseStatus.ApprovedForHandover };
        deathCases.Cases.Add(deathCase);
        caseCode = DeathCaseRules.CaseCode(deathCase.Id);

        service = new InvitationMailCheckService(
            repository, new FakeTokenService(), deathCases, beneficiaries, protector, vaults, users);
    }

    [Fact]
    public async Task Check_RightCodeAndLast4_VerifiesAndReturnsPackageAndOwnerName()
    {
        InvitationMailCheckResponse response = await service.CheckAsync(token, caseCode, "1234", CancellationToken.None);

        Assert.True(response.Verified);
        Assert.Equal("Gói gia đình", response.ContainerName);
        Assert.Equal("Nguyễn Văn Chủ", response.OwnerFullName);
    }

    [Fact]
    public async Task Check_CaseCodeIsNotCaseSensitive()
    {
        InvitationMailCheckResponse response = await service.CheckAsync(
            token, "  " + caseCode.ToLowerInvariant() + " ", "1234", CancellationToken.None);

        Assert.True(response.Verified);
    }

    [Fact]
    public async Task Check_WrongLast4_FailsAndLeaksNothingButAttemptsLeft()
    {
        InvitationMailCheckResponse response = await service.CheckAsync(token, caseCode, "9999", CancellationToken.None);

        Assert.False(response.Verified);
        Assert.Equal(InvitationMailCheckService.MaxAttempts - 1, response.AttemptsLeft);
        Assert.Null(response.ContainerName);
        Assert.Null(response.OwnerFullName);
    }

    [Fact]
    public async Task Check_WrongCaseCode_Fails()
    {
        InvitationMailCheckResponse response = await service.CheckAsync(token, "ABCD1234", "1234", CancellationToken.None);

        Assert.False(response.Verified);
    }

    [Fact]
    public async Task Check_UnknownToken_ThrowsInvitationInvalid()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CheckAsync("khong-co", caseCode, "1234", CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverInvitationInvalid, error.Code);
        Assert.Equal(AppErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task Check_TooManyWrongTries_LocksEvenTheRightAnswerAfterwards()
    {
        for (int i = 0; i < InvitationMailCheckService.MaxAttempts; i++)
        {
            await service.CheckAsync(token, caseCode, "0000", CancellationToken.None);
        }

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CheckAsync(token, caseCode, "1234", CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverMailCheckLocked, error.Code);
        Assert.Equal(AppErrorType.Locked, error.Type);
    }

    [Fact]
    public async Task Check_RightAnswerResetsTheFailureCount()
    {
        await service.CheckAsync(token, caseCode, "0000", CancellationToken.None);
        await service.CheckAsync(token, caseCode, "1234", CancellationToken.None);

        InvitationMailCheckResponse response = await service.CheckAsync(token, caseCode, "0000", CancellationToken.None);

        Assert.Equal(InvitationMailCheckService.MaxAttempts - 1, response.AttemptsLeft);
    }
}

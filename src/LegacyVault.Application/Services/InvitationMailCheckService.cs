using System.Collections.Concurrent;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

public interface IInvitationMailCheckService
{
    /// <summary>
    /// Công khai (không cần đăng nhập): người nhận nhập mã hồ sơ và 4 số cuối căn cước ghi trong thư để biết thư có thật không.
    /// Đúng thì trả tên gói và tên Chủ kho; sai thì trả số lần thử còn lại. Hết lượt thì 423 HANDOVER_MAIL_CHECK_LOCKED.
    /// </summary>
    Task<InvitationMailCheckResponse> CheckAsync(string token, string caseCode, string citizenIdLast4, CancellationToken ct);
}

/// <remarks>
/// Số lần thử đếm theo lượt mời, giữ trong bộ nhớ (khởi động lại thì đếm lại): chỉ nhằm chặn dò mã hồ sơ và số căn cước,
/// không phải dữ liệu nghiệp vụ. Token sai cho cùng lỗi 404 như API kiểm link, không lộ token nào có thật.
/// </remarks>
public sealed class InvitationMailCheckService : IInvitationMailCheckService
{
    /// <summary>Số lần thử tối đa cho một lượt mời.</summary>
    public const int MaxAttempts = 5;

    private static readonly ConcurrentDictionary<Guid, int> Failures = new ConcurrentDictionary<Guid, int>();

    private readonly IHandoverRepository handover;
    private readonly ITokenService tokens;
    private readonly IDeathCaseRepository deathCases;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IVaultRepository vaults;
    private readonly IUserRepository users;

    public InvitationMailCheckService(
        IHandoverRepository handover,
        ITokenService tokens,
        IDeathCaseRepository deathCases,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IVaultRepository vaults,
        IUserRepository users)
    {
        this.handover = handover;
        this.tokens = tokens;
        this.deathCases = deathCases;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.vaults = vaults;
        this.users = users;
    }

    public async Task<InvitationMailCheckResponse> CheckAsync(
        string token, string caseCode, string citizenIdLast4, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByTokenHashAsync(tokens.HashOpaqueToken(token), ct);
        if (turn == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverInvitationInvalid,
                "Liên kết mời không hợp lệ.");
        }

        int used = Failures.GetOrAdd(turn.Id, 0);
        if (used >= MaxAttempts)
        {
            throw new AppException(AppErrorType.Locked, ErrorCode.HandoverMailCheckLocked,
                "Bạn đã thử quá số lần cho phép. Hãy gọi số điện thoại công ty ghi trong thư để xác minh.");
        }

        string wantedCode = string.Empty;
        DeathCase? deathCase = await deathCases.FindOpenByVaultIdAsync(turn.VaultId, ct);
        if (deathCase != null)
        {
            wantedCode = DeathCaseRules.CaseCode(deathCase.Id);
        }
        string wantedLast4 = string.Empty;
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary != null)
        {
            string citizenId = protector.Unprotect(beneficiary).CitizenId;
            if (citizenId.Length >= 4)
            {
                wantedLast4 = citizenId.Substring(citizenId.Length - 4);
            }
        }

        bool matched = wantedCode.Length > 0 && wantedLast4.Length > 0
            && string.Equals(caseCode.Trim(), wantedCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(citizenIdLast4.Trim(), wantedLast4, StringComparison.Ordinal);

        InvitationMailCheckResponse response = new InvitationMailCheckResponse();
        if (!matched)
        {
            int total = Failures.AddOrUpdate(turn.Id, 1, (_, current) => current + 1);
            response.Verified = false;
            response.AttemptsLeft = Math.Max(0, MaxAttempts - total);
            return response;
        }

        Failures.TryRemove(turn.Id, out _);
        response.Verified = true;
        response.AttemptsLeft = MaxAttempts;
        response.ContainerName = turn.ContainerName;
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        User? owner = vault == null ? null : await users.FindByIdAsync(vault.OwnerId, ct);
        response.OwnerFullName = owner?.FullName;
        return response;
    }
}

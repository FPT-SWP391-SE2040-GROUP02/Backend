using System.Collections.Concurrent;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Kiểm thư mời có thật không; tối đa MaxAttempts lần sai mỗi lượt, đếm trong bộ nhớ.</summary>
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
    private readonly IHandoverRecordRepository records;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly IVaultRepository vaults;
    private readonly IUserRepository users;

    public InvitationMailCheckService(
        IHandoverRepository handover,
        ITokenService tokens,
        IHandoverRecordRepository records,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        IVaultRepository vaults,
        IUserRepository users)
    {
        this.handover = handover;
        this.tokens = tokens;
        this.records = records;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.vaults = vaults;
        this.users = users;
    }

    /// <summary>Nút "Kiểm tra" ở trang lời mời: đối chiếu mã hồ sơ và số căn cước, tối đa 5 lần sai mỗi lượt.</summary>
    public async Task<InvitationMailCheckResponse> CheckAsync(
        string token, string caseCode, string citizenId, CancellationToken ct)
    {
        // 1. Tìm lượt theo token trong link; token sai thì báo link không hợp lệ
        HandoverTurn? turn = await handover.FindTurnByTokenHashAsync(tokens.HashOpaqueToken(token), ct);
        if (turn == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverInvitationInvalid,
                "Liên kết mời không hợp lệ.");
        }

        // 2. Đã sai đủ số lần cho phép thì khóa
        int used = Failures.GetOrAdd(turn.Id, 0);
        if (used >= MaxAttempts)
        {
            throw new AppException(AppErrorType.Locked, ErrorCode.HandoverMailCheckLocked,
                "Bạn đã thử quá số lần cho phép. Hãy gọi số điện thoại công ty ghi trong thư để xác minh.");
        }

        // 3. Mã hồ sơ đúng lấy từ hồ sơ chứng tử, số căn cước đúng lấy từ thông tin Chủ kho đã khai
        string wantedCode = string.Empty;
        HandoverRecord? record = await records.FindByVaultIdAsync(turn.VaultId, ct);
        if (record != null)
        {
            wantedCode = HandoverRecordRules.CaseCode(record.Id);
        }
        string wantedCitizenId = string.Empty;
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(turn.VaultId, turn.BeneficiaryId, ct);
        if (beneficiary != null)
        {
            wantedCitizenId = protector.Unprotect(beneficiary).CitizenId;
        }

        // 4. So khớp: mã hồ sơ không phân biệt hoa thường, số căn cước so chính xác
        bool matched = wantedCode.Length > 0 && wantedCitizenId.Length > 0
            && string.Equals(caseCode.Trim(), wantedCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(citizenId.Trim(), wantedCitizenId, StringComparison.Ordinal);

        InvitationMailCheckResponse response = new InvitationMailCheckResponse();
        // 5. Sai: cộng một lần sai và trả số lần còn lại
        if (!matched)
        {
            int total = Failures.AddOrUpdate(turn.Id, 1, (_, current) => current + 1);
            response.Verified = false;
            response.AttemptsLeft = Math.Max(0, MaxAttempts - total);
            return response;
        }

        // 6. Đúng: xóa bộ đếm sai, trả tên gói và tên Chủ kho
        Failures.TryRemove(turn.Id, out _);
        response.Verified = true;
        response.AttemptsLeft = MaxAttempts;
        response.ContainerName = turn.ContainerName;
        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault != null)
        {
            User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
            if (owner != null)
            {
                response.OwnerFullName = owner.FullName;
            }
        }
        return response;
    }
}

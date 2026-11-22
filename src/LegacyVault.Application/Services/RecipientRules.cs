using System.Security.Cryptography;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật chung khi thêm hoặc sửa người nhận, dùng cho cả Chủ kho (hồ sơ người nhận) lẫn luật sư (thêm người nhận
/// vào biên bản): ngày sinh hợp lệ và đủ 18 tuổi, email không trùng với những người cùng tham gia kho.
/// </summary>
public static class RecipientRules
{
    public const int MinimumAge = 18;

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Hôm nay theo giờ Việt Nam.</summary>
    public static DateOnly TodayInVietnam(TimeProvider timeProvider)
    {
        DateTimeOffset nowInVietnam = timeProvider.GetUtcNow().ToOffset(VietnamOffset);
        return DateOnly.FromDateTime(nowInVietnam.DateTime);
    }

    /// <summary>Ngày sinh không ở tương lai và người nhận đủ 18 tuổi tính đến hôm nay (400 ở trường dateOfBirth).</summary>
    public static void EnsureAdultBirthDate(DateOnly dateOfBirth, DateOnly today)
    {
        if (dateOfBirth > today)
        {
            throw ValidationErrors.For("dateOfBirth", "Ngày sinh không được ở tương lai.");
        }
        // AddYears xử lý đúng ngày 29/2.
        DateOnly latestAllowed = today.AddYears(-MinimumAge);
        if (dateOfBirth > latestAllowed)
        {
            throw ValidationErrors.For("dateOfBirth", "Người nhận phải đủ 18 tuổi.");
        }
    }

    /// <summary>
    /// Email người nhận không được trùng với: Chủ kho, tài khoản nhân sự (Người xét duyệt, Người thực thi hệ thống,
    /// Quản trị viên), Người thực thi của kho (đã mời hoặc đã nhận việc) và các người nhận khác trong kho.
    /// <paramref name="exceptBeneficiaryId"/> là hồ sơ đang sửa, không tính là "người nhận khác".
    /// </summary>
    public static async Task EnsureEmailAvailableAsync(
        string email,
        Vault vault,
        Guid? exceptBeneficiaryId,
        IUserRepository users,
        IExecutorInvitationRepository invitations,
        IBeneficiaryDataProtector protector,
        IReadOnlyList<Beneficiary> vaultBeneficiaries,
        CancellationToken ct)
    {
        string normalized = email.Trim().ToLowerInvariant();

        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        if (owner != null && string.Equals(owner.Email?.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationErrors.For("email", "Email người nhận không được trùng với email của Chủ kho.");
        }

        User? account = await users.FindByEmailAsync(normalized, ct);
        if (account != null
            && (account.Role == UserRole.Executor || account.Role == UserRole.Verifier || account.Role == UserRole.Admin))
        {
            throw ValidationErrors.For("email",
                "Email này thuộc tài khoản nhân sự của LegacyVault (Người xét duyệt hoặc Người thực thi) nên không thể làm người nhận.");
        }

        List<Executor> invited = await invitations.ListByInviteeEmailAsync(normalized, 50, ct);
        foreach (Executor executor in invited)
        {
            bool counts = executor.Status == ExecutorStatus.Invited
                || executor.Status == ExecutorStatus.Accepted
                || executor.Status == ExecutorStatus.Suspended;
            if (executor.VaultId == vault.Id && counts)
            {
                throw ValidationErrors.For("email", "Email người nhận không được trùng với email của Người thực thi của kho.");
            }
        }

        foreach (Beneficiary other in vaultBeneficiaries)
        {
            if (exceptBeneficiaryId != null && other.Id == exceptBeneficiaryId.Value)
            {
                continue;
            }
            string? otherEmail = ReadEmail(protector, other);
            if (otherEmail != null && string.Equals(otherEmail, normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw ValidationErrors.For("email", "Email này đã dùng cho một người nhận khác trong kho.");
            }
        }
    }

    private static string? ReadEmail(IBeneficiaryDataProtector protector, Beneficiary beneficiary)
    {
        try
        {
            string? value = protector.Unprotect(beneficiary).Email;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }
        catch (CryptographicException)
        {
            // Hồ sơ không giải mã được (thiếu KEK, dữ liệu hỏng): bỏ qua, không chặn người dùng vì hồ sơ khác.
            return null;
        }
    }
}

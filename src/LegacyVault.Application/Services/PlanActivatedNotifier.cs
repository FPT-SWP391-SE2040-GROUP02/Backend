using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Thư báo người nhận khi kế hoạch được kích hoạt (xem IPlanActivatedNotifier).
/// </summary>
/// <remarks>
/// - Người nhận lấy từ các gói đã sẵn sàng; một người có trong nhiều gói chỉ nhận một thư.
/// - Thư có email và số điện thoại của Chủ kho và Người thực thi; hai người này được báo trước ở màn kích hoạt
///   và màn nhận lời mời rằng thông tin liên hệ của họ sẽ được chia sẻ cho người nhận.
/// - Thư không có tên gói hay tài sản. Gửi sau khi đã lưu kích hoạt, nên gửi lỗi không làm hỏng việc kích hoạt.
/// </remarks>
public sealed class PlanActivatedNotifier(
    IVaultRepository vaults,
    IUserRepository users,
    IHandoverRepository handover,
    IBeneficiaryDataProtector protector,
    IPlanActivatedNoticeSender sender) : IPlanActivatedNotifier
{
    public async Task NotifyAsync(Guid vaultId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault == null)
        {
            return;
        }
        User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
        if (owner == null)
        {
            return;
        }
        User? executor = await FindAcceptedExecutorAsync(vault, ct);

        List<Designation> designations = await handover.FindStartableDesignationsAsync(vault.Id, ct);
        HashSet<Guid> notified = new HashSet<Guid>();
        foreach (Designation designation in designations)
        {
            if (!notified.Add(designation.BeneficiaryId))
            {
                continue;
            }
            try
            {
                BeneficiaryPersonalData data = protector.Unprotect(designation.Beneficiary);
                if (string.IsNullOrWhiteSpace(data.Email))
                {
                    continue;
                }
                PlanActivatedNotice notice = new PlanActivatedNotice
                {
                    ToEmail = data.Email,
                    FullName = data.FullName,
                    OwnerName = owner.FullName,
                    OwnerEmail = owner.Email,
                    OwnerPhone = owner.Phone
                };
                if (executor != null)
                {
                    notice.ExecutorName = executor.FullName;
                    notice.ExecutorEmail = executor.Email;
                    notice.ExecutorPhone = executor.Phone;
                }
                await sender.SendAsync(notice, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Không ném lại: kế hoạch đã kích hoạt; một thư lỗi không chặn các thư còn lại.
            }
        }
    }

    /// <summary>Tài khoản của Người thực thi đã chấp nhận lời mời của kho; chưa có thì null.</summary>
    private async Task<User?> FindAcceptedExecutorAsync(Vault vault, CancellationToken ct)
    {
        foreach (Executor item in vault.Executors)
        {
            if (item.Status == ExecutorStatus.Accepted && item.UserId.HasValue)
            {
                return await users.FindByIdAsync(item.UserId.Value, ct);
            }
        }
        return null;
    }
}

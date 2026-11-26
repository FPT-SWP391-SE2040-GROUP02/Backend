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
/// - Thư có email và số điện thoại của Chủ tài sản và Người thực thi; hai người này được báo trước ở màn kích hoạt
///   và màn nhận lời mời rằng thông tin liên hệ của họ sẽ được chia sẻ cho người nhận.
/// - Thư không có tên tài sản. Gửi sau khi đã lưu kích hoạt, nên gửi lỗi không làm hỏng việc kích hoạt.
/// - Mỗi người nhận có link riêng để gửi yêu cầu nhận tài sản khi Chủ tài sản qua đời; kích hoạt lại thì cấp link mới.
/// </remarks>
public sealed class PlanActivatedNotifier(
    IVaultRepository vaults,
    IUserRepository users,
    IAssetAssignmentRepository assignments,
    IBeneficiaryDataProtector protector,
    IPlanActivatedNoticeSender sender,
    IHandoverRecordRepository records,
    IUnitOfWork unitOfWork) : IPlanActivatedNotifier
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

        List<AssetAssignment> ownerAssignments = await assignments.FindDetailedByVaultIdAsync(vault.Id, ct);

        // Cấp link riêng mới cho từng người nhận (link cũ hết hiệu lực), lưu băm trước khi gửi thư.
        Dictionary<Guid, string> tokens = new Dictionary<Guid, string>();
        foreach (AssetAssignment assignment in ownerAssignments)
        {
            if (tokens.ContainsKey(assignment.BeneficiaryId))
            {
                continue;
            }
            Beneficiary? tracked = await records.FindBeneficiaryForUpdateAsync(assignment.BeneficiaryId, ct);
            if (tracked == null)
            {
                continue;
            }
            (string value, string hash) = HandoverRecordRules.CreateRequestToken();
            tracked.RequestTokenHash = hash;
            tokens[assignment.BeneficiaryId] = value;
        }
        if (tokens.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        HashSet<Guid> notified = new HashSet<Guid>();
        foreach (AssetAssignment assignment in ownerAssignments)
        {
            if (!notified.Add(assignment.BeneficiaryId))
            {
                continue;
            }
            try
            {
                BeneficiaryPersonalData data = protector.Unprotect(assignment.Beneficiary);
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
                if (tokens.ContainsKey(assignment.BeneficiaryId))
                {
                    notice.RequestToken = tokens[assignment.BeneficiaryId];
                }
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

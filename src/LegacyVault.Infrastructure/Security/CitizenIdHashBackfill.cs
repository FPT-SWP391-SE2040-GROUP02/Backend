using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Điền CitizenIdHash cho các danh tính gắn trước khi có cột này: giải mã từng dòng, tính hash, lưu từng dòng.
/// Chạy được nhiều lần và an toàn khi nhiều instance khởi động cùng lúc (chỉ đụng dòng chưa có hash; instance
/// thua cuộc gặp vi phạm unique thì bỏ qua dòng đó). Lỗi một dòng (không giải mã được, trùng số) chỉ ghi cảnh báo
/// kèm UserId rồi đi tiếp, không bao giờ làm API không khởi động. Không ghi số CCCD vào log.
/// </summary>
public static class CitizenIdHashBackfill
{
    public static async Task<int> RunAsync(
        AppDbContext context, IOwnerIdentityProtector protector, ICitizenIdBlindIndex index, ILogger logger, CancellationToken ct)
    {
        if (!index.Enabled)
        {
            return 0;
        }

        // Một truy vấn lấy cả các dòng cần điền (bảng nhỏ: mỗi Owner tối đa một dòng).
        List<OwnerIdentity> pending = await context.OwnerIdentities.Where(i => i.CitizenIdHash == null).ToListAsync(ct);
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int filled = 0;
        foreach (OwnerIdentity identity in pending)
        {
            try
            {
                string hash = index.Compute(protector.Unprotect(identity).CitizenId)!;
                bool takenInDb = await context.OwnerIdentities.AnyAsync(i => i.CitizenIdHash == hash && i.Id != identity.Id, ct);
                if (takenInDb || !seen.Add(hash))
                {
                    logger.LogWarning("Backfill CCCD: danh tính của user {UserId} trùng số với danh tính khác, bỏ qua.", identity.UserId);
                    continue;
                }
                identity.CitizenIdHash = hash;
                await context.SaveChangesAsync(ct);
                filled++;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Backfill CCCD: bỏ qua danh tính của user {UserId}.", identity.UserId);
                identity.CitizenIdHash = null;
                context.Entry(identity).State = EntityState.Detached;
            }
        }
        return filled;
    }
}

using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Điền CitizenIdHash cho các danh tính gắn trước khi có cột này: giải mã từng dòng, tính hash, lưu từng dòng.
/// Chạy được nhiều lần (chỉ đụng dòng chưa có hash). Dòng trùng hash với dòng khác (dữ liệu cũ đã trùng CCCD)
/// bị bỏ qua và ghi cảnh báo kèm UserId, không ghi số CCCD.
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

        List<Guid> pendingIds = await context.OwnerIdentities
            .Where(i => i.CitizenIdHash == null)
            .Select(i => i.Id)
            .ToListAsync(ct);

        int filled = 0;
        foreach (Guid id in pendingIds)
        {
            OwnerIdentity identity = await context.OwnerIdentities.FirstAsync(i => i.Id == id, ct);
            string hash = index.Compute(protector.Unprotect(identity).CitizenId)!;
            bool taken = await context.OwnerIdentities.AnyAsync(i => i.CitizenIdHash == hash && i.Id != id, ct);
            if (taken)
            {
                logger.LogWarning("Backfill CCCD: danh tính của user {UserId} trùng số với danh tính khác, bỏ qua.", identity.UserId);
                continue;
            }
            identity.CitizenIdHash = hash;
            await context.SaveChangesAsync(ct);
            filled++;
        }
        return filled;
    }
}

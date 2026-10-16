using System.Data.Common;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class StorageReservationRepository : IStorageReservationRepository
{
    private const int LockTimeoutMilliseconds = 10000;

    private readonly AppDbContext context;

    public StorageReservationRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<StorageReservation?> TryReserveAsync(
        Guid vaultId, long bytes, long quotaBytes, DateTimeOffset now, TimeSpan holdFor,
        ReservationOutcome outcome, CancellationToken ct)
    {
        await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct))
        {
            // Khóa riêng của kho, giữ tới hết giao dịch: hai lần tải cùng kho xếp hàng ở bước kiểm + ghi chỗ giữ.
            string resource = "legacyvault:storage:" + vaultId.ToString("N");
            int lockResult = await AcquireLockAsync(resource, transaction, ct);
            if (lockResult < 0)
            {
                throw new TimeoutException("Không lấy được khóa dung lượng của kho.");
            }

            // Đọc chỗ đang giữ TRƯỚC, phần đã dùng SAU: một lần tải chốt giữa hai phép đọc (chỗ giữ chuyển sang Committed
            // cùng lúc tài sản được ghi) chỉ có thể bị đếm hai lần, không bao giờ bị bỏ sót.
            long held = await context.Set<StorageReservation>()
                .Where(r => r.VaultId == vaultId && r.Status == StorageReservationStatus.Active && r.ExpiresAt > now)
                .SumAsync(r => (long?)r.Bytes, ct) ?? 0;
            long used = await context.AssetVersions
                .Where(v => v.Asset.VaultId == vaultId)
                .SumAsync(v => (long?)v.SizeBytes, ct) ?? 0;

            long remaining = Math.Max(0, quotaBytes - used - held);
            outcome.RemainingBytes = remaining;
            if (bytes > remaining)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            StorageReservation reservation = new StorageReservation();
            reservation.VaultId = vaultId;
            reservation.Bytes = bytes;
            reservation.Status = StorageReservationStatus.Active;
            reservation.CreatedAt = now;
            reservation.ExpiresAt = now + holdFor;
            context.Set<StorageReservation>().Add(reservation);
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return reservation;
        }
    }

    public void MarkCommitted(StorageReservation reservation, DateTimeOffset now)
    {
        reservation.Status = StorageReservationStatus.Committed;
        reservation.ClosedAt = now;
    }

    public async Task ReleaseAsync(Guid reservationId, DateTimeOffset now)
    {
        try
        {
            await context.Set<StorageReservation>()
                .Where(r => r.Id == reservationId && r.Status == StorageReservationStatus.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, StorageReservationStatus.Released)
                    .SetProperty(r => r.ClosedAt, (DateTimeOffset?)now));
        }
        catch (Exception)
        {
            // Chỗ giữ sẽ tự hết hạn; không che lỗi gốc của lần tải.
        }
    }

    private async Task<int> AcquireLockAsync(string resource, IDbContextTransaction transaction, CancellationToken ct)
    {
        DbConnection connection = context.Database.GetDbConnection();
        await using (DbCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', "
                + "@LockOwner = 'Transaction', @LockTimeout = @timeout; SELECT @r;";
            DbParameter resParameter = command.CreateParameter();
            resParameter.ParameterName = "@res";
            resParameter.Value = resource;
            command.Parameters.Add(resParameter);
            DbParameter timeoutParameter = command.CreateParameter();
            timeoutParameter.ParameterName = "@timeout";
            timeoutParameter.Value = LockTimeoutMilliseconds;
            command.Parameters.Add(timeoutParameter);
            object? result = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }
    }
}

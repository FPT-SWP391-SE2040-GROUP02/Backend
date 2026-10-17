using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Gửi thư nhắc lời mời Executor ở giờ thứ 48 (SETUP-20). Trả true chỉ khi thư đã được nhận để gửi.</summary>
public interface IExecutorReminderSender
{
    Task<bool> SendAsync(string email, string role, DateTimeOffset expiresAt, CancellationToken ct);
}

public interface IExecutorInvitationJob
{
    /// <summary>Id các lời mời đang chờ cần xử lý (quá 7 ngày hoặc tới giờ nhắc), cũ nhất trước.</summary>
    Task<List<Guid>> FindDueIdsAsync(CancellationToken ct);

    /// <summary>Trả "EXPIRED", "REMINDED", hoặc null khi không có gì để làm. Chạy lặp an toàn (TIME-02).</summary>
    Task<string?> ProcessAsync(Guid executorId, CancellationToken ct);
}

/// <remarks>
/// SETUP-20: lời mời hết hạn 7 ngày (EndedAt = ExpiresAt, mốc thật), nhắc MỘT lần ở giờ thứ 48. Thư nhắc không có link
/// vì DB chỉ lưu mã băm của token; người được mời mở thư mời trước đó, hoặc Owner "Mời lại" để có link mới.
/// Gửi nhắc trước rồi mới lưu ReminderSentAt; gửi lỗi thì không đánh dấu và vòng sau thử lại. Không đổi UpdatedAt nên etag
/// của Owner giữ nguyên.
/// </remarks>
public sealed class ExecutorInvitationJob(IExecutorInvitationRepository invitations, IExecutorReminderSender sender,
    IUnitOfWork unitOfWork, TimeProvider time) : IExecutorInvitationJob
{
    private const int BatchSize = 50;

    /// <summary>SETUP-20: nhắc sau 48 giờ chưa trả lời.</summary>
    public static readonly TimeSpan ReminderAfter = TimeSpan.FromHours(48);

    public Task<List<Guid>> FindDueIdsAsync(CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        return invitations.FindDueForJobAsync(now, now - ReminderAfter, BatchSize, ct);
    }

    public async Task<string?> ProcessAsync(Guid executorId, CancellationToken ct)
    {
        Executor? item = await invitations.FindByIdAsync(executorId, ct);
        if (item == null || item.Status != ExecutorStatus.Invited)
        {
            return null;
        }

        DateTimeOffset now = time.GetUtcNow();
        if (now >= item.ExpiresAt)
        {
            item.Status = ExecutorStatus.Expired;
            item.EndedAt = item.ExpiresAt;
            item.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(ct);
            return "EXPIRED";
        }

        if (item.ReminderSentAt != null || now < item.InvitedAt + ReminderAfter)
        {
            return null;
        }
        bool sent = await sender.SendAsync(item.InviteeEmail, item.Role.ToString().ToUpperInvariant(), item.ExpiresAt, ct);
        if (!sent)
        {
            return null;
        }
        item.ReminderSentAt = now;
        await unitOfWork.SaveChangesAsync(ct);
        return "REMINDED";
    }
}

using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - D3: gửi hồ sơ chỉ tạo dòng Pending; job này gửi thư. SMTP nhận → Delivered, DeliveredAt;
///   lỗi → tăng AttemptCount, đủ 5 lần → Failed (TODO: tạo sự cố F4, có người và hạn xử lý theo DEATH-08).
/// - DEATH-08: 72 giờ chỉ bắt đầu khi thông báo của bản NotifiedVersionNo được giao.
/// - DEATH-11: thông báo của bản cũ (đã có bản trọng yếu mới hơn) được giao thì KHÔNG khởi động đồng hồ.
/// - Mỗi thông báo một SaveChanges. Gửi thư thành công nhưng lưu lỗi (xung đột) thì vòng sau gửi lại:
///   chấp nhận gửi ít nhất một lần, không bao giờ mất thông báo.
/// - Không tăng DeathCase.UpdatedAt khi đặt mốc 72 giờ: đây không phải sửa nội dung nên etag của Executor
///   không bị đổi. Concurrency token vẫn nằm trong WHERE nên nếu Executor vừa gửi bản mới (đổi UpdatedAt)
///   thì lần lưu này xung đột và vòng sau xét lại.
/// - SEC-07: không đưa CCCD, tên Executor vào thư (DeathCaseOwnerNotice không có các trường đó).
/// </remarks>
public sealed class DeathCaseNoticeJob : IDeathCaseNoticeJob
{
    /// <summary>Số thông báo tối đa xử lý mỗi vòng quét.</summary>
    private const int BatchSize = 50;

    /// <summary>Số ký tự của Id hồ sơ dùng làm mã rút gọn trong thư.</summary>
    private const int CaseCodeLength = 8;

    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IUserRepository userRepository;
    private readonly IDeathCaseNoticeSender noticeSender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public DeathCaseNoticeJob(
        IDeathCaseRepository deathCaseRepository,
        IUserRepository userRepository,
        IDeathCaseNoticeSender noticeSender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.deathCaseRepository = deathCaseRepository;
        this.userRepository = userRepository;
        this.noticeSender = noticeSender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<List<Guid>> FindPendingNoticeIdsAsync(CancellationToken ct)
    {
        return deathCaseRepository.FindPendingNoticeIdsAsync(BatchSize, ct);
    }

    public async Task<DeathCaseNoticeStatus?> ProcessNoticeAsync(Guid noticeId, CancellationToken ct)
    {
        DeathCaseNotice? notice = await deathCaseRepository.FindNoticeForJobAsync(noticeId, ct);
        // TIME-02: đã Delivered/Failed thì không gửi lại.
        if (notice == null || notice.Status != DeathCaseNoticeStatus.Pending)
        {
            return null;
        }

        DeathCase? deathCase = await deathCaseRepository.FindByIdAsync(notice.CaseId, ct);
        if (deathCase == null)
        {
            return null;
        }

        User? owner = await userRepository.FindByIdAsync(notice.RecipientUserId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        bool delivered = false;
        if (owner != null && !string.IsNullOrWhiteSpace(owner.Email))
        {
            DeathCaseOwnerNotice message = new DeathCaseOwnerNotice();
            message.NoticeId = notice.Id;
            message.ToEmail = owner.Email;
            message.FullName = owner.FullName;
            message.CaseCode = BuildCaseCode(deathCase.Id);
            message.ExpectedObjectionEndsAt = DeathCaseRules.ObjectionEndsAt(now);
            try
            {
                await noticeSender.SendAsync(message, ct);
                delivered = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Không ghi chi tiết lỗi (có thể chứa địa chỉ email); trạng thái lưu ở AttemptCount/LastAttemptAt.
                delivered = false;
            }
        }

        notice.AttemptCount = notice.AttemptCount + 1;
        notice.LastAttemptAt = now;
        notice.UpdatedAt = now;
        if (delivered)
        {
            notice.Status = DeathCaseNoticeStatus.Delivered;
            notice.DeliveredAt = now;
            // DEATH-08, DEATH-11: chỉ thông báo của bản đang cần báo mới khởi động 72 giờ, và chỉ một lần.
            if (notice.VersionNo == deathCase.NotifiedVersionNo && deathCase.ObjectionStartsAt == null)
            {
                deathCase.ObjectionStartsAt = now;
                deathCase.ObjectionEndsAt = DeathCaseRules.ObjectionEndsAt(now);

                // ASSIGN-04, D1, TIME-07: hạn Verifier không sớm hơn 48 giờ sau khi hết phản đối.
                // Không đổi RemindedDueAt: hạn mới khác hạn đã nhắc nên sẽ được nhắc lại.
                foreach (DeathCaseAssignment assignment in deathCase.Assignments)
                {
                    if (assignment.EndedAt == null)
                    {
                        assignment.DueAt = DeathCaseAssignmentRules.ExtendForObjection(
                            assignment.DueAt, deathCase.ObjectionEndsAt.Value);
                    }
                }
            }
        }
        else if (notice.AttemptCount >= DeathCaseRules.MaxNoticeAttempts)
        {
            // TODO: F4 — tạo sự cố có người và hạn xử lý khi mọi kênh đều lỗi (DEATH-08). Hiện monitor chỉ ghi log.
            notice.Status = DeathCaseNoticeStatus.Failed;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return notice.Status;
    }

    /// <summary>
    /// Mã hồ sơ rút gọn: 8 ký tự hex CUỐI của Id, chữ hoa. Không lấy phần đầu vì Guid v7 bắt đầu bằng mốc thời gian,
    /// các hồ sơ tạo gần nhau sẽ trùng mã.
    /// </summary>
    private static string BuildCaseCode(Guid caseId)
    {
        string hex = caseId.ToString("N");
        return hex.Substring(hex.Length - CaseCodeLength).ToUpperInvariant();
    }
}

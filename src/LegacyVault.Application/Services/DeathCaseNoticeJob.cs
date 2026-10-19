using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - D3: gửi hồ sơ chỉ tạo dòng Pending; job này gửi thư. SMTP nhận → Delivered, DeliveredAt;
///   lỗi → tăng AttemptCount, đủ 5 lần → Failed và mở sự cố F4 có người phụ trách, có hạn xử lý (DEATH-08, INCIDENT NotificationFailed).
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

    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IUserRepository userRepository;
    private readonly IDeathCaseNoticeSender noticeSender;
    private readonly IIncidentService incidentService;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public DeathCaseNoticeJob(
        IDeathCaseRepository deathCaseRepository,
        IUserRepository userRepository,
        IDeathCaseNoticeSender noticeSender,
        IIncidentService incidentService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.incidentService = incidentService;
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
            message.CaseCode = DeathCaseRules.CaseCode(deathCase.Id);
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
            // DEATH-08: mọi kênh đều lỗi thì tạo sự cố có người phụ trách và hạn; lưu cùng giao dịch với dòng Failed
            // nên không có chuyện Failed mà không có sự cố. Chưa có ai nhận thì RaiseAsync chọn Admin ít việc nhất.
            notice.Status = DeathCaseNoticeStatus.Failed;
            await incidentService.RaiseAsync(IncidentType.NotificationFailed, deathCase.VaultId,
                IncidentRules.TargetDeathCase, deathCase.Id,
                "Không gửi được thông báo hồ sơ chứng tử cho Owner sau nhiều lần thử.", null, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return notice.Status;
    }
}

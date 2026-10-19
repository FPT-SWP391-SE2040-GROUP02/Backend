using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền gửi lại lời mời F3 chưa giao được (OPS-04): lần gửi đầu lỗi, hoặc tiến trình dừng sau khi lưu lượt
/// nhưng trước khi gửi thư. Mỗi hàm Process* đọc lại dữ liệu và kiểm lại điều kiện nên chạy lặp là an toàn.
/// </summary>
public interface IHandoverInvitationRetryJob
{
    /// <summary>Id các lượt cần thử gửi (đã đủ tuổi, chưa đủ số lần thử). Còn phải kiểm giãn cách ở ProcessTurnAsync.</summary>
    Task<IReadOnlyList<Guid>> FindPendingTurnIdsAsync(CancellationToken ct);

    /// <summary>Thử gửi một lượt. Thành công thì đặt InvitationSentAt; đủ số lần lỗi thì mở sự cố NotificationFailed.</summary>
    Task ProcessTurnAsync(Guid turnId, CancellationToken ct);
}

/// <remarks>
/// - Lượt phải còn Invited, chưa ai nhận và kế hoạch còn HandoverInProgress; kho có hold thì tạm dừng (không tính là lần lỗi).
/// - DB chỉ giữ mã băm nên mỗi lần gửi lại là token mới (INVITE-01); chỉ lưu mã băm mới sau khi email được nhận,
///   nên gửi lỗi thì link cũ còn nguyên. Lượt có RowVersion nên hai tiến trình cùng xử lý thì bên lưu sau bị xung đột.
/// - INVITE-04: lời mời chưa từng giao được thì hạn phản hồi bắt đầu tính từ lần giao thành công đầu tiên.
/// - Giãn cách giữa các lần thử tăng dần (5 phút nhân số lần lỗi); đủ 5 lần lỗi hoặc người nhận không có email
///   thì ngừng thử và mở sự cố có người phụ trách (ASSIGN-04) để Executor/Admin xử lý, không để trôi.
/// - Sự cố và log không chứa email, token hay tên gói (OPS-04, SEC-07).
/// </remarks>
public sealed class HandoverInvitationRetryJob : IHandoverInvitationRetryJob
{
    public const int MaxAttempts = 5;

    /// <summary>Lượt phải đủ tuổi này mới được job gửi, để không tranh với HandoverStarter đang gửi lần đầu.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);

    /// <summary>Giãn cách cơ bản giữa các lần thử; lần thứ n chờ n lần giá trị này.</summary>
    public static readonly TimeSpan BackoffStep = TimeSpan.FromMinutes(5);

    public const string TargetTurn = "HANDOVER_TURN";

    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly ITokenService tokens;
    private readonly IHandoverInvitationSender sender;
    private readonly IHoldReader holdReader;
    private readonly IIncidentService incidentService;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverInvitationRetryJob(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        ITokenService tokens,
        IHandoverInvitationSender sender,
        IHoldReader holdReader,
        IIncidentService incidentService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.vaults = vaults;
        this.handover = handover;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.tokens = tokens;
        this.sender = sender;
        this.holdReader = holdReader;
        this.incidentService = incidentService;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<Guid>> FindPendingTurnIdsAsync(CancellationToken ct)
    {
        DateTimeOffset createdBefore = timeProvider.GetUtcNow() - MinAge;
        return handover.FindUndeliveredInvitationTurnIdsAsync(createdBefore, MaxAttempts, ct);
    }

    public async Task ProcessTurnAsync(Guid turnId, CancellationToken ct)
    {
        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (turn == null || turn.Status != HandoverTurnStatus.Invited || turn.InvitationSentAt != null
            || turn.ClaimantUserId != null || turn.InvitationAttempts >= MaxAttempts)
        {
            return;
        }
        if (now - turn.CreatedAt < MinAge || !BackoffElapsed(turn, now))
        {
            return;
        }

        Vault? vault = await vaults.FindByIdAsync(turn.VaultId, ct);
        if (vault == null || vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            return;
        }
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            // Kho đang bị giữ: tạm dừng, không tính là lần lỗi (cùng luật với gửi lại thủ công).
            return;
        }

        string? email = null;
        string fullName = string.Empty;
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, turn.BeneficiaryId, ct);
        if (beneficiary != null)
        {
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            email = data.Email;
            fullName = data.FullName;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            // Không có email thì thử lại cũng vô ích: ngừng thử và giao cho người xử lý.
            turn.InvitationAttempts = MaxAttempts;
            turn.InvitationLastAttemptAt = now;
            turn.UpdatedAt = now;
            await RaiseIncidentAsync(vault, turn, "Người nhận chưa có email nên chưa gửi được lời mời nhận tài sản.", ct);
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        // INVITE-04: chưa từng giao được thì hạn phản hồi tính từ lần giao thành công đầu tiên.
        DateTimeOffset responseDeadline = now + HandoverRules.ResponseWindow;
        OpaqueToken token = tokens.CreateOpaqueToken();
        HandoverInvitation invitation = new HandoverInvitation();
        invitation.TurnId = turn.Id;
        invitation.ToEmail = email;
        invitation.FullName = fullName;
        invitation.Token = token.Value;
        invitation.ResponseDeadline = responseDeadline;

        try
        {
            await sender.SendAsync(invitation, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Không ghi chi tiết lỗi (có thể chứa email). Chưa lưu token mới nên link cũ còn nguyên.
            turn.InvitationAttempts = turn.InvitationAttempts + 1;
            turn.InvitationLastAttemptAt = now;
            turn.UpdatedAt = now;
            if (turn.InvitationAttempts >= MaxAttempts)
            {
                await RaiseIncidentAsync(vault, turn,
                    "Không gửi được lời mời nhận tài sản cho người nhận sau nhiều lần thử.", ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        turn.InvitationTokenHash = token.Hash;
        turn.InvitationSentAt = now;
        turn.ResponseDeadline = responseDeadline;
        turn.UpdatedAt = now;
        // Đã giao được: đóng sự cố (nếu trước đó đã mở vì lỗi) cùng lần lưu.
        await incidentService.ResolveByTargetAsync(IncidentType.NotificationFailed, TargetTurn, turn.Id,
            "Lời mời đã được gửi.", ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static bool BackoffElapsed(HandoverTurn turn, DateTimeOffset now)
    {
        if (turn.InvitationLastAttemptAt == null || turn.InvitationAttempts <= 0)
        {
            return true;
        }
        TimeSpan wait = TimeSpan.FromTicks(BackoffStep.Ticks * turn.InvitationAttempts);
        return now - turn.InvitationLastAttemptAt.Value >= wait;
    }

    private async Task RaiseIncidentAsync(Vault vault, HandoverTurn turn, string summary, CancellationToken ct)
    {
        await incidentService.RaiseAsync(IncidentType.NotificationFailed, vault.Id, TargetTurn, turn.Id, summary, null, ct);
    }
}

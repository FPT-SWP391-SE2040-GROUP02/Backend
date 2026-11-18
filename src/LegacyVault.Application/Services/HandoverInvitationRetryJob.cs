using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Application.Services;

/// <summary>
/// Việc nền gửi lại lời mời F3 chưa giao được: lần gửi đầu lỗi, hoặc tiến trình dừng sau khi lưu lượt
/// nhưng trước khi gửi thư. Mỗi hàm Process* đọc lại dữ liệu và kiểm lại điều kiện nên chạy lặp là an toàn.
/// </summary>
public interface IHandoverInvitationRetryJob
{
    /// <summary>Id các lượt cần thử gửi (đã đủ tuổi, chưa đủ số lần thử). Còn phải kiểm giãn cách ở ProcessTurnAsync.</summary>
    Task<IReadOnlyList<Guid>> FindPendingTurnIdsAsync(CancellationToken ct);

    /// <summary>Thử gửi một lượt. Thành công thì đặt InvitationSentAt; đủ số lần lỗi thì ghi log cảnh báo và ngừng thử.</summary>
    Task ProcessTurnAsync(Guid turnId, CancellationToken ct);
}

/// <remarks>
/// - Lượt phải còn Invited, chưa ai nhận và kế hoạch còn HandoverInProgress; kho có hold thì tạm dừng (không tính là lần lỗi).
/// - DB chỉ giữ mã băm nên mỗi lần gửi lại là token mới; chỉ lưu mã băm mới sau khi email được nhận,
///   nên gửi lỗi thì link cũ còn nguyên. Lượt có RowVersion nên hai tiến trình cùng xử lý thì bên lưu sau bị xung đột.
/// - Lời mời chưa từng giao được thì hạn phản hồi bắt đầu tính từ lần giao thành công đầu tiên.
/// - Giãn cách giữa các lần thử tăng dần (5 phút nhân số lần lỗi); đủ 5 lần lỗi hoặc người nhận không có email
///   thì ngừng thử và ghi log cảnh báo.
/// - Log không chứa email, token hay tên gói.
/// </remarks>
public sealed class HandoverInvitationRetryJob : IHandoverInvitationRetryJob
{
    public const int MaxAttempts = 5;

    /// <summary>Lượt phải đủ tuổi này mới được job gửi, để không tranh với HandoverStarter đang gửi lần đầu.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);

    /// <summary>Giãn cách cơ bản giữa các lần thử; lần thứ n chờ n lần giá trị này.</summary>
    public static readonly TimeSpan BackoffStep = TimeSpan.FromMinutes(5);

    private readonly IVaultRepository vaults;
    private readonly IHandoverRepository handover;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly ITokenService tokens;
    private readonly IHandoverInvitationSender sender;
    private readonly IHoldReader holdReader;
    private readonly ILogger<HandoverInvitationRetryJob> logger;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    private readonly IHandoverContactReader? contactReader;

    public HandoverInvitationRetryJob(
        IVaultRepository vaults,
        IHandoverRepository handover,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        ITokenService tokens,
        IHandoverInvitationSender sender,
        IHoldReader holdReader,
        ILogger<HandoverInvitationRetryJob> logger,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IHandoverContactReader? contactReader = null)
    {
        this.contactReader = contactReader;
        this.vaults = vaults;
        this.handover = handover;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.tokens = tokens;
        this.sender = sender;
        this.holdReader = holdReader;
        this.logger = logger;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<Guid>> FindPendingTurnIdsAsync(CancellationToken ct)
    {
        DateTimeOffset createdBefore = timeProvider.GetUtcNow() - MinAge;
        return handover.FindUndeliveredInvitationTurnIdsAsync(createdBefore, MaxAttempts, ct);
    }

    // Gửi lại thư mời cho một lượt chưa gửi được (tối đa MaxAttempts lần, cách nhau tăng dần).
    public async Task ProcessTurnAsync(Guid turnId, CancellationToken ct)
    {
        // 1. Lượt phải còn chờ: chưa gửi được, chưa ai nhận, chưa hết lượt thử, đã đủ thời gian chờ
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

        // 2. Kế hoạch phải đang bàn giao; kho đang bị giữ thì tạm dừng
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

        // 3. Lấy email người nhận; không có email thì ngừng thử và ghi cảnh báo
        string? email = null;
        string fullName = string.Empty;
        string? ownerMessage = null;
        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, turn.BeneficiaryId, ct);
        if (beneficiary != null)
        {
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            email = data.Email;
            fullName = data.FullName;
            ownerMessage = data.Message;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            // Không có email thì thử lại cũng vô ích: ngừng thử và ghi log cảnh báo.
            turn.InvitationAttempts = MaxAttempts;
            turn.InvitationLastAttemptAt = now;
            turn.UpdatedAt = now;
            WarnInvitationNotSent(vault, turn, "Người nhận chưa có email nên chưa gửi được lời mời nhận tài sản.");
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        // 4. Soạn thư mời với token mới và hạn phản hồi mới
        // Chưa từng giao được thì hạn phản hồi tính từ lần giao thành công đầu tiên.
        DateTimeOffset responseDeadline = now + HandoverRules.ResponseWindow;
        OpaqueToken token = tokens.CreateOpaqueToken();
        HandoverInvitation invitation = new HandoverInvitation
        {
            TurnId = turn.Id,
            ToEmail = email,
            FullName = fullName,
            Token = token.Value,
            ResponseDeadline = responseDeadline,
            OwnerMessage = ownerMessage
        };
        if (contactReader != null)
        {
            invitation.ApplyContacts(await contactReader.FindAsync(vault.Id, ct));
        }

        try
        {
            // 5. Gửi thư; lỗi thì tăng số lần thử, giữ link cũ, lưu và dừng
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
                WarnInvitationNotSent(vault, turn, "Không gửi được lời mời nhận tài sản cho người nhận sau nhiều lần thử.");
            }
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        // 6. Gửi được: thay token, ghi mốc đã gửi và hạn phản hồi mới, lưu
        turn.InvitationTokenHash = token.Hash;
        turn.InvitationSentAt = now;
        turn.ResponseDeadline = responseDeadline;
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Đã đủ thời gian chờ giữa hai lần thử chưa: lần thứ n phải chờ n lần BackoffStep kể từ lần thử trước. Chưa thử lần nào thì được thử ngay.</summary>
    private static bool BackoffElapsed(HandoverTurn turn, DateTimeOffset now)
    {
        if (turn.InvitationLastAttemptAt == null || turn.InvitationAttempts <= 0)
        {
            return true;
        }
        TimeSpan wait = TimeSpan.FromTicks(BackoffStep.Ticks * turn.InvitationAttempts);
        return now - turn.InvitationLastAttemptAt.Value >= wait;
    }

    /// <summary>Chỉ ghi mã lượt và mã kho, không ghi email hay tên người nhận.</summary>
    private void WarnInvitationNotSent(Vault vault, HandoverTurn turn, string reason)
    {
        logger.LogWarning("Lời mời bàn giao không gửi được: {Reason} (lượt {TurnId}, kho {VaultId}).", reason, turn.Id, vault.Id);
    }
}

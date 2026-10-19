using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// - H2: trước đây lời mời lỗi hoặc người nhận không có email thì không có đường gửi lại. Executor của kho được gửi lại
///   cho lượt chưa ai nhận (Invited hoặc InitialResponseExpired).
/// - INVITE-02: gửi lại không đổi người nhận và không đặt lại hạn phản hồi. Ngoại lệ INVITE-04: lời mời chưa từng được giao
///   (InvitationSentAt null) thì hạn 5 ngày bắt đầu từ lần giao thành công đầu tiên.
/// - INVITE-01: DB chỉ giữ mã băm nên không có link cũ để gửi; mỗi lần gửi lại là token mới, link cũ hết dùng. Chỉ lưu mã băm
///   mới sau khi email được nhận, nên gửi lỗi thì link cũ còn nguyên.
/// - SEC-05: Executor không thấy email hay link; email chỉ giải mã ở đây để gửi.
/// </remarks>
public sealed class HandoverInvitationService : IHandoverInvitationService
{
    /// <summary>Giãn cách tối thiểu giữa hai lần gửi để Executor không dội thư vào người nhận.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(15);

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverProgressReader progressReader;
    private readonly IHandoverRepository handover;
    private readonly IBeneficiaryRepository beneficiaries;
    private readonly IBeneficiaryDataProtector protector;
    private readonly ITokenService tokens;
    private readonly IHandoverInvitationSender sender;
    private readonly IHoldReader holdReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverInvitationService(
        ICurrentUser currentUser,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IHandoverProgressReader progressReader,
        IHandoverRepository handover,
        IBeneficiaryRepository beneficiaries,
        IBeneficiaryDataProtector protector,
        ITokenService tokens,
        IHandoverInvitationSender sender,
        IHoldReader holdReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.progressReader = progressReader;
        this.handover = handover;
        this.beneficiaries = beneficiaries;
        this.protector = protector;
        this.tokens = tokens;
        this.sender = sender;
        this.holdReader = holdReader;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ResendHandoverInvitationResponse> ResendAsync(Guid caseId, Guid turnId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        DeathCase? deathCase = await progressReader.FindCaseAsync(caseId, ct);
        if (deathCase == null)
        {
            throw CaseNotFound();
        }
        Vault? vault = await vaults.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw CaseNotFound();
        }

        HandoverTurn? turn = await handover.FindTurnByIdAsync(turnId, ct);
        if (turn == null || turn.PlanId != deathCase.PlanId || turn.VaultId != vault.Id)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound,
                "Không tìm thấy nội dung bàn giao.");
        }

        if (vault.Plan.Status != PlanStatus.HandoverInProgress)
        {
            throw NotAllowed(AppErrorType.Conflict, "PLAN_NOT_IN_HANDOVER", "Kế hoạch không còn ở giai đoạn bàn giao.");
        }
        if (!HandoverRules.CanClaim(turn.Status) || turn.ClaimantUserId != null)
        {
            throw NotAllowed(AppErrorType.Conflict, "WRONG_STATE", "Lượt này đã có người nhận hoặc đã kết thúc.");
        }
        if (await holdReader.HasActiveHoldAsync(vault.Id, ct))
        {
            throw NotAllowed(AppErrorType.Conflict, "HOLD_ACTIVE", "Kho đang bị giữ nên chưa gửi lại được.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (turn.InvitationSentAt != null && now - turn.InvitationSentAt.Value < ResendCooldown)
        {
            throw NotAllowed(AppErrorType.Conflict, "RESEND_TOO_SOON", "Vừa gửi lời mời xong, vui lòng thử lại sau.");
        }

        Beneficiary? beneficiary = await beneficiaries.FindByIdAsync(vault.Id, turn.BeneficiaryId, ct);
        string? email = null;
        string fullName = string.Empty;
        if (beneficiary != null)
        {
            BeneficiaryPersonalData data = protector.Unprotect(beneficiary);
            email = data.Email;
            fullName = data.FullName;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw NotAllowed(AppErrorType.Conflict, "NO_EMAIL", "Người nhận chưa có email để gửi lời mời.");
        }

        bool firstDelivery = turn.InvitationSentAt == null;
        DateTimeOffset responseDeadline = turn.ResponseDeadline;
        if (firstDelivery)
        {
            // INVITE-04: chưa từng giao được thì chưa chạy hạn; hạn 5 ngày tính từ lần giao thành công đầu tiên.
            responseDeadline = now + HandoverRules.ResponseWindow;
        }

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
            // Không ghi chi tiết lỗi (có thể chứa email). Chưa lưu gì nên link cũ còn nguyên (INVITE-04).
            throw NotAllowed(AppErrorType.ServiceUnavailable, "INVITATION_SEND_FAILED",
                "Chưa gửi được lời mời lúc này. Vui lòng thử lại sau.");
        }

        turn.InvitationTokenHash = token.Hash;
        turn.InvitationSentAt = now;
        turn.ResponseDeadline = responseDeadline;
        if (firstDelivery && turn.Status == HandoverTurnStatus.InitialResponseExpired)
        {
            // Hạn mới tính lại từ lúc giao được nên lượt trở về trạng thái chờ phản hồi.
            turn.Status = HandoverTurnStatus.Invited;
        }
        turn.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        ResendHandoverInvitationResponse response = new ResendHandoverInvitationResponse();
        response.TurnId = turn.Id;
        response.InvitationSentAt = now;
        response.ResponseDeadline = turn.ResponseDeadline;
        return response;
    }

    /// <summary>Executor đang hiệu lực của kho và không phải Owner, cùng luật với phần xem tiến độ.</summary>
    private async Task<bool> IsActiveExecutorAsync(Vault vault, Guid userId, CancellationToken ct)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }

    private static AppException CaseNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
    }

    private static AppException NotAllowed(AppErrorType type, string reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(type, ErrorCode.HandoverNotAllowed, message, extensions);
    }
}

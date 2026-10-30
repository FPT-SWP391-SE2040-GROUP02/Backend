using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// SEC-07: không ghi PIN, hash PIN hay mật khẩu vào nhật ký, email hay message lỗi.
/// Sai PIN lưu bộ đếm rồi mới trả lỗi, nên lần thử sai không bị mất khi request kết thúc bằng exception.
/// </remarks>
public sealed class VaultPinService : IVaultPinService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IVaultPinRepository pinRepository;
    private readonly IAssetRepository assetRepository;
    private readonly IVaultPinHasher pinHasher;
    private readonly IPasswordHasher passwordHasher;
    private readonly IUserRepository userRepository;
    private readonly IEmailSender emailSender;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public VaultPinService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IVaultPinRepository pinRepository,
        IAssetRepository assetRepository,
        IVaultPinHasher pinHasher,
        IPasswordHasher passwordHasher,
        IUserRepository userRepository,
        IEmailSender emailSender,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.pinRepository = pinRepository;
        this.assetRepository = assetRepository;
        this.pinHasher = pinHasher;
        this.passwordHasher = passwordHasher;
        this.userRepository = userRepository;
        this.emailSender = emailSender;
        this.auditLogRepository = auditLogRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<VaultPinStatusResponse> GetStatusAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultPin? pin = await pinRepository.FindByVaultIdAsync(vault.Id, ct);
        int assetCount = await assetRepository.CountByVaultIdAsync(vault.Id, ct);
        return ToStatus(pin, assetCount > 0, timeProvider.GetUtcNow());
    }

    public async Task<VaultPinStatusResponse> SetPinAsync(Guid vaultId, SetVaultPinRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultPinRules.EnsureAcceptable(request.Pin, request.ConfirmPin, "pin", "confirmPin");
        if (await pinRepository.FindByVaultIdAsync(vault.Id, ct) != null)
        {
            throw AlreadySet();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        VaultPin pin = new VaultPin();
        pin.VaultId = vault.Id;
        pin.PinHash = pinHasher.Hash(vault.Id, request.Pin);
        pin.SetAt = now;
        pinRepository.Add(pin);
        AddAudit(vault, AuditAction.VaultPinSet, AuditOutcome.Succeeded, now);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (pinRepository.IsUniqueViolation(ex))
        {
            // Hai request cùng đặt PIN lần đầu: chỉ một bên thắng.
            pinRepository.DiscardPendingChanges();
            throw AlreadySet();
        }

        await NotifyAsync(vault.OwnerId, VaultPinNoticeKind.Set, null, ct);
        return await StatusAsync(vault.Id, pin, now, ct);
    }

    public async Task<VaultPinStatusResponse> ChangePinAsync(
        Guid vaultId, ChangeVaultPinRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultPin pin = await FindPinOrThrowAsync(vault.Id, ct);
        VaultPinRules.EnsureAcceptable(request.NewPin, request.ConfirmPin, "newPin", "confirmPin");

        DateTimeOffset now = timeProvider.GetUtcNow();
        await CheckPinAsync(vault, pin, request.CurrentPin, now, ct);

        pin.PinHash = pinHasher.Hash(vault.Id, request.NewPin);
        pin.UpdatedAt = now;
        AddAudit(vault, AuditAction.VaultPinChanged, AuditOutcome.Succeeded, now);
        await unitOfWork.SaveChangesAsync(ct);

        await NotifyAsync(vault.OwnerId, VaultPinNoticeKind.Changed, null, ct);
        return await StatusAsync(vault.Id, pin, now, ct);
    }

    public async Task<VaultPinStatusResponse> ResetPinAsync(
        Guid vaultId, ResetVaultPinRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultPin pin = await FindPinOrThrowAsync(vault.Id, ct);
        VaultPinRules.EnsureAcceptable(request.NewPin, request.ConfirmPin, "newPin", "confirmPin");

        User? user = await userRepository.FindByIdAsync(vault.OwnerId, ct);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            // Tài khoản chỉ đăng nhập bằng Google không có mật khẩu để xác nhận.
            throw new AppException(AppErrorType.Conflict, ErrorCode.VaultPinResetNeedsPassword,
                "Tài khoản của bạn chưa có mật khẩu nên chưa đặt lại mã PIN được. Vui lòng đặt mật khẩu trước.");
        }
        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Cùng lỗi với các nơi đòi xác thực lại bằng mật khẩu: 401 nhưng phiên vẫn còn.
            throw new ReauthenticationFailedException();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        pin.PinHash = pinHasher.Hash(vault.Id, request.NewPin);
        pin.UpdatedAt = now;
        pin.FailedCount = 0;
        pin.LockLevel = 0;
        pin.LockedUntil = null;
        AddAudit(vault, AuditAction.VaultPinReset, AuditOutcome.Succeeded, now);
        await unitOfWork.SaveChangesAsync(ct);

        await NotifyAsync(vault.OwnerId, VaultPinNoticeKind.Reset, null, ct);
        return await StatusAsync(vault.Id, pin, now, ct);
    }

    public async Task VerifyAsync(Guid vaultId, string? pin, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultPin? record = await pinRepository.FindByVaultIdAsync(vault.Id, ct);
        if (record == null)
        {
            // Kho chưa có tài sản thì chưa có gì để bảo vệ.
            if (await assetRepository.CountByVaultIdAsync(vault.Id, ct) == 0)
            {
                return;
            }
            throw NotSet();
        }

        await CheckPinAsync(vault, record, pin, timeProvider.GetUtcNow(), ct);
        // Đúng PIN: lưu việc đưa bộ đếm sai về 0 (không có thay đổi nào thì EF không ghi gì).
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task EnsurePinSetBeforeNewAssetAsync(Guid vaultId, CancellationToken ct)
    {
        if (await pinRepository.FindByVaultIdAsync(vaultId, ct) != null)
        {
            return;
        }
        if (await assetRepository.CountByVaultIdAsync(vaultId, ct) >= 1)
        {
            throw NotSet();
        }
    }

    /// <summary>
    /// Kiểm một lần nhập PIN. Đúng thì đưa bộ đếm sai về 0 (người gọi lưu). Khóa, thiếu PIN hoặc sai thì ném lỗi;
    /// sai PIN lưu bộ đếm ngay trước khi ném.
    /// </summary>
    private async Task CheckPinAsync(Vault vault, VaultPin record, string? pin, DateTimeOffset now, CancellationToken ct)
    {
        if (record.LockedUntil is DateTimeOffset until && until > now)
        {
            throw Locked(until);
        }
        if (string.IsNullOrEmpty(pin))
        {
            // Chưa nhập PIN không tính là một lần đoán.
            throw new AppException(AppErrorType.Forbidden, ErrorCode.VaultPinRequired,
                "Thao tác này cần nhập mã PIN của kho.");
        }

        if (VaultPinRules.IsWellFormed(pin) && pinHasher.Verify(vault.Id, pin, record.PinHash))
        {
            record.FailedCount = 0;
            record.LockLevel = 0;
            record.LockedUntil = null;
            return;
        }

        record.FailedCount++;
        bool locks = record.FailedCount >= VaultPinRules.MaxFailedAttempts;
        if (locks)
        {
            record.LockLevel = Math.Min(record.LockLevel + 1, VaultPinRules.MaxLockLevel);
            record.LockedUntil = now + VaultPinRules.LockDuration(record.LockLevel);
            record.FailedCount = 0;
            AddAudit(vault, AuditAction.VaultPinLocked, AuditOutcome.Denied, now);
        }
        else
        {
            AddAudit(vault, AuditAction.VaultPinWrong, AuditOutcome.Failed, now);
        }
        await unitOfWork.SaveChangesAsync(ct);

        if (locks)
        {
            DateTimeOffset lockedUntil = record.LockedUntil!.Value;
            await NotifyAsync(vault.OwnerId, VaultPinNoticeKind.Locked, lockedUntil, ct);
            throw Locked(lockedUntil);
        }
        throw new AppException(AppErrorType.Forbidden, ErrorCode.InvalidVaultPin, "Mã PIN chưa đúng.",
            new Dictionary<string, object?>
            {
                ["attemptsRemaining"] = VaultPinRules.MaxFailedAttempts - record.FailedCount,
            });
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    private async Task<VaultPin> FindPinOrThrowAsync(Guid vaultId, CancellationToken ct)
    {
        VaultPin? pin = await pinRepository.FindByVaultIdAsync(vaultId, ct);
        if (pin == null)
        {
            throw NotSet();
        }
        return pin;
    }

    private async Task<VaultPinStatusResponse> StatusAsync(
        Guid vaultId, VaultPin pin, DateTimeOffset now, CancellationToken ct)
    {
        int assetCount = await assetRepository.CountByVaultIdAsync(vaultId, ct);
        return ToStatus(pin, assetCount > 0, now);
    }

    private static VaultPinStatusResponse ToStatus(VaultPin? pin, bool isRequired, DateTimeOffset now)
    {
        VaultPinStatusResponse response = new VaultPinStatusResponse();
        response.IsSet = pin != null;
        response.IsRequired = isRequired;
        if (pin != null)
        {
            response.LockedUntil = pin.LockedUntil > now ? pin.LockedUntil : null;
            response.AttemptsRemaining = VaultPinRules.MaxFailedAttempts - pin.FailedCount;
        }
        return response;
    }

    private void AddAudit(Vault vault, AuditAction action, AuditOutcome outcome, DateTimeOffset now)
    {
        // Không ghi PIN hay hash vào nhật ký (SEC-07).
        auditLogRepository.Add(AuditRules.Create(
            vault.Id, vault.OwnerId, AuditActorRole.Owner, action, AuditTargetType.Vault, vault.Id, null,
            outcome, null, now));
    }

    /// <summary>Email báo là phụ: gửi lỗi không được làm hỏng thao tác PIN đã lưu.</summary>
    private async Task NotifyAsync(Guid ownerId, VaultPinNoticeKind kind, DateTimeOffset? lockedUntil, CancellationToken ct)
    {
        try
        {
            User? user = await userRepository.FindByIdAsync(ownerId, ct);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }
            await emailSender.SendVaultPinNoticeAsync(user.Email, user.FullName, kind, lockedUntil, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
        }
    }

    private static AppException NotSet()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.VaultPinNotSet,
            "Kho đã có tài sản nên cần đặt mã PIN trước.");
    }

    private static AppException AlreadySet()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.VaultPinAlreadySet,
            "Kho đã có mã PIN. Hãy dùng chức năng đổi mã PIN.");
    }

    private static AppException Locked(DateTimeOffset lockedUntil)
    {
        return new AppException(AppErrorType.Locked, ErrorCode.VaultPinLocked,
            "Mã PIN đang bị khóa tạm vì nhập sai nhiều lần. Vui lòng thử lại sau.",
            new Dictionary<string, object?> { ["lockedUntil"] = lockedUntil });
    }
}

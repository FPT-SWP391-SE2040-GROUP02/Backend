using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class VaultService(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IVaultRepository vaultRepository,
    IContainerRepository containerRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IVaultService
{
    private const string DefaultVaultName = "Kho của tôi";

    public async Task<OpenVaultResult> OpenOrCreateMyVaultAsync(CreateVaultRequest? request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();

        // Đọc lại user từ DB: token có thể được cấp trước khi tài khoản bị khóa.
        var user = await userRepository.FindByIdAsync(userId, ct)
            ?? throw new AppException(AppErrorType.Unauthorized, ErrorCode.SessionExpired,
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        if (user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive,
                "Tài khoản đang bị khóa hoặc vô hiệu hóa.");
        }

        // Đã có kho (kể cả CLOSED, AC-104) → mở lại kho đó, bỏ qua tên/mô tả gửi lên.
        var existing = await vaultRepository.FindByOwnerIdAsync(userId, ct);
        if (existing is not null)
        {
            return new OpenVaultResult { Created = false, Vault = await ToResponseAsync(existing, ct) };
        }

        if (user.EmailVerifiedAt is null)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.EmailNotVerified,
                "Vui lòng xác minh email trước khi tạo kho.");
        }

        var vault = BuildNewVault(userId, request);
        // SEC-13: ghi nhật ký trong cùng lần lưu với kho. Bên thua (unique OwnerId) không lưu lại lần nào nữa
        // trong request này, nên dòng nhật ký của bên thua không bao giờ được ghi.
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, userId, AuditAction.VaultCreated, AuditTargetType.Vault, vault.Id, null, vault.CreatedAt));
        if (!await vaultRepository.TryAddAsync(vault, ct))
        {
            // Request khác (tab khác, bấm lặp, retry) vừa tạo kho trước: trả lại kho đó (AC-89).
            var winner = await vaultRepository.FindByOwnerIdAsync(userId, ct)
                ?? throw new InvalidOperationException("Unique OwnerId bị từ chối nhưng không đọc lại được kho.");
            return new OpenVaultResult { Created = false, Vault = await ToResponseAsync(winner, ct) };
        }

        return new OpenVaultResult { Created = true, Vault = await ToResponseAsync(vault, ct) };
    }

    public async Task<VaultResponse> GetMyVaultAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var vault = VaultRules.EnsureOwnedBy(await vaultRepository.FindByOwnerIdAsync(userId, ct), userId);
        return await ToResponseAsync(vault, ct);
    }

    public async Task<VaultResponse> GetVaultAsync(Guid vaultId, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var vault = VaultRules.EnsureOwnedBy(await vaultRepository.FindByIdAsync(vaultId, ct), userId);
        return await ToResponseAsync(vault, ct);
    }

    public async Task<VaultResponse> UpdateVaultAsync(Guid vaultId, UpdateVaultRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var vault = VaultRules.EnsureOwnedBy(await vaultRepository.FindByIdAsync(vaultId, ct), userId);
        VaultRules.EnsureEditable(vault);

        if (!Etag.Matches(request.Etag, vault.UpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Kho vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw ValidationErrors.For("name", "Tên kho không được để trống.");
            }
            vault.Name = name;
        }
        if (request.Description is not null)
        {
            vault.Description = TextInput.NullIfBlank(request.Description);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (request.ExecutorNote is not null)
        {
            vault.ExecutorNote = TextInput.NullIfBlank(request.ExecutorNote);
            vault.ExecutorNoteUpdatedAt = now;
        }

        // Không tăng Plan.ConfigVersion: tên/mô tả kho và hướng dẫn cho Executor không thuộc cấu hình bàn giao.
        vault.UpdatedAt = now;
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, userId, AuditAction.VaultUpdated, AuditTargetType.Vault, vault.Id, null, vault.UpdatedAt.Value));
        await unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(vault, ct);
    }

    private Vault BuildNewVault(Guid ownerId, CreateVaultRequest? request)
    {
        var now = timeProvider.GetUtcNow();
        var vault = new Vault
        {
            OwnerId = ownerId,
            Name = TextInput.NullIfBlank(request?.Name) ?? DefaultVaultName,
            Description = TextInput.NullIfBlank(request?.Description),
            Status = VaultStatus.Open,
            CreatedAt = now,
        };
        vault.Plan = new HandoverPlan
        {
            VaultId = vault.Id,
            Status = PlanStatus.Draft,
            ConfigVersion = 1,
            CreatedAt = now,
        };
        vault.Entitlement = new VaultEntitlement
        {
            VaultId = vault.Id,
            Tier = ServiceTier.Free,
            Status = EntitlementStatus.Free,
            StorageQuotaBytes = StorageLimits.FreeQuotaBytes,
            IncludedBeneficiarySlots = 0,
            ExtraBeneficiarySlots = 0,
            CreatedAt = now,
        };
        return vault;
    }

    private async Task<VaultResponse> ToResponseAsync(Vault vault, CancellationToken ct)
    {
        var usedBytes = await vaultRepository.SumUsedBytesAsync(vault.Id, ct);
        var activeContainers = await containerRepository.CountActiveByVaultIdAsync(vault.Id, ct);
        var usedBeneficiarySlots = await containerRepository.CountUsedBeneficiarySlotsAsync(vault.Id, ct);
        return vault.ToResponse(usedBytes, activeContainers, usedBeneficiarySlots);
    }
}

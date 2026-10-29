using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Phần Owner quản lý tài sản đã tạo (LV-10, ASSET-04): tải nội dung, đổi tên/mô tả, xóa.
/// </summary>
/// <remarks>
/// - Nội dung không đổi sau khi tạo; muốn thay thì tạo tài sản mới rồi xóa tài sản cũ.
/// - Xóa chỉ khi tài sản không còn ở gói nào, chưa được ghim vào snapshot bàn giao và kho không có hold.
/// - Tên tài sản, tên tệp và nội dung không bao giờ vào nhật ký hoạt động (SEC-07).
/// </remarks>
public sealed partial class AssetService
{
    public async Task<PreparedDownload> PrepareContentAsync(
        Guid vaultId, Guid assetId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);

        if (asset.ContentDeletedAt != null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound,
                "Nội dung tài sản này đã được xóa theo chính sách lưu dữ liệu.");
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = asset.StorageKey;
        stored.SizeBytes = asset.SizeBytes;
        stored.EncryptionKeyId = asset.EncryptionKeyId;
        stored.WrappedDataKey = asset.WrappedDataKey;

        // Lượt 1: giải mã vào Stream.Null. Kiểm kích thước chỉ xảy ra ở cuối luồng nên phải chạy hết
        // một lượt trước khi gửi byte đầu tiên, nếu không Owner có thể nhận tệp hỏng.
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Nội dung tài sản không vượt qua kiểm tra toàn vẹn.", ex);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, AuditAction.AssetContentDownloaded, AuditTargetType.Asset, asset.Id,
            null, now));
        await unitOfWork.SaveChangesAsync(ct);

        PreparedDownload download = new PreparedDownload();
        download.FileName = ResolveFileName(asset);
        download.ContentType = asset.ContentType;
        download.Length = asset.SizeBytes;
        // Lượt 2: giải mã lần nữa, lần này ghi thẳng ra response.
        download.WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token);
        return download;
    }

    public async Task<AssetResponse> UpdateAssetAsync(
        Guid vaultId, Guid assetId, UpdateAssetRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultRules.EnsureEditable(vault);
        await EnsureNoActiveHoldAsync(vault.Id, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);
        EnsureEtagMatches(request.Etag, asset);

        bool changed = false;
        if (request.Name != null)
        {
            string? name = TextInput.NullIfBlank(request.Name);
            if (name == null)
            {
                throw ValidationErrors.For("name", "Tên tài sản là bắt buộc.");
            }
            if (name.Length > MaxNameLength)
            {
                throw ValidationErrors.For("name", "Tên tài sản tối đa 200 ký tự.");
            }
            if (name != asset.Name)
            {
                asset.Name = name;
                changed = true;
            }
        }
        if (request.Description != null)
        {
            string? description = TextInput.NullIfBlank(request.Description);
            if (description != asset.Description)
            {
                asset.Description = description;
                changed = true;
            }
        }

        if (!changed)
        {
            return asset.ToResponse();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        asset.UpdatedAt = now;
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, AuditAction.AssetUpdated, AuditTargetType.Asset, asset.Id,
            null, now));
        await unitOfWork.SaveChangesAsync(ct);
        return asset.ToResponse();
    }

    public async Task DeleteAssetAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // Xóa là thao tác "giảm": vẫn cho khi dịch vụ đang ân hạn, chặn khi kho đóng hoặc kế hoạch bị khóa.
        VaultRules.EnsureCanReduce(vault);
        await EnsureNoActiveHoldAsync(vault.Id, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);

        ContainerAsset? link = await containerRepository.FindLinkByAssetIdAsync(asset.Id, ct);
        if (link != null)
        {
            throw AssetInUse("Tài sản đang nằm trong một gói. Hãy gỡ khỏi gói trước khi xóa.", "IN_CONTAINER");
        }
        bool pinned = await assetRepository.IsPinnedInHandoverAsync(asset.Id, ct);
        if (pinned)
        {
            throw AssetInUse("Tài sản đã được ghim vào một lượt bàn giao nên không thể xóa.", "PINNED_IN_HANDOVER");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        bool hasStoredFile = asset.ContentDeletedAt == null && asset.StorageDeletedAt == null;

        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, AuditAction.AssetDeleted, AuditTargetType.Asset, asset.Id,
            null, now));
        assetRepository.Remove(asset);
        await unitOfWork.SaveChangesAsync(ct);

        // Chỉ xóa nội dung SAU khi bản ghi đã xóa thành công. Xóa lỗi thì để lại object mồ côi (đã mã hóa, không ai
        // truy cập được) chứ không để bản ghi trỏ vào nội dung đã mất. Không dùng ct vì request có thể đã bị hủy.
        if (hasStoredFile)
        {
            try
            {
                await contentStore.DeleteAsync(asset.StorageKey, CancellationToken.None);
            }
            catch (Exception)
            {
                // Không che việc xóa bản ghi đã thành công; object còn lại cần dọn thủ công.
            }
        }
    }

    private async Task<Asset> LoadAssetAsync(Guid vaultId, Guid assetId, CancellationToken ct)
    {
        Asset? asset = await assetRepository.FindByIdAsync(vaultId, assetId, ct);
        if (asset == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound, "Không tìm thấy tài sản.");
        }
        return asset;
    }

    private async Task EnsureNoActiveHoldAsync(Guid vaultId, CancellationToken ct)
    {
        bool onHold = await holdReader.HasActiveHoldAsync(vaultId, ct);
        if (onHold)
        {
            Dictionary<string, object?> extensions = new Dictionary<string, object?>();
            extensions["reason"] = "HOLD_ACTIVE";
            throw new AppException(AppErrorType.Conflict, ErrorCode.VaultNotEditable,
                "Kho đang bị tạm giữ nên không thể thay đổi tài sản.", extensions);
        }
    }

    private static void EnsureEtagMatches(string? etag, Asset asset)
    {
        if (!Etag.Matches(etag, asset.UpdatedAt))
        {
            throw EditConflict();
        }
    }

    private static AppException EditConflict()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
            "Tài sản vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
    }

    private static AppException AssetInUse(string message, string reason)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.AssetInUse, message, extensions);
    }

    private static string ResolveFileName(Asset asset)
    {
        if (!string.IsNullOrWhiteSpace(asset.FileName))
        {
            return asset.FileName;
        }
        if (asset.Kind == AssetKind.Text)
        {
            return asset.Name + ".txt";
        }
        return asset.Name;
    }
}

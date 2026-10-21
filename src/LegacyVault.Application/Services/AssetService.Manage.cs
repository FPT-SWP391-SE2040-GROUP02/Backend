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
/// Phần Owner quản lý tài sản đã tạo (LV-10, ASSET-04): tải nội dung, đổi tên/mô tả, thay nội dung bằng phiên bản mới, xóa.
/// </summary>
/// <remarks>
/// - Thay nội dung KHÔNG ghi đè: tạo phiên bản mới với khóa DEK riêng; phiên bản cũ vẫn được giữ cho snapshot và quota.
/// - Xóa chỉ khi tài sản không còn ở gói nào, chưa được ghim vào snapshot bàn giao và kho không có hold.
/// - Tên tài sản, tên tệp và nội dung không bao giờ vào nhật ký hoạt động (SEC-07).
/// </remarks>
public sealed partial class AssetService
{
    public async Task<PreparedDownload> PrepareContentAsync(
        Guid vaultId, Guid assetId, int? versionNumber, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);

        int wanted = asset.CurrentVersionNumber;
        if (versionNumber != null)
        {
            wanted = versionNumber.Value;
        }
        AssetVersion? version = asset.Versions.FirstOrDefault(v => v.VersionNumber == wanted);
        if (version == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound, "Không tìm thấy phiên bản của tài sản.");
        }
        if (version.ContentDeletedAt != null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound,
                "Nội dung phiên bản này đã được xóa theo chính sách lưu dữ liệu.");
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = version.StorageKey;
        stored.SizeBytes = version.SizeBytes;
        stored.Sha256 = version.Sha256;
        stored.EncryptionKeyId = version.EncryptionKeyId;
        stored.WrappedDataKey = version.WrappedDataKey;

        // Lượt 1: giải mã vào Stream.Null. Kiểm kích thước và SHA-256 chỉ xảy ra ở cuối luồng nên phải chạy hết
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
            version.VersionNumber, now));
        await unitOfWork.SaveChangesAsync(ct);

        PreparedDownload download = new PreparedDownload();
        download.FileName = ResolveFileName(asset, version);
        download.ContentType = version.ContentType;
        download.Length = version.SizeBytes;
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
            asset.CurrentVersionNumber, now));
        await unitOfWork.SaveChangesAsync(ct);
        return asset.ToResponse();
    }

    public async Task<AssetResponse> CreateFileVersionAsync(
        Guid vaultId, Guid assetId, CreateFileAssetVersionRequest request, CancellationToken ct)
    {
        string? fileName = SanitizeFileName(request.FileName);
        if (fileName == null)
        {
            throw ValidationErrors.For("file", "Vui lòng chọn tệp.");
        }
        if (request.Length <= 0)
        {
            throw ValidationErrors.For("file", "Tệp rỗng.");
        }

        return await AddVersionAsync(vaultId, assetId, AssetKind.File, request.Etag, fileName,
            SanitizeContentType(request.ContentType), request.Length, request.Content, ct);
    }

    public async Task<AssetResponse> CreateTextVersionAsync(
        Guid vaultId, Guid assetId, CreateTextAssetVersionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw ValidationErrors.For("content", "Nội dung là bắt buộc.");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(request.Content);
        using (MemoryStream content = new MemoryStream(bytes, writable: false))
        {
            return await AddVersionAsync(vaultId, assetId, AssetKind.Text, request.Etag, null,
                TextContentType, bytes.LongLength, content, ct);
        }
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
        List<string> storageKeys = new List<string>();
        foreach (AssetVersion version in asset.Versions)
        {
            if (version.ContentDeletedAt == null && version.StorageDeletedAt == null)
            {
                storageKeys.Add(version.StorageKey);
            }
        }

        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, AuditAction.AssetDeleted, AuditTargetType.Asset, asset.Id,
            asset.CurrentVersionNumber, now));
        assetRepository.Remove(asset);
        await unitOfWork.SaveChangesAsync(ct);

        // Chỉ xóa nội dung SAU khi bản ghi đã xóa thành công. Xóa lỗi thì để lại object mồ côi (đã mã hóa, không ai
        // truy cập được) chứ không để bản ghi trỏ vào nội dung đã mất. Không dùng ct vì request có thể đã bị hủy.
        foreach (string storageKey in storageKeys)
        {
            try
            {
                await contentStore.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception)
            {
                // Không che việc xóa bản ghi đã thành công; object còn lại cần dọn thủ công.
            }
        }
    }

    private async Task<AssetResponse> AddVersionAsync(
        Guid vaultId, Guid assetId, AssetKind expectedKind, string etag, string? fileName,
        string contentType, long declaredLength, Stream content, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        VaultRules.EnsureEditable(vault);
        await EnsureNoActiveHoldAsync(vault.Id, ct);
        Asset asset = await LoadAssetAsync(vault.Id, assetId, ct);

        if (asset.Kind != expectedKind)
        {
            if (expectedKind == AssetKind.File)
            {
                throw ValidationErrors.For("file", "Tài sản này là dạng chữ, hãy gửi nội dung chữ thay vì tệp.");
            }
            throw ValidationErrors.For("content", "Tài sản này là dạng tệp, hãy tải tệp thay vì gửi nội dung chữ.");
        }
        EnsureEtagMatches(etag, asset);

        long quotaBytes = vault.Entitlement.StorageQuotaBytes;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // QUOTA-02: phiên bản mới chiếm thêm dung lượng (phiên bản cũ vẫn được giữ), nên cũng phải giữ chỗ.
        ReservationOutcome outcome = new ReservationOutcome();
        StorageReservation? reservation = await reservations.TryReserveAsync(
            vault.Id, declaredLength, quotaBytes, now, ReservationHold, outcome, ct);
        if (reservation == null)
        {
            throw QuotaExceeded(quotaBytes, outcome.RemainingBytes);
        }

        StoredContent stored;
        try
        {
            stored = await contentStore.SaveAsync(content, declaredLength, ct);
        }
        catch (AppException ex) when (ex.Code == ErrorCode.FileTooLarge)
        {
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            throw QuotaExceeded(quotaBytes, outcome.RemainingBytes);
        }
        catch
        {
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            throw;
        }

        AssetVersion version = new AssetVersion();
        version.AssetId = asset.Id;
        version.VersionNumber = asset.CurrentVersionNumber + 1;
        version.FileName = fileName;
        version.ContentType = contentType;
        version.SizeBytes = stored.SizeBytes;
        version.Sha256 = stored.Sha256;
        version.StorageKey = stored.StorageKey;
        version.EncryptionKeyId = stored.EncryptionKeyId;
        version.WrappedDataKey = stored.WrappedDataKey;
        version.CreatedAt = now;

        asset.CurrentVersionNumber = version.VersionNumber;
        asset.UpdatedAt = now;
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, AuditAction.AssetVersionCreated, AuditTargetType.Asset, asset.Id,
            version.VersionNumber, now));
        reservations.MarkCommitted(reservation, now);

        bool added;
        try
        {
            added = await assetRepository.TryAddVersionAsync(version, ct);
        }
        catch
        {
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            await contentStore.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }
        if (!added)
        {
            // Request khác vừa tạo phiên bản cùng số: bỏ nội dung vừa ghi và báo xung đột.
            await reservations.ReleaseAsync(reservation.Id, timeProvider.GetUtcNow());
            await contentStore.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw EditConflict();
        }

        // Nạp lại phiên bản mới vào tài sản để ToResponse thấy phiên bản hiện tại.
        if (!asset.Versions.Contains(version))
        {
            asset.Versions.Add(version);
        }
        return asset.ToResponse();
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

    private static string ResolveFileName(Asset asset, AssetVersion version)
    {
        if (!string.IsNullOrWhiteSpace(version.FileName))
        {
            return version.FileName;
        }
        if (asset.Kind == AssetKind.Text)
        {
            return asset.Name + ".txt";
        }
        return asset.Name;
    }
}

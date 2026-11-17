using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Một cặp gói - người nhận trong bản chụp di chúc (chỉ có id).</summary>
public sealed class WillRecipientPair
{
    public Guid ContainerId { get; set; }

    public Guid BeneficiaryId { get; set; }
}

/// <summary>Một cặp gói - tài sản trong bản chụp di chúc (chỉ có id).</summary>
public sealed class WillAssetPair
{
    public Guid ContainerId { get; set; }

    public Guid AssetId { get; set; }
}

/// <summary>
/// Bản chụp cấu hình bàn giao tại lúc Chủ kho ký. Chỉ chứa id, không có tên tài sản/người nhận (SEC-07).
/// Tập gói được suy ra từ các cặp (một gói READY luôn có ít nhất một người nhận và một tài sản).
/// </summary>
public sealed class WillSnapshot
{
    public List<WillRecipientPair> Recipients { get; set; } = new List<WillRecipientPair>();

    public List<WillAssetPair> Assets { get; set; } = new List<WillAssetPair>();
}

/// <summary>
/// Luật thuần của di chúc số: dựng bản chụp, so sánh với cấu hình hiện tại, tính phần được bàn giao.
/// Quy tắc: bàn giao chỉ theo bản ký gần nhất; tập hiệu lực = giao của (bản chụp) và (cấu hình hiện tại).
/// </summary>
public static class WillVersionRules
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new JsonSerializerOptions();
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        return options;
    }

    /// <summary>
    /// Dựng bản chụp từ các gói hiện tại (kèm Assets và Designations). Chỉ gói READY chưa xóa mới vào bản chụp,
    /// vì chỉ gói READY được bàn giao.
    /// </summary>
    public static WillSnapshot BuildSnapshot(List<Container> containers)
    {
        WillSnapshot snapshot = new WillSnapshot();
        foreach (Container container in containers)
        {
            if (container.Status != ContainerStatus.Ready || container.RemovedAt != null)
            {
                continue;
            }
            foreach (Designation designation in container.Designations)
            {
                WillRecipientPair recipient = new WillRecipientPair();
                recipient.ContainerId = container.Id;
                recipient.BeneficiaryId = designation.BeneficiaryId;
                snapshot.Recipients.Add(recipient);
            }
            foreach (ContainerAsset link in container.Assets)
            {
                WillAssetPair asset = new WillAssetPair();
                asset.ContainerId = container.Id;
                asset.AssetId = link.AssetId;
                snapshot.Assets.Add(asset);
            }
        }
        return snapshot;
    }

    public static string ToJson(WillSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    /// <summary>Đọc bản chụp từ JSON đã lưu. JSON hỏng → JsonException (không đoán thay).</summary>
    public static WillSnapshot FromJson(string json)
    {
        WillSnapshot? snapshot = JsonSerializer.Deserialize<WillSnapshot>(json, JsonOptions);
        if (snapshot == null)
        {
            throw new JsonException("Bản chụp di chúc rỗng.");
        }
        return snapshot;
    }

    /// <summary>Tạo dòng WillVersions mới từ bản chụp (người gọi tự đưa vào repository).</summary>
    public static WillVersion CreateVersion(
        Guid vaultId, int versionNo, int configVersion, WillSnapshot snapshot, DateTimeOffset now)
    {
        WillVersion version = new WillVersion();
        version.VaultId = vaultId;
        version.VersionNo = versionNo;
        version.ConfigVersion = configVersion;
        version.SignedAt = now;
        version.SnapshotJson = ToJson(snapshot);
        return version;
    }

    /// <summary>
    /// So bản ký với cấu hình hiện tại. Gói thêm/xóa tính ở mức gói; tài sản và người nhận chỉ so trong các gói có ở cả hai bên
    /// (gói mới/xóa đã được tính ở mức gói). changedRecipientCount = số gói (có ở cả hai bên) mà tập người nhận khác nhau.
    /// </summary>
    public static UnsignedWillChangesResponse Compare(WillSnapshot signed, WillSnapshot current)
    {
        HashSet<Guid> signedContainers = ContainerIds(signed);
        HashSet<Guid> currentContainers = ContainerIds(current);

        int removedContainers = 0;
        foreach (Guid containerId in signedContainers)
        {
            if (!currentContainers.Contains(containerId))
            {
                removedContainers++;
            }
        }
        int addedContainers = 0;
        foreach (Guid containerId in currentContainers)
        {
            if (!signedContainers.Contains(containerId))
            {
                addedContainers++;
            }
        }

        HashSet<string> signedAssetKeys = new HashSet<string>();
        foreach (WillAssetPair pair in signed.Assets)
        {
            signedAssetKeys.Add(PairKey(pair.ContainerId, pair.AssetId));
        }
        HashSet<string> currentAssetKeys = new HashSet<string>();
        foreach (WillAssetPair pair in current.Assets)
        {
            currentAssetKeys.Add(PairKey(pair.ContainerId, pair.AssetId));
        }
        int removedAssets = 0;
        foreach (WillAssetPair pair in signed.Assets)
        {
            bool inBoth = currentContainers.Contains(pair.ContainerId);
            if (inBoth && !currentAssetKeys.Contains(PairKey(pair.ContainerId, pair.AssetId)))
            {
                removedAssets++;
            }
        }
        int addedAssets = 0;
        foreach (WillAssetPair pair in current.Assets)
        {
            bool inBoth = signedContainers.Contains(pair.ContainerId);
            if (inBoth && !signedAssetKeys.Contains(PairKey(pair.ContainerId, pair.AssetId)))
            {
                addedAssets++;
            }
        }

        HashSet<string> signedRecipientKeys = new HashSet<string>();
        foreach (WillRecipientPair pair in signed.Recipients)
        {
            signedRecipientKeys.Add(PairKey(pair.ContainerId, pair.BeneficiaryId));
        }
        HashSet<string> currentRecipientKeys = new HashSet<string>();
        foreach (WillRecipientPair pair in current.Recipients)
        {
            currentRecipientKeys.Add(PairKey(pair.ContainerId, pair.BeneficiaryId));
        }
        HashSet<Guid> changedContainers = new HashSet<Guid>();
        int removedRecipients = 0;
        foreach (WillRecipientPair pair in signed.Recipients)
        {
            bool inBoth = currentContainers.Contains(pair.ContainerId);
            if (inBoth && !currentRecipientKeys.Contains(PairKey(pair.ContainerId, pair.BeneficiaryId)))
            {
                removedRecipients++;
                changedContainers.Add(pair.ContainerId);
            }
        }
        int addedRecipients = 0;
        foreach (WillRecipientPair pair in current.Recipients)
        {
            bool inBoth = signedContainers.Contains(pair.ContainerId);
            if (inBoth && !signedRecipientKeys.Contains(PairKey(pair.ContainerId, pair.BeneficiaryId)))
            {
                addedRecipients++;
                changedContainers.Add(pair.ContainerId);
            }
        }

        return new UnsignedWillChangesResponse(removedContainers, addedContainers, removedAssets, addedAssets,
            removedRecipients, addedRecipients, changedContainers.Count);
    }

    /// <summary>true khi có ít nhất một thay đổi chưa ký.</summary>
    public static bool HasChanges(UnsignedWillChangesResponse changes)
    {
        if (changes.RemovedContainers > 0 || changes.AddedContainers > 0
            || changes.RemovedAssets > 0 || changes.AddedAssets > 0
            || changes.RemovedRecipients > 0 || changes.AddedRecipients > 0
            || changes.ChangedRecipientCount > 0)
        {
            return true;
        }
        return false;
    }

    /// <summary>Số liệu toàn 0: chưa có thay đổi nào chưa ký.</summary>
    public static UnsignedWillChangesResponse NoChanges()
    {
        return new UnsignedWillChangesResponse(0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Chỉ định được bàn giao: cặp (gói, người nhận) có trong bản ký. Chỉ định hiện tại mà bản ký không có (thêm/đổi sau khi ký)
    /// bị bỏ qua; cặp trong bản ký mà đã xóa thì vốn không còn trong danh sách hiện tại.
    /// </summary>
    public static List<Designation> FilterDesignations(WillSnapshot signed, List<Designation> currentDesignations)
    {
        HashSet<string> signedKeys = new HashSet<string>();
        foreach (WillRecipientPair pair in signed.Recipients)
        {
            signedKeys.Add(PairKey(pair.ContainerId, pair.BeneficiaryId));
        }
        List<Designation> result = new List<Designation>();
        foreach (Designation designation in currentDesignations)
        {
            if (signedKeys.Contains(PairKey(designation.ContainerId, designation.BeneficiaryId)))
            {
                result.Add(designation);
            }
        }
        return result;
    }

    /// <summary>Id tài sản của một gói có trong bản ký; tài sản thêm vào gói sau khi ký không nằm trong tập này.</summary>
    public static HashSet<Guid> SignedAssetIds(WillSnapshot signed, Guid containerId)
    {
        HashSet<Guid> ids = new HashSet<Guid>();
        foreach (WillAssetPair pair in signed.Assets)
        {
            if (pair.ContainerId == containerId)
            {
                ids.Add(pair.AssetId);
            }
        }
        return ids;
    }

    private static HashSet<Guid> ContainerIds(WillSnapshot snapshot)
    {
        HashSet<Guid> ids = new HashSet<Guid>();
        foreach (WillRecipientPair pair in snapshot.Recipients)
        {
            ids.Add(pair.ContainerId);
        }
        foreach (WillAssetPair pair in snapshot.Assets)
        {
            ids.Add(pair.ContainerId);
        }
        return ids;
    }

    private static string PairKey(Guid first, Guid second)
    {
        return first.ToString("N") + ":" + second.ToString("N");
    }
}

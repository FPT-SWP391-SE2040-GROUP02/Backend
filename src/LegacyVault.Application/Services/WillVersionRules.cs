using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>Một cặp tài sản - người nhận trong bản chụp di chúc (chỉ có id).</summary>
public sealed class WillAssignmentPair
{
    public Guid AssetId { get; set; }

    public Guid BeneficiaryId { get; set; }
}

/// <summary>
/// Bản chụp cấu hình bàn giao tại lúc Chủ kho ký. Chỉ chứa id, không có tên tài sản hay người nhận.
/// Tài sản gán cho nhiều người thì có nhiều cặp, mỗi người một cặp.
/// </summary>
public sealed class WillSnapshot
{
    public List<WillAssignmentPair> Assignments { get; set; } = new List<WillAssignmentPair>();
}

/// <summary>
/// Luật thuần của di chúc số: dựng bản chụp, so sánh với cấu hình hiện tại.
/// Quy tắc: bàn giao chỉ theo bản ký gần nhất.
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

    /// <summary>Dựng bản chụp từ các dòng gán hiện tại của kho.</summary>
    public static WillSnapshot BuildSnapshot(List<AssetAssignment> assignments)
    {
        WillSnapshot snapshot = new WillSnapshot();
        foreach (AssetAssignment assignment in assignments)
        {
            WillAssignmentPair pair = new WillAssignmentPair();
            pair.AssetId = assignment.AssetId;
            pair.BeneficiaryId = assignment.BeneficiaryId;
            snapshot.Assignments.Add(pair);
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
    /// So bản ký với cấu hình hiện tại: đếm cặp tài sản - người nhận mới thêm, cặp đã gỡ và số người nhận bị ảnh hưởng.
    /// </summary>
    public static UnsignedWillChangesResponse Compare(WillSnapshot signed, WillSnapshot current)
    {
        HashSet<string> signedKeys = new HashSet<string>();
        foreach (WillAssignmentPair pair in signed.Assignments)
        {
            signedKeys.Add(PairKey(pair.AssetId, pair.BeneficiaryId));
        }
        HashSet<string> currentKeys = new HashSet<string>();
        foreach (WillAssignmentPair pair in current.Assignments)
        {
            currentKeys.Add(PairKey(pair.AssetId, pair.BeneficiaryId));
        }

        HashSet<Guid> affectedRecipients = new HashSet<Guid>();
        int removed = 0;
        foreach (WillAssignmentPair pair in signed.Assignments)
        {
            if (!currentKeys.Contains(PairKey(pair.AssetId, pair.BeneficiaryId)))
            {
                removed++;
                affectedRecipients.Add(pair.BeneficiaryId);
            }
        }
        int added = 0;
        foreach (WillAssignmentPair pair in current.Assignments)
        {
            if (!signedKeys.Contains(PairKey(pair.AssetId, pair.BeneficiaryId)))
            {
                added++;
                affectedRecipients.Add(pair.BeneficiaryId);
            }
        }

        return new UnsignedWillChangesResponse(added, removed, affectedRecipients.Count);
    }

    /// <summary>true khi có ít nhất một thay đổi chưa ký.</summary>
    public static bool HasChanges(UnsignedWillChangesResponse changes)
    {
        if (changes.AddedAssignments > 0 || changes.RemovedAssignments > 0)
        {
            return true;
        }
        return false;
    }

    /// <summary>Số liệu toàn 0: chưa có thay đổi nào chưa ký.</summary>
    public static UnsignedWillChangesResponse NoChanges()
    {
        return new UnsignedWillChangesResponse(0, 0, 0);
    }

    private static string PairKey(Guid first, Guid second)
    {
        return first.ToString("N") + ":" + second.ToString("N");
    }
}

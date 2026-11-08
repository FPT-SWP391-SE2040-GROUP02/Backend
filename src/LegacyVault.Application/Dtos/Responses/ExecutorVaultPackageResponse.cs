namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một gói bàn giao của kho nhìn từ Người thực thi (GET /api/v1/executor/vaults/{vaultId}/assets):
/// chỉ tên gói, người nhận và danh sách tài sản. Không có nội dung tài sản, mô tả hay tên tệp gốc.
/// </summary>
public class ExecutorVaultPackageResponse
{
    public Guid ContainerId { get; set; }

    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Người nhận được chỉ định cho gói, sắp theo thời điểm chỉ định.</summary>
    public List<ExecutorPackageRecipientResponse> Recipients { get; set; } = new List<ExecutorPackageRecipientResponse>();

    public List<ExecutorPackageAssetResponse> Assets { get; set; } = new List<ExecutorPackageAssetResponse>();
}

public class ExecutorPackageRecipientResponse
{
    /// <summary>Họ tên người nhận; null khi không giải mã được (thiếu KEK, dữ liệu hỏng).</summary>
    public string? FullName { get; set; }

    /// <summary>4 số cuối CCCD để phân biệt người nhận trùng tên.</summary>
    public string CitizenIdLast4 { get; set; } = string.Empty;
}

public class ExecutorPackageAssetResponse
{
    public Guid Id { get; set; }

    /// <summary>Tên do Chủ kho đặt.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>FILE hoặc TEXT.</summary>
    public string Kind { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}

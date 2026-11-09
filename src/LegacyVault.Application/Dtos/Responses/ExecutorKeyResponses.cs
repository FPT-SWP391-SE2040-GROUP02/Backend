namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái mảnh khóa Người thực thi của kho. Không bao giờ chứa mảnh hay hash.</summary>
public class ExecutorKeyStatusResponse
{
    /// <summary>Đã cấp mảnh chưa.</summary>
    public bool HasKey { get; set; }

    /// <summary>Phiên bản hiện hành (bắt đầu từ 1); 0 khi chưa cấp.</summary>
    public int Version { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }

    /// <summary>Lần cấp lại gần nhất; null khi chưa cấp lại.</summary>
    public DateTimeOffset? ReissuedAt { get; set; }

    /// <summary>false khi kế hoạch đã khóa (có báo tin) nên không cấp mới hay cấp lại được.</summary>
    public bool CanIssue { get; set; }
}

/// <summary>Kết quả cấp mảnh: mảnh gốc chỉ có trong response này, không đọc lại được.</summary>
public class ExecutorKeyIssuedResponse : ExecutorKeyStatusResponse
{
    /// <summary>Mảnh dạng "XXXX-XXXX-..."; Chủ kho đưa trực tiếp cho Người thực thi.</summary>
    public string Shard { get; set; } = string.Empty;
}

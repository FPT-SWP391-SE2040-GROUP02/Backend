namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một người nhận của kho nhìn từ Người thực thi (GET /api/v1/executor/vaults/{vaultId}/assets):
/// họ tên, số CCCD và các tài sản được gán cho người này. Không có nội dung tài sản, mô tả hay tên tệp gốc.
/// </summary>
public class ExecutorVaultRecipientResponse
{
    public Guid BeneficiaryId { get; set; }

    /// <summary>Họ tên người nhận; null khi không giải mã được (thiếu KEK, dữ liệu hỏng).</summary>
    public string? FullName { get; set; }

    /// <summary>4 số cuối căn cước ("•••• 1793") để phân biệt người nhận trùng tên; Người thực thi không nhận số đầy đủ.</summary>
    public string CitizenId { get; set; } = string.Empty;

    /// <summary>Các tài sản được gán cho người này, theo thứ tự gán; bỏ tài sản đã xóa nội dung.</summary>
    public List<ExecutorRecipientAssetResponse> Assets { get; set; } = new List<ExecutorRecipientAssetResponse>();
}

public class ExecutorRecipientAssetResponse
{
    public Guid Id { get; set; }

    /// <summary>Tên do Chủ tài sản đặt.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>FILE hoặc TEXT.</summary>
    public string Kind { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Số người nhận tài sản này, tính cả người đang xem; lớn hơn 1 nghĩa là nhận chung.</summary>
    public int SharedWithCount { get; set; }
}

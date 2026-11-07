namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả Executor điểm danh thay chủ kho (POST /api/v1/executor/vaults/{vaultId}/check-in).</summary>
public class ExecutorCheckInResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Trạng thái kế hoạch sau khi điểm danh hộ, luôn là ACTIVE khi thành công.</summary>
    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Thời điểm Executor điểm danh hộ (giờ server).</summary>
    public DateTimeOffset CheckedInAt { get; set; }

    /// <summary>Hạn điểm danh của kỳ mới vừa mở.</summary>
    public DateTimeOffset NextCheckInDueAt { get; set; }

    /// <summary>Mốc báo Người thực thi của kỳ mới vừa mở.</summary>
    public DateTimeOffset GraceEndsAt { get; set; }

    /// <summary>Số lần điểm danh thay liên tiếp, đã tính lần vừa bấm.</summary>
    public int ProxyUsed { get; set; }

    /// <summary>Số lần điểm danh thay liên tiếp tối đa do Admin đặt.</summary>
    public int ProxyLimit { get; set; }
}

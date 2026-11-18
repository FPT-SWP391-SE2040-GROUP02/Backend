namespace LegacyVault.Application.Services;

/// <summary>Việc nền lưu và xóa dữ liệu sau bàn giao.</summary>
public interface IRetentionJob
{
    /// <summary>Id các kế hoạch cần xét lưu và xóa trong lần quét này (mỗi lần tối đa một lô).</summary>
    Task<List<Guid>> FindPlanIdsAsync(CancellationToken ct);

    /// <summary>Xử lý một kế hoạch; đọc lại và kiểm lại mọi điều kiện nên chạy lặp an toàn.</summary>
    Task<RetentionRunResult> ProcessPlanAsync(Guid planId, CancellationToken ct);
}

namespace LegacyVault.Domain.Enums;

/// <summary>
/// Vai trò của Executor trong kho.
/// </summary>
/// <remarks>
/// Mỗi kho có một Executor chính và có thể thêm Executor dự phòng.
/// Nếu Executor chính cũng là người nhận thì phải có Executor dự phòng đã chấp nhận
/// (và người này không phải người nhận) mới kích hoạt được kế hoạch.
/// </remarks>
public enum ExecutorRole
{
    /// <summary>Executor chính của cả kho.</summary>
    Primary = 0,
    /// <summary>Executor dự phòng, thay Executor chính khi cần.</summary>
    Backup = 1,
}

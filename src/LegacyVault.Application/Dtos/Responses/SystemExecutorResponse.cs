namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// DTO đại diện cho Người thực thi hệ thống trong danh bạ để Chủ tài sản lựa chọn.
/// </summary>
public sealed class SystemExecutorResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PhoneMasked { get; set; }
    public string ExecutorRoleTitle { get; set; } = string.Empty;
    public int AssignedVaultsCount { get; set; }
    public bool IsAvailable { get; set; } = true;
}

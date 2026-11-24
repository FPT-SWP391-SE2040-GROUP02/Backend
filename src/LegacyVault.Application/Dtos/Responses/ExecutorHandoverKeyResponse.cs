namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Mã nhận bàn giao được cấp và gửi trực tiếp cho Người thực thi trên hệ thống.
/// </summary>
public sealed class ExecutorHandoverKeyResponse
{
    public Guid VaultId { get; set; }
    public string OwnerFullName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Shard { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ReissuedAt { get; set; }
    public string Status { get; set; } = "ACTIVE_ON_SYSTEM";
}

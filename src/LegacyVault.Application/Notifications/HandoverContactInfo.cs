namespace LegacyVault.Application.Notifications;

/// <summary>Thông tin liên hệ của một kho dùng trong thư mời nhận tài sản.</summary>
public sealed class HandoverContactInfo
{
    public string OwnerFullName { get; set; } = string.Empty;

    public string ExecutorFullName { get; set; } = string.Empty;

    public string? ExecutorPhone { get; set; }

    public string ExecutorEmail { get; set; } = string.Empty;

    public string CaseCode { get; set; } = string.Empty;

    public string VerifierFullName { get; set; } = string.Empty;

    public string? VerifierLawyerCardNumber { get; set; }
}

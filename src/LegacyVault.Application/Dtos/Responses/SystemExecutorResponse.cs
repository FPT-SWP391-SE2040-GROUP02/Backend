namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// DTO đại diện cho Người thực thi hệ thống trong danh bạ để Chủ kho lựa chọn.
/// </summary>
public sealed class SystemExecutorResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PhoneMasked { get; set; }
    public string ExecutorRoleTitle { get; set; } = string.Empty;
    public SystemExecutorLegalVerification LegalVerification { get; set; } = new();
    public int AssignedVaultsCount { get; set; }
    public bool IsAvailable { get; set; } = true;
}

/// <summary>
/// Thông tin xác minh pháp lý của Người thực thi hệ thống.
/// </summary>
public sealed class SystemExecutorLegalVerification
{
    public string Status { get; set; } = "VERIFIED";
    public string? LawyerCardNumber { get; set; }
    public string? PracticeCertificateNumber { get; set; }
    public string? Organization { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}

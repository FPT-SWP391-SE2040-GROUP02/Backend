using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Thông tin người dùng đối chiếu sau OCR; không thay thế bằng chứng từ ảnh.</summary>
public sealed class EkycDocumentConfirmationRequest
{
    public Guid VerificationId { get; set; }

    [Required]
    public string Purpose { get; set; } = string.Empty;

    public Guid? HandoverTurnId { get; set; }

    [Required, RegularExpression("^[0-9]{12}$")]
    public string IdNumber { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public DateOnly? DateOfBirth { get; set; }

    [Required, MaxLength(1000)]
    public string ResidenceAddress { get; set; } = string.Empty;
}

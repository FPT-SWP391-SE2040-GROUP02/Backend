using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/executor/vaults/{vaultId}/check-in: Executor khai đã liên lạc với ai, lúc nào, cách nào
/// trước khi điểm danh thay chủ kho.
/// </summary>
public class ExecutorCheckInRequest
{
    /// <summary>Cách liên lạc hợp lệ.</summary>
    public const string MethodPhone = "PHONE";
    public const string MethodInPerson = "IN_PERSON";
    public const string MethodMessage = "MESSAGE";
    public const string MethodEmail = "EMAIL";

    /// <summary>Người đã liên lạc, ví dụ "chính chủ kho" hoặc "chị Lan (vợ)".</summary>
    [Required(ErrorMessage = "Hãy ghi đã liên lạc với ai.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên người liên lạc dài từ 2 đến 200 ký tự.")]
    public string ContactedWho { get; set; } = string.Empty;

    /// <summary>Lúc đã liên lạc; không được ở tương lai.</summary>
    [Required(ErrorMessage = "Hãy ghi liên lạc lúc nào.")]
    public DateTimeOffset? ContactedAt { get; set; }

    /// <summary>PHONE, IN_PERSON, MESSAGE hoặc EMAIL.</summary>
    [Required(ErrorMessage = "Hãy chọn cách liên lạc.")]
    [RegularExpression("^(PHONE|IN_PERSON|MESSAGE|EMAIL)$", ErrorMessage = "Cách liên lạc không hợp lệ.")]
    public string ContactMethod { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Api.Models;

/// <summary>
/// Form multipart của POST /api/v1/executor/death-cases/{caseId}/files. Tên field: <c>file</c>.
/// </summary>
/// <remarks>Nằm ở Api vì IFormFile là kiểu của ASP.NET; controller chuyển sang AddDeathCaseFileRequest.</remarks>
public sealed class DeathCaseFileForm
{
    [Required(ErrorMessage = "Vui lòng chọn tệp.")]
    public IFormFile? File { get; set; }
}

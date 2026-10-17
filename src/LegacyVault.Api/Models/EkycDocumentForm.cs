using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Api.Models;

/// <summary>
/// Form multipart gửi mặt trước giấy tờ: ảnh (field <c>file</c>) và lượt bàn giao cần xác minh (field <c>handoverTurnId</c>).
/// </summary>
/// <remarks>handoverTurnId chỉ bắt buộc khi mục đích đã đồng ý là BENEFICIARY_CLAIM; service kiểm tra theo mục đích.</remarks>
public sealed class EkycDocumentForm
{
    [Required(ErrorMessage = "Vui lòng chọn ảnh.")]
    public IFormFile? File { get; set; }

    public Guid? HandoverTurnId { get; set; }
}

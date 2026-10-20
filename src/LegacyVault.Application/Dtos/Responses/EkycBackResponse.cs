namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Kết quả gửi mặt sau giấy tờ: trạng thái lượt eKYC như <see cref="EkycStatusResponse"/> kèm nơi cư trú đọc từ mặt
/// sau để người dùng đối chiếu rồi điền vào bản khai (BR-88).
/// </summary>
public class EkycBackResponse : EkycStatusResponse
{
    /// <summary>
    /// Nơi cư trú đọc được từ mặt sau; chuỗi rỗng khi không đọc được (người dùng tự nhập). Chỉ có trong phản hồi này,
    /// không lưu ở DB nên GET /ekyc/me không bao giờ trả lại.
    /// </summary>
    public string ResidenceAddress { get; set; } = string.Empty;
}

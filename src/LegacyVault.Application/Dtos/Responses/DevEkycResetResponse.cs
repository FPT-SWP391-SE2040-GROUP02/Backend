namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả đặt lại eKYC của một tài khoản: chỉ số lượng đã xóa, không có dữ liệu định danh.</summary>
public class DevEkycResetResponse
{
    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>Số lượt eKYC đã xóa (mọi mục đích).</summary>
    public int VerificationsRemoved { get; set; }

    /// <summary>Số lần đồng ý chụp ảnh đã xóa.</summary>
    public int ConsentsRemoved { get; set; }

    /// <summary>Có xóa danh tính đã gắn của Owner hay không.</summary>
    public bool IdentityRemoved { get; set; }

    /// <summary>Số phiên bản mẫu khuôn mặt của Owner đã xóa.</summary>
    public int FaceTemplatesRemoved { get; set; }
}

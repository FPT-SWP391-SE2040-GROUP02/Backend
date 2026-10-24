namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả đưa kho thử về trước eKYC và mua gói: chỉ số lượng đã đổi, không có dữ liệu định danh.</summary>
public class DevRewindBeforeEkycResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Email Owner của kho, để Admin đối chiếu đúng tài khoản.</summary>
    public string OwnerEmail { get; set; } = string.Empty;

    /// <summary>Số lượt eKYC của Owner đã xóa (mọi mục đích).</summary>
    public int VerificationsRemoved { get; set; }

    /// <summary>Số lần đồng ý chụp ảnh của Owner đã xóa.</summary>
    public int ConsentsRemoved { get; set; }

    /// <summary>Có xóa danh tính đã gắn của Owner hay không.</summary>
    public bool IdentityRemoved { get; set; }

    /// <summary>Số phiên bản mẫu khuôn mặt của Owner đã xóa.</summary>
    public int FaceTemplatesRemoved { get; set; }

    /// <summary>Hạng dịch vụ của kho trước khi đưa về Free (FREE nếu vốn đã là Free).</summary>
    public string PreviousTier { get; set; } = string.Empty;

    /// <summary>Quyền dịch vụ có được trả về Free hay không (false khi kho vốn đã là Free).</summary>
    public bool EntitlementReset { get; set; }

    /// <summary>Số đơn thanh toán đang chờ đã hủy.</summary>
    public int PendingOrdersCancelled { get; set; }
}

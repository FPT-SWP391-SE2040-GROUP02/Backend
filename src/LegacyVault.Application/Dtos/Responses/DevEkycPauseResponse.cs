namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái tạm ngừng eKYC đang chạy (công cụ thử nghiệm, chỉ có ở Development/Staging).</summary>
public class DevEkycPauseResponse
{
    /// <summary>true khi eKYC đang tạm ngừng; chỉ ở bộ nhớ, khởi động lại máy chủ là về false.</summary>
    public bool Paused { get; set; }
}

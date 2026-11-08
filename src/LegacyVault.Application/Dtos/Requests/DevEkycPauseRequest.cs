namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin bật hoặc tắt chế độ tạm ngừng eKYC (công cụ thử nghiệm, chỉ có ở Development/Staging).</summary>
public class DevEkycPauseRequest
{
    /// <summary>true: mọi bước eKYC dùng dịch vụ giả và luôn đạt; false: dùng dịch vụ eKYC thật.</summary>
    public bool Paused { get; set; }
}

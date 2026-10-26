namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Trạng thái ngưỡng so khớp khuôn mặt eKYC đang chạy (công cụ thử nghiệm, chỉ có ở Development/Staging).</summary>
public class DevEkycThresholdResponse
{
    /// <summary>Ngưỡng đang áp dụng hiện tại (đã tính tới override runtime nếu có).</summary>
    public double CurrentThreshold { get; set; }

    /// <summary>Ngưỡng cấu hình gốc (mốc để đặt lại).</summary>
    public double DefaultThreshold { get; set; }

    /// <summary>Giới hạn dưới của dải an toàn.</summary>
    public double Min { get; set; }

    /// <summary>Giới hạn trên của dải an toàn.</summary>
    public double Max { get; set; }
}

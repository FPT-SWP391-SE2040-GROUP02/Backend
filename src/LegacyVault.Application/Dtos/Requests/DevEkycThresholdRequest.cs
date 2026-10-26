namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin đổi ngưỡng so khớp khuôn mặt eKYC (công cụ thử nghiệm, chỉ có ở Development/Staging).</summary>
public class DevEkycThresholdRequest
{
    /// <summary>Ngưỡng cosine trong dải an toàn [0.20; 0.60]; cao siết chặt hơn, thấp nới lỏng hơn.</summary>
    public double Threshold { get; set; }
}

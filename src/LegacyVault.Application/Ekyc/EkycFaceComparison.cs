namespace LegacyVault.Application.Ekyc;

/// <summary>Kết quả so khớp selfie với khuôn mặt trên giấy tờ.</summary>
public sealed class EkycFaceComparison
{
    /// <summary>Điểm cosine; không phải phần trăm xác suất.</summary>
    public double Score { get; set; }

    public bool Matched { get; set; }
}

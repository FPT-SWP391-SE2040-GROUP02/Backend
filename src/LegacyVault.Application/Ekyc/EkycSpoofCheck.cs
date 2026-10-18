namespace LegacyVault.Application.Ekyc;

/// <summary>Kết quả kiểm tra giả mạo trên một ảnh selfie đơn.</summary>
public sealed class EkycSpoofCheck
{
    public bool Passed { get; set; }

    public double LiveScore { get; set; }
}

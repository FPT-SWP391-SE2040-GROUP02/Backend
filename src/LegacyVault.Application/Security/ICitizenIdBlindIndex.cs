namespace LegacyVault.Application.Security;

/// <summary>
/// Chỉ mục mù cho số CCCD: HMAC-SHA256 với khóa riêng, để DB có unique index chống trùng mà không lưu số CCCD rõ.
/// Khóa nằm ở cấu hình riêng (Ekyc:CitizenIdIndexKey), KHÔNG dùng chung khóa mã hóa KEK.
/// </summary>
public interface ICitizenIdBlindIndex
{
    /// <summary>true khi đã cấu hình khóa. Chưa cấu hình thì không ghi/đối chiếu hash (vẫn còn kiểm trùng ở tầng ứng dụng).</summary>
    bool Enabled { get; }

    /// <summary>Hash hex 64 ký tự của số CCCD đã chuẩn hóa; null khi chưa bật.</summary>
    string? Compute(string citizenId);
}

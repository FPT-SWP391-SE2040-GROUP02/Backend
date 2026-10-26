namespace LegacyVault.Application.Ekyc;

/// <summary>
/// Ngưỡng so khớp khuôn mặt cho eKYC, đọc từ mục "Ekyc" trong appsettings (EkycOptions, mặc định
/// EkycRules.MatchThreshold). Application không tham chiếu IOptions: Infrastructure bind POCO và bridge
/// qua interface này (mẫu IHandoverPolicy / IBilling*).
/// </summary>
/// <remarks>
/// CurrentThreshold là giá trị đang chạy: ban đầu bằng DefaultThreshold (cấu hình), Admin có thể ghi đè
/// runtime qua DevTools (SetThreshold) rồi ResetDefault để về giá trị cấu hình. Ghi đè chỉ ở bộ nhớ,
/// không lưu DB; khởi động lại là về DefaultThreshold.
/// </remarks>
public interface IEkycOptions
{
    /// <summary>Ngưỡng cấu hình (appsettings, fallback EkycRules.MatchThreshold) — mốc để reset.</summary>
    double DefaultThreshold { get; }

    /// <summary>Ngưỡng đang áp dụng (đã tính tới override runtime nếu có).</summary>
    double CurrentThreshold { get; }

    /// <summary>Ghi đè ngưỡng đang chạy (chỉ DevTools; giới hạn an toàn do phía gọi kiểm).</summary>
    void SetThreshold(double threshold);

    /// <summary>Bỏ override, đưa CurrentThreshold về DefaultThreshold.</summary>
    void ResetDefault();
}

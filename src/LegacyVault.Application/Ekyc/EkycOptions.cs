using LegacyVault.Application.Services;

namespace LegacyVault.Application.Ekyc;

/// <summary>
/// Giá trị cấu hình mục "Ekyc" trong appsettings cho so khớp khuôn mặt, kèm override runtime cho DevTools.
/// Application không tham chiếu IOptions nên đây chỉ là POCO; Infrastructure (InfrastructureConfig) bind
/// POCO và đóng gói thành singleton <see cref="IEkycOptions"/> cho <c>EkycService</c> / <c>OwnerFaceVerifier</c>.
/// </summary>
public sealed class EkycOptions : IEkycOptions
{
    /// <summary>Trùng tên mục cấu hình với <c>Ekyc</c> (EkycSettings cũng bind cùng mục; thuộc tính không trùng).</summary>
    public const string SectionName = "Ekyc";

    /// <summary>Lock bảo vệ override runtime; ghi đè từ DevTools không được ảnh hưởng tới đọc của luồng eKYC.</summary>
    private readonly object _overrideLock = new();

    /// <summary>Override runtime (DevTools); null khi đang dùng giá trị cấu hình.</summary>
    private double? _runtimeOverride;

    /// <summary>Ngưỡng cấu hình (binder đọc từ <c>Ekyc:MatchThreshold</c>), default giá trị chuẩn của <see cref="EkycRules.MatchThreshold"/>.</summary>
    public double MatchThreshold { get; set; } = EkycRules.MatchThreshold;

    /// <inheritdoc/> Ngưỡng cấu hình được coi là mốc mặc định để reset.
    double IEkycOptions.DefaultThreshold => MatchThreshold;

    /// <inheritdoc/>
    public double CurrentThreshold
    {
        get
        {
            lock (_overrideLock)
            {
                return _runtimeOverride ?? MatchThreshold;
            }
        }
    }

    /// <inheritdoc/>
    public void SetThreshold(double threshold)
    {
        lock (_overrideLock)
        {
            _runtimeOverride = threshold;
        }
    }

    /// <inheritdoc/>
    private bool? _pausedOverride;

    /// <summary>
    /// Ekyc:PausedOnStartup: khởi động ở trạng thái tạm ngừng eKYC để chạy thử, khỏi bấm nút sau mỗi lần chạy lại API.
    /// Chỉ có tác dụng ở Development hoặc Staging (môi trường khác bị ép về false lúc cấu hình).
    /// </summary>
    public bool PausedOnStartup { get; set; }

    public bool Paused => _pausedOverride ?? PausedOnStartup;

    public void SetPaused(bool paused)
    {
        _pausedOverride = paused;
    }

    public void ResetDefault()
    {
        lock (_overrideLock)
        {
            _runtimeOverride = null;
        }
    }
}

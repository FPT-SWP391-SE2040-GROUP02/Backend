namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Lần đồng ý vừa ghi (POST /api/v1/ekyc/consent).</summary>
public class EkycConsentResponse
{
    public string Purpose { get; set; } = string.Empty;

    public string NoticeVersion { get; set; } = string.Empty;

    public DateTimeOffset ConsentedAt { get; set; }

    /// <summary>Hạn phải bắt đầu chụp mặt trước; quá hạn thì đồng ý lại.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

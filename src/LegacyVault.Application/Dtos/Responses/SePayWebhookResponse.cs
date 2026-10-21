namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Phản hồi webhook SePay: SePay chỉ coi là thành công khi nhận HTTP 200 và body đúng {"success": true}.</summary>
public sealed class SePayWebhookResponse
{
    public bool Success { get; set; }
}

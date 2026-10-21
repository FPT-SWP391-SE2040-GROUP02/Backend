using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Webhook SePay báo tiền vào tài khoản (SRS §8.3, §8.5). Không dùng JWT của người dùng: SePay xác thực bằng
/// header "Authorization: Apikey &lt;key&gt;" do service kiểm tra.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/billing/sepay/webhook")]
[Produces("application/json")]
public sealed class SePayWebhookController : ControllerBase
{
    private readonly ISePayWebhookService webhookService;

    public SePayWebhookController(ISePayWebhookService webhookService)
    {
        this.webhookService = webhookService;
    }

    /// <summary>
    /// Nhận giao dịch. Chỉ trả 200 {"success": true} sau khi giao dịch đã được lưu; mọi lỗi (401, 400, 500) đều không trả
    /// success nên SePay sẽ gửi lại.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<SePayWebhookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<SePayWebhookResponse> Receive(
        [FromHeader(Name = "Authorization")] string? authorization,
        SePayWebhookRequest request,
        CancellationToken ct)
    {
        await webhookService.HandleAsync(authorization, request, ct);

        SePayWebhookResponse response = new SePayWebhookResponse();
        response.Success = true;
        return response;
    }
}

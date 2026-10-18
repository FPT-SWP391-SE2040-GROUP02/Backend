using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Thanh toán dịch vụ của kho người đang đăng nhập (SRS §8, docs/api-contract.md).</summary>
[ApiController]
[Authorize]
[Route("api/v1/billing")]
[Produces("application/json")]
public sealed class BillingController : ControllerBase
{
    private readonly IBillingService billingService;

    public BillingController(IBillingService billingService)
    {
        this.billingService = billingService;
    }

    /// <summary>Bảng giá hiện hành (hạng, năm, suất thêm).</summary>
    [HttpGet("plans")]
    [ProducesResponseType<BillingPlansResponse>(StatusCodes.Status200OK)]
    public Task<BillingPlansResponse> GetPlans(CancellationToken ct)
    {
        return billingService.GetPlansAsync(ct);
    }

    /// <summary>Báo giá, không tạo đơn. Backend tự tính giá và tự xác định loại đơn.</summary>
    [HttpPost("quote")]
    [ProducesResponseType<BillingQuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<BillingQuoteResponse> Quote(BillingSelectionRequest request, CancellationToken ct)
    {
        return billingService.QuoteAsync(request, ct);
    }
}

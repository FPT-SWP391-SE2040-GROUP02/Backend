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

    /// <summary>
    /// Tạo đơn thanh toán: 201 khi tạo mới, 200 khi đã có đơn chờ cùng lựa chọn (trả lại đơn cũ).
    /// Khác lựa chọn → 409 PAYMENT_ORDER_PENDING.
    /// </summary>
    [HttpPost("orders")]
    [ProducesResponseType<PaymentOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentOrderResponse>> CreateOrder(BillingSelectionRequest request, CancellationToken ct)
    {
        CreatePaymentOrderResult result = await billingService.CreateOrderAsync(request, ct);
        if (result.Created)
        {
            return CreatedAtAction(nameof(GetOrder), new { orderId = result.Order.Id }, result.Order);
        }
        return Ok(result.Order);
    }

    /// <summary>Đơn đang chờ thanh toán của kho mình. Không có → 204.</summary>
    [HttpGet("orders/current")]
    [ProducesResponseType<PaymentOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentOrderResponse>> GetCurrentOrder(CancellationToken ct)
    {
        PaymentOrderResponse? order = await billingService.GetCurrentOrderAsync(ct);
        if (order == null)
        {
            return NoContent();
        }
        return Ok(order);
    }

    [HttpGet("orders/{orderId:guid}")]
    [ProducesResponseType<PaymentOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaymentOrderResponse> GetOrder(Guid orderId, CancellationToken ct)
    {
        return billingService.GetOrderAsync(orderId, ct);
    }

    /// <summary>Hủy đơn đang chờ. Hủy lặp vẫn 200; đơn đã trả hoặc hết hạn → 409 PAYMENT_ORDER_NOT_PAYABLE.</summary>
    [HttpPost("orders/{orderId:guid}/cancel")]
    [ProducesResponseType<PaymentOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<PaymentOrderResponse> CancelOrder(Guid orderId, CancellationToken ct)
    {
        return billingService.CancelOrderAsync(orderId, ct);
    }
}

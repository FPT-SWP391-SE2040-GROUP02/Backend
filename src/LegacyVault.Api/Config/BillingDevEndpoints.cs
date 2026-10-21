using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;

namespace LegacyVault.Api.Config;

/// <summary>
/// Endpoint giả lập trả tiền cho môi trường Development (D9). Công tắc tắt thì route KHÔNG được đăng ký,
/// nên ở môi trường thật đường dẫn này là 404. Không có body: số tiền và trạng thái không bao giờ do client gửi.
/// </summary>
public static class BillingDevEndpoints
{
    public static void MapBillingDevEndpoints(this WebApplication app)
    {
        IBillingDevSettings settings = app.Services.GetRequiredService<IBillingDevSettings>();
        if (!settings.DevPaymentEnabled)
        {
            return;
        }

        app.MapPost("/api/v1/billing/orders/{orderId:guid}/dev-pay", PayAsync)
            .RequireAuthorization()
            .WithTags("Billing (chỉ Development)")
            .Produces<PaymentOrderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> PayAsync(Guid orderId, IBillingService billingService, CancellationToken ct)
    {
        PaymentOrderResponse order = await billingService.DevPayAsync(orderId, ct);
        return Results.Ok(order);
    }
}

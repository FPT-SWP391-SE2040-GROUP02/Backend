namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả tạo đơn: Created để controller chọn 201 (đơn mới) hay 200 (trả lại đơn cũ cùng lựa chọn).</summary>
public class CreatePaymentOrderResult
{
    public bool Created { get; set; }

    public PaymentOrderResponse Order { get; set; } = new PaymentOrderResponse();
}

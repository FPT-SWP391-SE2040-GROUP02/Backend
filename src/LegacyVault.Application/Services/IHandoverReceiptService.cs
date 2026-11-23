namespace LegacyVault.Application.Services;

/// <summary>Phiếu giao nhận: Người thực thi ký bên giao sau khi người nhận đã xác nhận nhận đủ.</summary>
public interface IHandoverReceiptService
{
    /// <summary>Lỗi: 404 khi không phải Người thực thi của kho; 409 NOT_RECEIVED khi người nhận chưa xác nhận.</summary>
    Task SignAsExecutorAsync(Guid recordId, Guid turnId, CancellationToken ct);
}

using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Màn đối chiếu thanh toán của Admin (SRS ADMIN-09, PAY-07, docs/api-contract.md mục 6.18). Chỉ đọc: không có API nào
/// ghi trạng thái PAID hay sửa giao dịch. Quyền kiểm từ DB trong service: không phải Admin đang hoạt động thì 404 (SEC-04).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/payment-transactions")]
// Response chứa dữ liệu thanh toán nên cấm trình duyệt và proxy lưu đệm.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPaymentsController : ControllerBase
{
    private readonly IAdminPaymentService adminPaymentService;

    public AdminPaymentsController(IAdminPaymentService adminPaymentService)
    {
        this.adminPaymentService = adminPaymentService;
    }

    /// <summary>
    /// Giao dịch tiền vào, mới nhất trước. status: SUCCESS (Matched và Resolved), RECEIVED, MATCHED, RECONCILIATION_REQUIRED,
    /// RESOLVED. environment: Live hoặc Test. from, to: ngày yyyy-MM-dd giờ Việt Nam, tính cả hai đầu, lọc theo thời điểm nhận.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<AdminPaymentTransactionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<AdminPaymentTransactionResponse>> List(
        [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? environment = null, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        return adminPaymentService.ListAsync(
            status, search, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct, environment, from, to);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }
}

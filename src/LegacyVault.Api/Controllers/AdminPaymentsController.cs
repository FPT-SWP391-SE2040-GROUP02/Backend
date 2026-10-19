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

    /// <summary>Giao dịch tiền vào, mới nhất trước. status: RECEIVED, MATCHED, RECONCILIATION_REQUIRED, RESOLVED.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<AdminPaymentTransactionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<AdminPaymentTransactionResponse>> List(
        [FromQuery] string? status, CancellationToken ct,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return adminPaymentService.ListAsync(status, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct);
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

using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Khu vực quản trị (docs/api-contract.md mục 6.15). Quyền kiểm từ DB trong service: không phải Admin đang hoạt động
/// thì 404 như khu vực không tồn tại (SEC-04). Không có endpoint nào đọc nội dung tài sản, trang giấy hay danh tính.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin")]
// Response chứa danh sách tài khoản và dữ liệu vận hành nên cấm trình duyệt và proxy lưu đệm.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService adminService;
    private readonly IIncidentService incidentService;
    private readonly IHoldService holdService;

    public AdminController(IAdminService adminService, IIncidentService incidentService, IHoldService holdService)
    {
        this.adminService = adminService;
        this.incidentService = incidentService;
        this.holdService = holdService;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType<AdminDashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminDashboardResponse> GetDashboard(CancellationToken ct)
    {
        return adminService.GetDashboardAsync(ct);
    }

    // ---- Tài khoản ----

    [HttpGet("users")]
    [ProducesResponseType<PaginatedList<AdminUserResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PaginatedList<AdminUserResponse>> ListUsers(
        [FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status,
        CancellationToken ct, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return adminService.ListUsersAsync(search, role, status, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct);
    }

    [HttpPut("users/{userId:guid}/role")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminUserResponse> ChangeRole(Guid userId, ChangeUserRoleRequest request, CancellationToken ct)
    {
        return adminService.ChangeRoleAsync(userId, request, ct);
    }

    [HttpPut("users/{userId:guid}/status")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminUserResponse> ChangeStatus(Guid userId, ChangeUserStatusRequest request, CancellationToken ct)
    {
        return adminService.ChangeStatusAsync(userId, request, ct);
    }

    // ---- Phân công Verifier ----

    [HttpPost("death-cases/{caseId:guid}/reassign-verifier")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReassignVerifier(Guid caseId, ReassignVerifierRequest request, CancellationToken ct)
    {
        await adminService.ReassignVerifierAsync(caseId, request, ct);
        return NoContent();
    }

    // ---- Sự cố ----

    [HttpGet("incidents")]
    [ProducesResponseType<PaginatedList<IncidentSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<PaginatedList<IncidentSummaryResponse>> ListIncidents(
        [FromQuery] string? status, [FromQuery] string? type, [FromQuery] Guid? assigneeUserId,
        CancellationToken ct, [FromQuery] bool overdueOnly = false,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return incidentService.ListAsync(
            status, type, assigneeUserId, overdueOnly, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct);
    }

    [HttpGet("incidents/{incidentId:guid}")]
    [ProducesResponseType<IncidentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IncidentDetailResponse> GetIncident(Guid incidentId, CancellationToken ct)
    {
        return incidentService.GetAsync(incidentId, ct);
    }

    [HttpPost("incidents/{incidentId:guid}/assign")]
    [ProducesResponseType<IncidentDetailResponse>(StatusCodes.Status200OK)]
    public Task<IncidentDetailResponse> AssignIncident(Guid incidentId, AssignIncidentRequest request, CancellationToken ct)
    {
        return incidentService.AssignAsync(incidentId, request.AssigneeUserId!.Value, request.Reason, request.Etag, ct);
    }

    [HttpPost("incidents/{incidentId:guid}/notes")]
    [ProducesResponseType<IncidentDetailResponse>(StatusCodes.Status200OK)]
    public Task<IncidentDetailResponse> AddIncidentNote(Guid incidentId, IncidentNoteRequest request, CancellationToken ct)
    {
        return incidentService.AddNoteAsync(incidentId, request.Note, ct);
    }

    [HttpPost("incidents/{incidentId:guid}/review")]
    [ProducesResponseType<IncidentDetailResponse>(StatusCodes.Status200OK)]
    public Task<IncidentDetailResponse> ReviewIncident(Guid incidentId, ReviewIncidentRequest request, CancellationToken ct)
    {
        return incidentService.ReviewAsync(incidentId, request.NextReviewAt!.Value, request.Note, request.Etag, ct);
    }

    [HttpPost("incidents/{incidentId:guid}/resolve")]
    [ProducesResponseType<IncidentDetailResponse>(StatusCodes.Status200OK)]
    public Task<IncidentDetailResponse> ResolveIncident(Guid incidentId, ResolveIncidentRequest request, CancellationToken ct)
    {
        return incidentService.ResolveAsync(incidentId, request.Note, request.Etag, ct);
    }

    // ---- Hold ----

    [HttpGet("holds")]
    [ProducesResponseType<PaginatedList<HoldResponse>>(StatusCodes.Status200OK)]
    public Task<PaginatedList<HoldResponse>> ListHolds(
        [FromQuery] string? status, [FromQuery] Guid? vaultId, CancellationToken ct,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return holdService.ListAsync(status, vaultId, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct);
    }

    [HttpGet("holds/{holdId:guid}")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    public Task<HoldResponse> GetHold(Guid holdId, CancellationToken ct)
    {
        return holdService.GetAsync(holdId, ct);
    }

    [HttpPost("holds")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceHold(PlaceHoldRequest request, CancellationToken ct)
    {
        HoldResponse hold = await holdService.PlaceAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, hold);
    }

    [HttpPost("holds/{holdId:guid}/review")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    public Task<HoldResponse> ReviewHold(Guid holdId, ReviewHoldRequest request, CancellationToken ct)
    {
        return holdService.ReviewAsync(holdId, request, ct);
    }

    [HttpPost("holds/{holdId:guid}/release")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    public Task<HoldResponse> ReleaseHold(Guid holdId, ReleaseHoldRequest request, CancellationToken ct)
    {
        return holdService.ReleaseAsync(holdId, request, ct);
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

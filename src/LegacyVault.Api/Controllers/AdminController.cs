using LegacyVault.Api.Handover;
using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos;
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
    private readonly IPlanSetupService planSetupService;
    private readonly INotaryCardService notaryCardService;

    public AdminController(IAdminService adminService, IIncidentService incidentService, IHoldService holdService,
        IPlanSetupService planSetupService, INotaryCardService notaryCardService)
    {
        this.planSetupService = planSetupService;
        this.notaryCardService = notaryCardService;
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

    [HttpPost("users")]
    [ProducesResponseType<AdminUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserResponse>> CreateUser(
        CreateAdminUserRequest request, CancellationToken ct)
    {
        AdminUserResponse response = await adminService.CreateUserAsync(request, ct);
        return Created($"/api/v1/admin/users/{response.Id}", response);
    }

    /// <summary>Tải ảnh thẻ công chứng viên của một Người xét duyệt (multipart/form-data: file). JPEG, PNG hoặc WebP, tối đa 2 MiB.</summary>
    [HttpPut("users/{userId:guid}/notary-card")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AvatarRules.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarRules.MaxBytes + 64 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadNotaryCard(Guid userId, [FromForm] AvatarForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using Stream content = file.OpenReadStream();
        await notaryCardService.UploadAsync(userId, content, file.Length, ct);
        return NoContent();
    }

    /// <summary>Xem ảnh thẻ công chứng viên của một Người xét duyệt (nội dung ảnh, không phải JSON). 404 khi chưa có ảnh.</summary>
    [HttpGet("users/{userId:guid}/notary-card")]
    [Produces("image/jpeg", "image/png", "image/webp", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotaryCard(Guid userId, CancellationToken ct)
    {
        PreparedDownload download = await notaryCardService.PrepareForAdminAsync(userId, ct);
        return new AvatarFileResult(download);
    }

    [HttpGet("users")]
    [ProducesResponseType<PaginatedList<AdminUserResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PaginatedList<AdminUserResponse>> ListUsers(
        [FromQuery] string? search, [FromQuery] string? role, [FromQuery] string? status,
        CancellationToken ct, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return adminService.ListUsersAsync(search, role, status, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct);
    }

    /// <summary>Số tài khoản theo vai trò cho các thẻ lọc; nhận cùng bộ lọc tìm kiếm và trạng thái với danh sách.</summary>
    [HttpGet("users/counts")]
    [ProducesResponseType<CountsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<CountsResponse> CountUsers([FromQuery] string? search, [FromQuery] string? status, CancellationToken ct)
    {
        return adminService.CountUsersByRoleAsync(search, status, ct);
    }

    [HttpGet("users/{userId:guid}")]
    [ProducesResponseType<AdminUserDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminUserDetailResponse> GetUser(Guid userId, CancellationToken ct)
    {
        return adminService.GetUserDetailAsync(userId, ct);
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

    // ---- Nhật ký ----

    /// <summary>
    /// Nhật ký toàn hệ thống, chỉ đọc (ADMIN-06). sortBy: at (mặc định); isAscending=false để xem mới nhất trước.
    /// Lọc theo action (UPPER_SNAKE_CASE) và khoảng thời gian [from, to).
    /// </summary>
    [HttpGet("audit-logs")]
    [ProducesResponseType<PaginatedList<AdminAuditLogResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<AdminAuditLogResponse>> ListAuditLogs(
        [FromQuery] PaginationRequest pagination, [FromQuery] AuditLogFilterRequest filter, CancellationToken ct)
    {
        return adminService.ListAuditLogsAsync(pagination, filter, ct);
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

    // ---- Người thực thi ----

    /// <summary>
    /// Giao hoặc đổi Người thực thi của kho (lý do bắt buộc). Người được giao nhận lời mời và phải chấp nhận; người cũ giữ vai trò
    /// tới khi người mới chấp nhận. Kế hoạch đã nộp giấy chứng tử hoặc kho đã đóng → 409 EXECUTOR_NOT_ALLOWED (PLAN_LOCKED).
    /// </summary>
    [HttpPost("vaults/{vaultId:guid}/executor")]
    [ProducesResponseType<ExecutorInvitationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ExecutorInvitationResponse> AssignExecutor(Guid vaultId, AssignExecutorRequest request, CancellationToken ct)
    {
        return await planSetupService.AssignByAdminAsync(vaultId, request, ct);
    }

    // ---- Sự cố ----

    [HttpGet("incidents")]
    [ProducesResponseType<PaginatedList<IncidentSummaryResponse>>(StatusCodes.Status200OK)]
    public Task<PaginatedList<IncidentSummaryResponse>> ListIncidents(
        [FromQuery] string? status, [FromQuery] string? type, [FromQuery] Guid? assigneeUserId,
        [FromQuery] string? search, CancellationToken ct, [FromQuery] bool overdueOnly = false,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, [FromQuery] bool mine = false)
    {
        return incidentService.ListAsync(
            status, type, assigneeUserId, search, overdueOnly, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct, mine);
    }

    /// <summary>Số sự cố theo trạng thái cho các thẻ lọc; nhận cùng bộ lọc với danh sách, trừ trạng thái.</summary>
    [HttpGet("incidents/counts")]
    [ProducesResponseType<CountsResponse>(StatusCodes.Status200OK)]
    public Task<CountsResponse> CountIncidents(
        [FromQuery] string? type, [FromQuery] Guid? assigneeUserId, [FromQuery] string? search, CancellationToken ct,
        [FromQuery] bool overdueOnly = false, [FromQuery] bool mine = false)
    {
        return incidentService.CountsAsync(type, assigneeUserId, search, overdueOnly, mine, ct);
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

    // ---- Hold (chỉ xem; hold do hệ thống đặt và gỡ) ----

    [HttpGet("holds")]
    [ProducesResponseType<PaginatedList<HoldResponse>>(StatusCodes.Status200OK)]
    public Task<PaginatedList<HoldResponse>> ListHolds(
        [FromQuery] string? status, [FromQuery] Guid? vaultId, CancellationToken ct,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return holdService.ListAsync(status, vaultId, Clamp(pageIndex, 1, int.MaxValue), Clamp(pageSize, 1, 100), ct);
    }

    /// <summary>Số hold theo trạng thái cho các thẻ lọc.</summary>
    [HttpGet("holds/counts")]
    [ProducesResponseType<CountsResponse>(StatusCodes.Status200OK)]
    public Task<CountsResponse> CountHolds([FromQuery] Guid? vaultId, CancellationToken ct)
    {
        return holdService.CountsAsync(vaultId, ct);
    }

    [HttpGet("holds/{holdId:guid}")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    public Task<HoldResponse> GetHold(Guid holdId, CancellationToken ct)
    {
        return holdService.GetAsync(holdId, ct);
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

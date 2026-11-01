using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IAdminService
{
    Task<PaginatedList<AdminUserResponse>> ListUsersAsync(
        string? search, string? role, string? status, int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>
    /// Cấp hoặc đổi vai trò hệ thống (ROLE-01: Verifier/Admin chỉ được cấp qua quản trị). Lỗi: 404, 409
    /// ADMIN_ACTION_NOT_ALLOWED reason CANNOT_CHANGE_SELF / LAST_ADMIN.
    /// </summary>
    Task<AdminUserResponse> ChangeRoleAsync(Guid userId, ChangeUserRoleRequest request, CancellationToken ct);

    /// <summary>
    /// Một trang nhật ký toàn hệ thống (ADMIN-06). sortBy chỉ nhận "at"; isAscending=false để xem mới nhất trước.
    /// Lọc theo loại thao tác và khoảng thời gian [from, to). Lỗi: 400 VALIDATION_ERROR, 404 (không phải Admin).
    /// </summary>
    Task<PaginatedList<AdminAuditLogResponse>> ListAuditLogsAsync(
        PaginationRequest pagination, AuditLogFilterRequest filter, CancellationToken ct);

    /// <summary>Khóa, mở khóa, vô hiệu hóa. Cùng ràng buộc với đổi vai trò.</summary>
    Task<AdminUserResponse> ChangeStatusAsync(Guid userId, ChangeUserStatusRequest request, CancellationToken ct);

    /// <summary>
    /// Giao lại Verifier cho hồ sơ chứng tử đang chờ xét, kèm lý do (ASSIGN-01). Người mới phải đủ điều kiện như vòng xoay
    /// (không phải Owner, Executor, người gửi hồ sơ). Admin không tự chọn người lạ cho Executor và không duyệt thay (ASSIGN-02).
    /// </summary>
    Task ReassignVerifierAsync(Guid caseId, ReassignVerifierRequest request, CancellationToken ct);

    Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken ct);

    /// <summary>Admin tạo tài khoản hệ thống mới (Verifier, Executor, Admin). Kích hoạt ngay.</summary>
    Task<AdminUserResponse> CreateUserAsync(CreateAdminUserRequest request, CancellationToken ct);

}

/// <remarks>
/// - Mọi thao tác ghi AuditLogs trong cùng giao dịch. Lý do không chứa dữ liệu nhạy cảm (SEC-07).
/// - Admin không có API nào đọc nội dung tài sản, trang giấy chứng tử hay danh tính; chỉ quản trị tài khoản và điều phối.
/// - Không phải Admin → 404 như khu vực không tồn tại (SEC-04).
/// </remarks>
public sealed class AdminService : IAdminService
{
    private const string EndReasonAdmin = "ADMIN_REASSIGN";

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IAdminUserRepository adminUsers;
    private readonly IVaultRepository vaults;
    private readonly IDeathCaseRepository deathCases;
    private readonly IExecutorReader executorReader;
    private readonly IIncidentService incidentService;
    private readonly IIncidentRepository incidents;
    private readonly IHoldRepository holds;
    private readonly IRefreshTokenRepository refreshTokens;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly IPasswordHasher passwordHasher;
    private readonly TimeProvider timeProvider;

    public AdminService(
        ICurrentUser currentUser,
        IUserRepository users,
        IAdminUserRepository adminUsers,
        IVaultRepository vaults,
        IDeathCaseRepository deathCases,
        IExecutorReader executorReader,
        IIncidentService incidentService,
        IIncidentRepository incidents,
        IHoldRepository holds,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider)
    {
        this.refreshTokens = refreshTokens;
        this.currentUser = currentUser;
        this.users = users;
        this.adminUsers = adminUsers;
        this.vaults = vaults;
        this.deathCases = deathCases;
        this.executorReader = executorReader;
        this.incidentService = incidentService;
        this.incidents = incidents;
        this.holds = holds;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.passwordHasher = passwordHasher;
        this.timeProvider = timeProvider;
    }

    public async Task<PaginatedList<AdminUserResponse>> ListUsersAsync(
        string? search, string? role, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        UserRole? roleFilter = null;
        if (!string.IsNullOrWhiteSpace(role))
        {
            roleFilter = ParseRole(role, "role");
        }
        UserStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusFilter = ParseStatus(status, "status");
        }
        (List<User> items, int total) = await adminUsers.ListAsync(search, roleFilter, statusFilter, pageIndex, pageSize, ct);
        PaginatedList<AdminUserResponse> page = new PaginatedList<AdminUserResponse>();
        foreach (User user in items)
        {
            page.Items.Add(OperationsMapper.ToAdminUser(user));
        }
        page.TotalCount = total;
        page.PageIndex = pageIndex;
        page.PageSize = pageSize;
        page.TotalPages = (total + pageSize - 1) / pageSize;
        page.HasPreviousPage = pageIndex > 1;
        page.HasNextPage = pageIndex < page.TotalPages;
        return page;
    }

    public async Task<PaginatedList<AdminAuditLogResponse>> ListAuditLogsAsync(
        PaginationRequest pagination, AuditLogFilterRequest filter, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        if (!string.IsNullOrWhiteSpace(pagination.SortBy)
            && !string.Equals(pagination.SortBy, "at", StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationErrors.For("sortBy", "Chỉ sắp xếp theo at.");
        }
        AuditAction? action = AuditRules.ParseAction(filter.Action);
        if (filter.From != null && filter.To != null && filter.From.Value >= filter.To.Value)
        {
            throw ValidationErrors.For("from", "Thời điểm bắt đầu phải trước thời điểm kết thúc.");
        }

        (List<AuditLog> items, int total) = await audit.FindPageAsync(
            action, filter.From, filter.To, pagination.PageIndex, pagination.PageSize, pagination.IsAscending, ct);

        List<Guid> actorIds = new List<Guid>();
        foreach (AuditLog log in items)
        {
            if (log.ActorUserId != null && !actorIds.Contains(log.ActorUserId.Value))
            {
                actorIds.Add(log.ActorUserId.Value);
            }
        }
        Dictionary<Guid, User> actors = new Dictionary<Guid, User>();
        if (actorIds.Count > 0)
        {
            foreach (User actor in await adminUsers.FindByIdsAsync(actorIds, ct))
            {
                actors[actor.Id] = actor;
            }
        }

        List<AdminAuditLogResponse> responses = new List<AdminAuditLogResponse>();
        foreach (AuditLog log in items)
        {
            responses.Add(AuditLogMappings.ToAdminResponse(log, actors));
        }
        return responses.ToPaginatedList(total, pagination);
    }

    public async Task<AdminUserResponse> ChangeRoleAsync(Guid userId, ChangeUserRoleRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        UserRole newRole = ParseRole(request.Role, "role");
        // Executor là người ngoài do Owner mời theo từng kho (quan hệ với kho, ROLE-02), không phải vai trò Admin cấp.
        if (newRole == UserRole.Executor)
        {
            throw ValidationErrors.For("role", "Không cấp vai trò EXECUTOR. Người thực hiện do Owner mời theo từng kho.");
        }
        string reason = RequireReason(request.Reason);
        User user = await LoadUserAsync(userId, ct);
        if (user.Id == adminId)
        {
            throw NotAllowed("CANNOT_CHANGE_SELF");
        }
        if (user.Role == newRole)
        {
            return OperationsMapper.ToAdminUser(user);
        }
        if (user.Role == UserRole.Admin && user.Status == UserStatus.Active
            && await adminUsers.CountActiveAdminsAsync(ct) <= 1)
        {
            throw NotAllowed("LAST_ADMIN");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        user.Role = newRole;
        user.UpdatedAt = now;
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.UserRoleChanged,
            AuditTargetType.User, user.Id, null, AuditOutcome.Succeeded, newRole.ToApiString() + ": " + reason, now));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToAdminUser(user);
    }

    public async Task<AdminUserResponse> ChangeStatusAsync(Guid userId, ChangeUserStatusRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        UserStatus newStatus = ParseStatus(request.Status, "status");
        string reason = RequireReason(request.Reason);
        User user = await LoadUserAsync(userId, ct);
        if (user.Id == adminId)
        {
            throw NotAllowed("CANNOT_CHANGE_SELF");
        }
        if (user.Status == newStatus)
        {
            return OperationsMapper.ToAdminUser(user);
        }
        if (user.Role == UserRole.Admin && user.Status == UserStatus.Active && newStatus != UserStatus.Active
            && await adminUsers.CountActiveAdminsAsync(ct) <= 1)
        {
            throw NotAllowed("LAST_ADMIN");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        user.Status = newStatus;
        user.UpdatedAt = now;
        if (newStatus != UserStatus.Active)
        {
            // ROLE-04: khóa hoặc vô hiệu hóa thì thu hồi mọi phiên đăng nhập (refresh token) trong cùng giao dịch.
            // Access token đã cấp còn sống tối đa vài phút (Jwt:AccessTokenMinutes); các service nghiệp vụ kiểm Status ở mỗi thao tác.
            List<RefreshToken> sessions = await refreshTokens.FindActiveByUserAsync(user.Id, ct);
            foreach (RefreshToken session in sessions)
            {
                session.RevokedAt = now;
            }
        }
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.UserStatusChanged,
            AuditTargetType.User, user.Id, null, AuditOutcome.Succeeded, newStatus.ToApiString() + ": " + reason, now));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToAdminUser(user);
    }

    public async Task ReassignVerifierAsync(Guid caseId, ReassignVerifierRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string reason = RequireReason(request.Reason);
        DeathCase? deathCase = await deathCases.FindByIdAsync(caseId, ct);
        if (deathCase == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
        }
        if (!DeathCaseReviewRules.CanDecide(deathCase.Status))
        {
            throw NotAllowed("CASE_NOT_PENDING");
        }
        Vault? vault = await vaults.FindByIdAsync(deathCase.VaultId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.DeathCaseNotFound, "Không tìm thấy hồ sơ chứng tử.");
        }

        Guid verifierId = request.VerifierUserId!.Value;
        User? verifier = await users.FindByIdAsync(verifierId, ct);
        if (!DeathCaseAssignmentRules.IsVerifierAvailable(verifier))
        {
            throw ValidationErrors.For("verifierUserId", "Chỉ giao cho Verifier đang hoạt động.");
        }
        DeathCaseVersion reviewVersion = DeathCaseReviewRules.FindReviewVersion(deathCase);
        bool conflict = verifierId == vault.OwnerId || verifierId == reviewVersion.SubmittedByUserId;
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == verifierId)
            {
                conflict = true;
            }
        }
        if (conflict)
        {
            throw ValidationErrors.For("verifierUserId", "Verifier không được là chủ kho, Executor hoặc người gửi hồ sơ.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (DeathCaseAssignment assignment in deathCase.Assignments)
        {
            if (assignment.EndedAt == null)
            {
                if (assignment.VerifierUserId == verifierId)
                {
                    // Đã đúng người đó: gọi lặp, không làm gì.
                    return;
                }
                assignment.EndedAt = now;
                assignment.EndReason = EndReasonAdmin;
            }
        }
        DeathCaseAssignment created = new DeathCaseAssignment();
        created.CaseId = deathCase.Id;
        created.VerifierUserId = verifierId;
        created.AssignedAt = now;
        created.DueAt = DeathCaseAssignmentRules.ComputeDueAt(now, deathCase.ObjectionEndsAt);
        deathCase.Assignments.Add(created);
        deathCases.AddAssignment(created);
        // Đổi etag để Verifier cũ đang thao tác nhận 409 EDIT_CONFLICT, tải lại thì 404.
        deathCase.UpdatedAt = now;
        await incidentService.ResolveByTargetAsync(
            IncidentType.NoVerifier, IncidentRules.TargetDeathCase, deathCase.Id, "Admin đã giao Verifier.", ct);
        audit.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.VerifierReassigned,
            AuditTargetType.DeathCase, deathCase.Id, null, AuditOutcome.Succeeded, reason, now));
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (incidents.IsUniqueViolation(ex))
        {
            // Job phân công vừa giao cùng lúc (chỉ mục "mỗi hồ sơ một phân công còn hiệu lực"): báo xung đột để Admin tải lại.
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Hồ sơ vừa được giao ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }
    }

    public async Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        AdminDashboardResponse response = new AdminDashboardResponse();
        response.OpenIncidents = await incidents.CountOpenAsync(ct);
        response.OverdueIncidents = await incidents.CountOverdueAsync(now, ct);
        response.UnassignedIncidents = await incidents.CountUnassignedAsync(ct);
        response.ActiveHolds = await holds.CountActiveAsync(ct);
        response.HoldsPastReview = await holds.CountReviewOverdueAsync(now, ct);
        response.ActiveAdmins = await adminUsers.CountActiveAdminsAsync(ct);
        (List<User> verifiers, int verifierCount) = await adminUsers.ListAsync(
            null, UserRole.Verifier, UserStatus.Active, 1, 1, ct);
        response.ActiveVerifiers = verifierCount;
        response.CasesWithoutVerifier = await adminUsers.CountCasesWithoutVerifierAsync(ct);
        response.ServerTime = now;
        return response;
    }

    private async Task<User> LoadUserAsync(Guid userId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return user;
    }

    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return userId;
    }


    public async Task<AdminUserResponse> CreateUserAsync(CreateAdminUserRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw ValidationErrors.For("email", "Vui lòng nhập địa chỉ email.");
        }
        string fullName = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw ValidationErrors.For("fullName", "Vui lòng nhập họ và tên.");
        }
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw ValidationErrors.For("password", "Mật khẩu tối thiểu 8 ký tự.");
        }

        UserRole targetRole = ParseRole(request.Role, "role");
        // Executor là người ngoài do Owner mời theo từng kho (ROLE-02), Admin không tạo tài khoản Executor.
        if (targetRole != UserRole.Verifier && targetRole != UserRole.Admin)
        {
            throw ValidationErrors.For("role", "Chỉ được tạo tài khoản VERIFIER hoặc ADMIN. Người thực hiện do Owner mời theo từng kho.");
        }

        User? existing = await users.FindByEmailAsync(email, ct);
        if (existing != null)
        {
            throw new AppException(
                AppErrorType.Conflict,
                ErrorCode.EmailAlreadyExists,
                "Địa chỉ email này đã được sử dụng.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        User newUser = new User
        {
            Email = email,
            FullName = fullName,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = targetRole,
            Status = UserStatus.Active,
            EmailVerifiedAt = now,
            CreatedAt = now,
        };

        if (!await users.TryAddAsync(newUser, ct))
        {
            throw new AppException(
                AppErrorType.Conflict,
                ErrorCode.EmailAlreadyExists,
                "Địa chỉ email này đã được sử dụng.");
        }

        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.AdminCreatedUser,
            AuditTargetType.User, newUser.Id, null, AuditOutcome.Succeeded, $"Tạo tài khoản {targetRole.ToApiString()}: {email}", now));
        await unitOfWork.SaveChangesAsync(ct);

        return OperationsMapper.ToAdminUser(newUser);
    }

    private static string RequireReason(string? reason)
    {
        string? text = AuditRules.NormalizeReason(reason);
        if (text == null)
        {
            throw ValidationErrors.For("reason", "Vui lòng ghi lý do.");
        }
        return text;
    }

    private static UserRole ParseRole(string? value, string field)
    {
        foreach (UserRole candidate in Enum.GetValues<UserRole>())
        {
            if (string.Equals(candidate.ToApiString(), value?.Trim(), StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        throw ValidationErrors.For(field, "Vai trò chỉ nhận USER, VERIFIER, ADMIN hoặc EXECUTOR.");
    }

    private static UserStatus ParseStatus(string? value, string field)
    {
        foreach (UserStatus candidate in Enum.GetValues<UserStatus>())
        {
            if (string.Equals(candidate.ToApiString(), value?.Trim(), StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        throw ValidationErrors.For(field, "Trạng thái chỉ nhận ACTIVE, LOCKED hoặc DISABLED.");
    }

    private static AppException NotAllowed(string reason)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason;
        return new AppException(AppErrorType.Conflict, ErrorCode.AdminActionNotAllowed,
            "Thao tác quản trị không được phép lúc này.", extensions);
    }
}

using LegacyVault.Application.Notifications;
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

    /// <summary>Số tài khoản theo vai trò cho các thẻ lọc; cùng bộ lọc tìm kiếm và trạng thái với danh sách.</summary>
    Task<CountsResponse> CountUsersByRoleAsync(string? search, string? status, CancellationToken ct);

    /// <summary>Chi tiết một tài khoản: thông tin, hồ sơ đang giữ, quyết định và hoạt động gần đây. Không có tài khoản → 404.</summary>
    Task<AdminUserDetailResponse> GetUserDetailAsync(Guid userId, CancellationToken ct);

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

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IAdminUserRepository adminUsers;
    private readonly IVaultRepository vaults;
    private readonly IHandoverRecordRepository records;
    private readonly IExecutorReader executorReader;
    private readonly IRefreshTokenRepository refreshTokens;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly IPasswordHasher passwordHasher;
    private readonly TimeProvider timeProvider;
    private readonly IPasswordResetService passwordResetService;
    private readonly IHandoverRepository handover;
    private readonly IEmailDomainChecker domainChecker;
    private readonly IEmailBounceStore bounces;

    public AdminService(
        ICurrentUser currentUser,
        IUserRepository users,
        IAdminUserRepository adminUsers,
        IVaultRepository vaults,
        IHandoverRecordRepository records,
        IExecutorReader executorReader,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider,
        IHandoverRepository handover,
        IPasswordResetService passwordResetService,
        IEmailDomainChecker domainChecker,
        IEmailBounceStore bounces)
    {
        this.handover = handover;
        this.passwordResetService = passwordResetService;
        this.bounces = bounces;
        this.domainChecker = domainChecker;
        this.refreshTokens = refreshTokens;
        this.currentUser = currentUser;
        this.users = users;
        this.adminUsers = adminUsers;
        this.vaults = vaults;
        this.records = records;
        this.executorReader = executorReader;
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
        HashSet<string> bouncedEmails = await bounces.FindBouncedAsync(page.Items.Select(u => u.Email), ct);
        foreach (AdminUserResponse item in page.Items)
        {
            item.EmailUndeliverable = bouncedEmails.Contains(item.Email.Trim().ToLowerInvariant());
        }
        page.TotalCount = total;
        page.PageIndex = pageIndex;
        page.PageSize = pageSize;
        page.TotalPages = (total + pageSize - 1) / pageSize;
        page.HasPreviousPage = pageIndex > 1;
        page.HasNextPage = pageIndex < page.TotalPages;
        return page;
    }

    public async Task<CountsResponse> CountUsersByRoleAsync(string? search, string? status, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        UserStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusFilter = ParseStatus(status, "status");
        }
        Dictionary<UserRole, int> byRole = await adminUsers.CountByRoleAsync(search, statusFilter, ct);
        CountsResponse response = new CountsResponse();
        foreach (UserRole role in Enum.GetValues<UserRole>())
        {
            int count = byRole.TryGetValue(role, out int found) ? found : 0;
            response.Counts[role.ToApiString()] = count;
            response.Total += count;
        }
        return response;
    }

    public async Task<AdminUserDetailResponse> GetUserDetailAsync(Guid userId, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        User user = await LoadUserAsync(userId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        AdminUserDetailResponse response = new AdminUserDetailResponse();
        AdminUserResponse basic = OperationsMapper.ToAdminUser(user);
        response.Id = basic.Id;
        response.Email = basic.Email;
        response.FullName = basic.FullName;
        response.Role = basic.Role;
        response.Status = basic.Status;
        response.EmailVerified = basic.EmailVerified;
        response.CreatedAt = basic.CreatedAt;
        response.LastLoginAt = basic.LastLoginAt;
        response.Etag = basic.Etag;

        if (user.Role == UserRole.Verifier)
        {
            await AddVerifierWorkAsync(response, userId, ct);
        }
        if (user.Role == UserRole.Executor)
        {
            await AddHeldVaultsAsync(response, userId, ct);
        }
        foreach (AuditLog log in await adminUsers.ListRecentAuditByActorAsync(userId, 10, ct))
        {
            AdminUserActivityResponse item = new AdminUserActivityResponse();
            item.At = log.At;
            item.Action = log.Action.ToApiString();
            item.Outcome = log.Outcome.ToApiString();
            response.RecentActivity.Add(item);
        }
        return response;
    }

    /// <summary>Số lần xét gần nhất của luật sư hiện ở chi tiết tài khoản.</summary>
    private const int RecentDecisionCount = 10;

    /// <summary>
    /// Việc đang giữ của luật sư (biên bản bàn giao chưa xong, hồ sơ báo tin chờ xét) và các lần xét gần nhất. Chỉ trả mã,
    /// trạng thái và thời điểm; không trả lý do hay nội dung hồ sơ.
    /// </summary>
    private async Task AddVerifierWorkAsync(AdminUserDetailResponse response, Guid verifierUserId, CancellationToken ct)
    {
        foreach ((Guid recordId, HandoverRecordStatus status, DateTimeOffset requestedAt) in
            await adminUsers.ListOpenRecordsAsync(verifierUserId, ct))
        {
            AdminUserCaseResponse item = new AdminUserCaseResponse();
            item.Kind = AdminUserCaseResponse.KindHandoverRecord;
            item.CaseId = recordId;
            item.CaseCode = HandoverRecordRules.CaseCode(recordId);
            item.Status = status.ToApiString();
            item.ReceivedAt = requestedAt;
            response.AssignedCases.Add(item);
        }
        foreach ((Guid reportId, HandoverClaimStatus status, DateTimeOffset createdAt) in
            await adminUsers.ListPendingReportsAsync(verifierUserId, ct))
        {
            AdminUserCaseResponse item = new AdminUserCaseResponse();
            item.Kind = AdminUserCaseResponse.KindHandoverReport;
            item.CaseId = reportId;
            item.CaseCode = HandoverRecordRules.CaseCode(reportId);
            item.Status = status.ToApiString();
            item.ReceivedAt = createdAt;
            response.AssignedCases.Add(item);
        }

        // Ghép hai danh sách đã xếp mới nhất trước (xét yêu cầu và xét hồ sơ báo tin), lấy 10 dòng đầu.
        List<(Guid RecordId, HandoverClaimStatus Status, DateTimeOffset ReviewedAt)> claims =
            await adminUsers.ListRecentClaimReviewsAsync(verifierUserId, RecentDecisionCount, ct);
        List<(Guid ReportId, HandoverClaimStatus Status, DateTimeOffset ReviewedAt)> reports =
            await adminUsers.ListRecentReportReviewsAsync(verifierUserId, RecentDecisionCount, ct);
        int claimIndex = 0;
        int reportIndex = 0;
        while (response.RecentDecisions.Count < RecentDecisionCount
            && (claimIndex < claims.Count || reportIndex < reports.Count))
        {
            bool takeClaim;
            if (reportIndex >= reports.Count)
            {
                takeClaim = true;
            }
            else if (claimIndex >= claims.Count)
            {
                takeClaim = false;
            }
            else
            {
                takeClaim = claims[claimIndex].ReviewedAt >= reports[reportIndex].ReviewedAt;
            }
            AdminUserDecisionResponse decision = new AdminUserDecisionResponse();
            if (takeClaim)
            {
                decision.Kind = AdminUserCaseResponse.KindHandoverRecord;
                decision.CaseId = claims[claimIndex].RecordId;
                decision.Decision = claims[claimIndex].Status.ToApiString();
                decision.DecidedAt = claims[claimIndex].ReviewedAt;
                claimIndex = claimIndex + 1;
            }
            else
            {
                decision.Kind = AdminUserCaseResponse.KindHandoverReport;
                decision.CaseId = reports[reportIndex].ReportId;
                decision.Decision = reports[reportIndex].Status.ToApiString();
                decision.DecidedAt = reports[reportIndex].ReviewedAt;
                reportIndex = reportIndex + 1;
            }
            decision.CaseCode = HandoverRecordRules.CaseCode(decision.CaseId);
            response.RecentDecisions.Add(decision);
        }
    }

    /// <summary>
    /// Kho mà Người thực thi đang giữ: tên Chủ tài sản đã che, trạng thái kế hoạch, bước biên bản và Admin đổi người được không.
    /// Không trả CCCD, tên tài sản hay email người nhận.
    /// </summary>
    private async Task AddHeldVaultsAsync(AdminUserDetailResponse response, Guid executorUserId, CancellationToken ct)
    {
        foreach ((Guid vaultId, string ownerFullName, VaultStatus vaultStatus, PlanStatus planStatus,
            HandoverRecordStatus? recordStatus) in await adminUsers.ListHeldVaultsAsync(executorUserId, ct))
        {
            AdminUserHeldVaultResponse item = new AdminUserHeldVaultResponse();
            item.VaultId = vaultId;
            item.OwnerNameMasked = MaskFullName(ownerFullName);
            item.PlanStatus = planStatus.ToApiString();
            if (recordStatus != null)
            {
                item.HandoverRecordStatus = recordStatus.Value.ToApiString();
            }
            item.HandoverUnderway = ExecutorAssignRules.IsHandoverUnderway(planStatus);
            item.CanChangeExecutor = ExecutorAssignRules.PlanAllowsAssign(vaultStatus, planStatus);
            response.HeldVaults.Add(item);
        }
    }

    /// <summary>Che họ tên: chỉ giữ chữ đầu mỗi từ kèm dấu chấm, ví dụ "Nguyễn Văn An" thành "N. V. A.".</summary>
    public static string MaskFullName(string fullName)
    {
        string[] words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> initials = new List<string>();
        foreach (string word in words)
        {
            initials.Add(word.Substring(0, 1).ToUpperInvariant() + ".");
        }
        return string.Join(" ", initials);
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
        string reason = RequireReason(request.Reason);
        // Đếm Admin còn hoạt động và ghi phải cùng một giao dịch Serializable: hai Admin hạ nhau cùng lúc không thể vượt LAST_ADMIN.
        return await unitOfWork.ExecuteSerializableAsync(
            () => ChangeRoleCoreAsync(adminId, userId, newRole, reason, ct), ct);
    }

    private async Task<AdminUserResponse> ChangeRoleCoreAsync(
        Guid adminId, Guid userId, UserRole newRole, string reason, CancellationToken ct)
    {
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
        // Người đang nhận tài sản bàn giao (lượt chưa kết thúc) không được cấp vai trò nhân sự: nhân sự không được làm người nhận.
        bool becomesStaff = newRole == UserRole.Executor || newRole == UserRole.Verifier || newRole == UserRole.Admin;
        if (becomesStaff && await handover.HasOpenClaimedTurnAsync(user.Id, ct))
        {
            throw NotAllowed("RECIPIENT_HAS_ACTIVE_HANDOVER");
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
        // Cùng lý do với ChangeRoleAsync: kiểm LAST_ADMIN và ghi trong một giao dịch Serializable.
        return await unitOfWork.ExecuteSerializableAsync(
            () => ChangeStatusCoreAsync(adminId, userId, newStatus, reason, ct), ct);
    }

    private async Task<AdminUserResponse> ChangeStatusCoreAsync(
        Guid adminId, Guid userId, UserStatus newStatus, string reason, CancellationToken ct)
    {
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
        // caseId là id của biên bản bàn giao.
        Guid adminId = await RequireAdminAsync(ct);
        string reason = RequireReason(request.Reason);
        HandoverRecord? record = await records.FindByIdAsync(caseId, ct);
        if (record == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy biên bản bàn giao.");
        }
        // Biên bản đã đủ chữ ký thì không giao lại nữa.
        if (record.Status == HandoverRecordStatus.Completed)
        {
            throw NotAllowed("CASE_NOT_PENDING");
        }
        Vault? vault = await vaults.FindByIdAsync(record.VaultId, ct);
        if (vault == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.HandoverNotFound, "Không tìm thấy biên bản bàn giao.");
        }

        Guid verifierId = request.VerifierUserId!.Value;
        User? verifier = await users.FindByIdAsync(verifierId, ct);
        if (verifier == null || verifier.Role != UserRole.Verifier || verifier.Status != UserStatus.Active)
        {
            throw ValidationErrors.For("verifierUserId", "Chỉ giao cho luật sư đang hoạt động.");
        }
        bool conflict = verifierId == vault.OwnerId || verifierId == record.RequestedByUserId;
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
            throw ValidationErrors.For("verifierUserId", "Luật sư không được là Chủ tài sản hoặc Người thực thi của kho.");
        }
        if (record.VerifierUserId == verifierId)
        {
            // Đã đúng người đó: gọi lặp, không làm gì.
            return;
        }

        // Đổi luật sư: chữ ký của luật sư cũ không còn giá trị nên tăng số bản nếu biên bản đang ở bước ký.
        DateTimeOffset now = timeProvider.GetUtcNow();
        record.VerifierUserId = verifierId;
        if (record.Status == HandoverRecordStatus.Signing)
        {
            record.RevisionNo = record.RevisionNo + 1;
        }
        record.UpdatedAt = now;
        audit.Add(AuditRules.Create(vault.Id, adminId, AuditActorRole.Admin, AuditAction.VerifierReassigned,
            AuditTargetType.Vault, vault.Id, null, AuditOutcome.Succeeded, reason, now));
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<AdminDashboardResponse> GetDashboardAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        AdminDashboardResponse response = new AdminDashboardResponse();
        response.ActiveAdmins = await adminUsers.CountActiveAdminsAsync(ct);
        (List<User> verifiers, int verifierCount) = await adminUsers.ListAsync(
            null, UserRole.Verifier, UserStatus.Active, 1, 1, ct);
        response.ActiveVerifiers = verifierCount;
        response.CasesWithoutVerifier = await adminUsers.CountCasesWithoutVerifierAsync(ct);
        response.ServerTime = now;

        foreach ((PlanStatus status, int count) in await adminUsers.CountPlansByStatusAsync(ct))
        {
            if (status == PlanStatus.Closed)
            {
                response.ClosedPlans = count;
                continue;
            }
            DashboardPlanStageResponse stage = new DashboardPlanStageResponse();
            stage.Stage = status.ToApiString();
            stage.Count = count;
            response.PlanStages.Add(stage);
        }

        List<(Guid VerifierUserId, int Open, int Overdue)> load = await adminUsers.CountVerifierLoadAsync(now, ct);
        List<User> loadUsers = await adminUsers.FindByIdsAsync(load.Select(l => l.VerifierUserId).ToList(), ct);
        foreach ((Guid verifierUserId, int open, int overdue) in load.OrderByDescending(l => l.Open))
        {
            User? verifier = loadUsers.FirstOrDefault(u => u.Id == verifierUserId);
            if (verifier == null || verifier.Status != UserStatus.Active)
            {
                continue;
            }
            DashboardVerifierLoadResponse item = new DashboardVerifierLoadResponse();
            item.UserId = verifierUserId;
            item.FullName = verifier.FullName;
            item.OpenCases = open;
            item.OverdueCases = overdue;
            response.VerifierLoad.Add(item);
        }

        foreach ((AuditLog log, string? actorName) in await adminUsers.ListRecentAuditAsync(5, ct))
        {
            DashboardRecentActivityResponse activity = new DashboardRecentActivityResponse();
            activity.At = log.At;
            activity.ActorFullName = actorName;
            activity.Action = log.Action.ToApiString();
            response.RecentActivity.Add(activity);
        }
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
        UserRole targetRole = ParseRole(request.Role, "role");
        // Người xét duyệt / Người thực thi: bỏ trống mật khẩu thì gửi thư mời đặt mật khẩu tới email; mở được link chính là
        // chứng minh người đó sở hữu email. Quản trị viên vẫn bắt buộc mật khẩu khởi tạo.
        bool inviteByEmail = string.IsNullOrWhiteSpace(request.Password) && targetRole != UserRole.Admin;
        if (!inviteByEmail && (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8))
        {
            throw ValidationErrors.For("password", "Mật khẩu tối thiểu 8 ký tự.");
        }

        // Executor là người ngoài do Owner mời theo từng kho (ROLE-02), Admin không tạo tài khoản Executor.
        if (targetRole != UserRole.Verifier && targetRole != UserRole.Admin && targetRole != UserRole.Executor)
        {
            throw ValidationErrors.For("role", "Chỉ được tạo tài khoản VERIFIER, EXECUTOR hoặc ADMIN.");
        }

        await EmailDomainGuard.EnsureAsync(domainChecker, email, "email", ct);
        User? existing = await users.FindByEmailAsync(email, ct);
        if (existing != null)
        {
            throw new AppException(
                AppErrorType.Conflict,
                ErrorCode.EmailAlreadyExists,
                "Địa chỉ email này đã được sử dụng.");
        }

        // Số không bắt buộc ở đây; có thì chuẩn hóa giống đăng ký để so trùng đúng, và mỗi số chỉ thuộc một tài khoản.
        string? phone = string.IsNullOrWhiteSpace(request.Phone) ? null : PhoneRules.NormalizeContactPhone(request.Phone, "phone");
        if (phone != null && await users.PhoneExistsAsync(phone, null, ct))
        {
            throw PhoneRules.AlreadyUsed("phone");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        User newUser = new User
        {
            Email = email,
            FullName = fullName,
            Phone = phone,
            PracticeCertificateNumber = TrimOrNull(request.PracticeCertificateNumber, targetRole),
            LawyerCardNumber = TrimOrNull(request.LawyerCardNumber, targetRole),
            // Người thực thi hệ thống: số thẻ chỉ để hiển thị. Chỉ tính "đã xác minh pháp lý" khi Admin tải ảnh thẻ lên.
            ExecutorRoleTitle = targetRole == UserRole.Executor ? TrimOrNull(request.ExecutorRoleTitle, targetRole) : null,
            LegalOrganization = targetRole == UserRole.Executor ? TrimOrNull(request.LegalOrganization, targetRole) : null,
            // Mời qua email: mật khẩu ngẫu nhiên không ai biết, người nhận tự đặt qua link.
            PasswordHash = passwordHasher.Hash(inviteByEmail
                ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                : request.Password),
            Role = targetRole,
            Status = UserStatus.Active,
            // Tài khoản do Admin tạo luôn coi là đã xác minh email (Admin chịu trách nhiệm về địa chỉ). Mời qua email thì
            // mật khẩu ngẫu nhiên không ai biết, chỉ người mở được link gửi tới email đó mới đặt được mật khẩu.
            EmailVerifiedAt = now,
            CreatedAt = now,
        };

        if (!await users.TryAddAsync(newUser, ct))
        {
            if (phone != null && await users.PhoneExistsAsync(phone, null, ct))
            {
                throw PhoneRules.AlreadyUsed("phone");
            }
            throw new AppException(
                AppErrorType.Conflict,
                ErrorCode.EmailAlreadyExists,
                "Địa chỉ email này đã được sử dụng.");
        }

        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.AdminCreatedUser,
            AuditTargetType.User, newUser.Id, null, AuditOutcome.Succeeded, $"Tạo tài khoản {targetRole.ToApiString()}: {email}", now));
        await unitOfWork.SaveChangesAsync(ct);

        if (inviteByEmail)
        {
            // shortcut: dùng link đặt lại mật khẩu (hạn 30 phút); hết hạn thì người nhận bấm "Quên mật khẩu" để lấy link mới.
            await passwordResetService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = email }, ct);
        }

        return OperationsMapper.ToAdminUser(newUser);
    }

    /// <summary>Số chứng chỉ và thẻ luật sư chỉ có ý nghĩa với Người xét duyệt; vai trò khác bỏ qua. Trống = không có.</summary>
    private static string? TrimOrNull(string? value, UserRole role)
    {
        if ((role != UserRole.Verifier && role != UserRole.Executor) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.Trim();
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

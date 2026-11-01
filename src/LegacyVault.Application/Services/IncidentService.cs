using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật thuần của sự cố (SRS §6.4, ASSIGN-04, AC-40).</summary>
public static class IncidentRules
{
    /// <summary>Hạn xử lý mặc định và mốc nhắc người phụ trách (bảng thời hạn: việc thủ công nhắc sau 48 giờ).</summary>
    public static readonly TimeSpan ReminderAfter = TimeSpan.FromHours(48);

    /// <summary>Báo Admin khi quá 72 giờ chưa xử lý.</summary>
    public static readonly TimeSpan EscalateAfter = TimeSpan.FromHours(72);

    /// <summary>Xem lại sự cố và hold tối đa 7 ngày một lần.</summary>
    public static readonly TimeSpan MaxReviewInterval = TimeSpan.FromDays(7);

    /// <summary>Chưa có ai nhận: xem lại sau 1 ngày để không bị trôi (ASSIGN-05).</summary>
    public static readonly TimeSpan UnassignedReview = TimeSpan.FromDays(1);

    public const int MaxNoteLength = 1000;

    public const string TargetDeathCase = "DEATH_CASE";
    public const string TargetPlan = "PLAN";
    public const string TargetHold = "HOLD";
    public const string TargetUser = "USER";

    /// <summary>Chủ kho và Executor của kho không được xử lý sự cố của chính kho đó (ROLE-03).</summary>
    public static bool IsStaff(User? user)
    {
        return user != null && user.Status == UserStatus.Active
            && (user.Role == UserRole.Admin || user.Role == UserRole.Verifier);
    }
}

public interface IIncidentService
{
    /// <summary>
    /// Nội bộ: mở sự cố cho (loại, đối tượng) nếu chưa có sự cố đang mở. KHÔNG SaveChanges: người gọi lưu cùng thao tác của mình.
    /// Luôn có người phụ trách (preferred, hoặc Admin ít việc nhất) hoặc ngày xem lại sau 1 ngày nếu không ai nhận được.
    /// Trả sự cố (mới hoặc đang mở).
    /// </summary>
    Task<Incident> RaiseAsync(
        IncidentType type, Guid? vaultId, string targetType, Guid? targetId, string summary,
        Guid? preferredAssigneeUserId, CancellationToken ct);

    /// <summary>Nội bộ: đóng sự cố đang mở của (loại, đối tượng) nếu có. KHÔNG SaveChanges.</summary>
    Task ResolveByTargetAsync(IncidentType type, string targetType, Guid? targetId, string note, CancellationToken ct);

    /// <summary>
    /// Nội bộ: đổi người phụ trách sự cố đang mở của (loại, đối tượng) khi việc được giao cho người khác (ALIVE-08).
    /// Hạn nhắc tính lại từ bây giờ (ReminderAfter), RemindedCount về 0, ghi sự kiện Assigned. Không có sự cố mở hoặc
    /// đã đúng người thì không làm gì. KHÔNG SaveChanges.
    /// </summary>
    Task ReassignByTargetAsync(
        IncidentType type, string targetType, Guid? targetId, Guid newAssigneeUserId, string note, CancellationToken ct);

    Task<PaginatedList<IncidentSummaryResponse>> ListAsync(
        string? status, string? type, Guid? assigneeUserId, bool overdueOnly, int pageIndex, int pageSize, CancellationToken ct);

    Task<IncidentDetailResponse> GetAsync(Guid incidentId, CancellationToken ct);

    Task<IncidentDetailResponse> AssignAsync(Guid incidentId, Guid assigneeUserId, string reason, string etag, CancellationToken ct);

    Task<IncidentDetailResponse> AddNoteAsync(Guid incidentId, string note, CancellationToken ct);

    /// <summary>Ghi kết quả xem lại và ngày xem lại tiếp theo (tối đa 7 ngày).</summary>
    Task<IncidentDetailResponse> ReviewAsync(Guid incidentId, DateTimeOffset nextReviewAt, string note, string etag, CancellationToken ct);

    Task<IncidentDetailResponse> ResolveAsync(Guid incidentId, string note, string etag, CancellationToken ct);
}

/// <remarks>
/// - Admin không dùng sự cố để mở tài sản hay duyệt thay: không có thao tác nào ở đây đụng tới nội dung hay quyết định hồ sơ.
/// - Giao lại: người nhận phải là Admin/Verifier đang hoạt động và không phải Owner hay Executor đang hiệu lực của kho (ROLE-03).
/// - Mọi thao tác của Admin ghi AuditLogs trong cùng giao dịch (SEC-13).
/// </remarks>
public sealed class IncidentService : IIncidentService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IExecutorReader executorReader;
    private readonly IIncidentRepository incidents;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public IncidentService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IExecutorReader executorReader,
        IIncidentRepository incidents,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.executorReader = executorReader;
        this.incidents = incidents;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<Incident> RaiseAsync(
        IncidentType type, Guid? vaultId, string targetType, Guid? targetId, string summary,
        Guid? preferredAssigneeUserId, CancellationToken ct)
    {
        Incident? existing = await incidents.FindOpenAsync(type, targetType, targetId, ct);
        if (existing != null)
        {
            return existing;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Incident incident = new Incident();
        incident.Type = type;
        incident.Status = IncidentStatus.Open;
        incident.VaultId = vaultId;
        incident.TargetType = targetType;
        incident.TargetId = targetId;
        incident.Summary = AuditRules.NormalizeReason(summary) ?? type.ToApiString();
        incident.CreatedAt = now;
        incident.DueAt = now + IncidentRules.ReminderAfter;
        incident.NextReviewAt = now + IncidentRules.MaxReviewInterval;

        Guid? assignee = preferredAssigneeUserId;
        if (assignee == null)
        {
            assignee = await incidents.FindLeastLoadedAdminAsync(ct);
        }
        incident.AssigneeUserId = assignee;
        if (assignee == null)
        {
            // Không ai nhận được: ghi "bị kẹt" và xem lại sau 1 ngày, không bao giờ để trôi (ASSIGN-05).
            incident.NextReviewAt = now + IncidentRules.UnassignedReview;
        }
        incidents.Add(incident);
        AddEvent(incident, IncidentEventKind.Raised, null, null, now);
        return incident;
    }

    public async Task ResolveByTargetAsync(
        IncidentType type, string targetType, Guid? targetId, string note, CancellationToken ct)
    {
        Incident? open = await incidents.FindOpenAsync(type, targetType, targetId, ct);
        if (open == null)
        {
            return;
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        open.Status = IncidentStatus.Resolved;
        open.ResolvedAt = now;
        open.ResolutionNote = AuditRules.NormalizeReason(note);
        open.UpdatedAt = now;
        AddEvent(open, IncidentEventKind.Resolved, null, note, now);
    }

    public async Task ReassignByTargetAsync(
        IncidentType type, string targetType, Guid? targetId, Guid newAssigneeUserId, string note, CancellationToken ct)
    {
        Incident? open = await incidents.FindOpenAsync(type, targetType, targetId, ct);
        if (open == null)
        {
            return;
        }
        if (open.AssigneeUserId == newAssigneeUserId)
        {
            return;
        }
        // Người làm là hệ thống (job nền) nên không kiểm Admin, không etag, không ghi AuditLogs.
        DateTimeOffset now = timeProvider.GetUtcNow();
        open.AssigneeUserId = newAssigneeUserId;
        open.Status = IncidentStatus.InProgress;
        open.RemindedCount = 0;
        open.DueAt = now + IncidentRules.ReminderAfter;
        open.NextReviewAt = now + IncidentRules.MaxReviewInterval;
        open.UpdatedAt = now;
        AddEvent(open, IncidentEventKind.Assigned, null, note, now);
    }

    public async Task<PaginatedList<IncidentSummaryResponse>> ListAsync(
        string? status, string? type, Guid? assigneeUserId, bool overdueOnly, int pageIndex, int pageSize, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        IncidentStatus? statusFilter = ParseEnum<IncidentStatus>(status, "status");
        IncidentType? typeFilter = ParseEnum<IncidentType>(type, "type");
        DateTimeOffset now = timeProvider.GetUtcNow();
        (List<Incident> items, int total) = await incidents.ListAsync(
            statusFilter, typeFilter, assigneeUserId, overdueOnly, now, pageIndex, pageSize, ct);

        PaginatedList<IncidentSummaryResponse> page = new PaginatedList<IncidentSummaryResponse>();
        foreach (Incident incident in items)
        {
            page.Items.Add(OperationsMapper.ToSummary(incident, now));
        }
        page.TotalCount = total;
        page.PageIndex = pageIndex;
        page.PageSize = pageSize;
        page.TotalPages = (total + pageSize - 1) / pageSize;
        page.HasPreviousPage = pageIndex > 1;
        page.HasNextPage = pageIndex < page.TotalPages;
        return page;
    }

    public async Task<IncidentDetailResponse> GetAsync(Guid incidentId, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        Incident incident = await LoadAsync(incidentId, ct);
        return OperationsMapper.ToDetail(incident, timeProvider.GetUtcNow());
    }

    public async Task<IncidentDetailResponse> AssignAsync(
        Guid incidentId, Guid assigneeUserId, string reason, string etag, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string note = RequireNote(reason, "reason");
        Incident incident = await LoadOpenAsync(incidentId, etag, ct);

        User? assignee = await users.FindByIdAsync(assigneeUserId, ct);
        if (!IncidentRules.IsStaff(assignee))
        {
            throw ValidationErrors.For("assigneeUserId", "Chỉ giao cho Admin hoặc Verifier đang hoạt động.");
        }
        if (incident.VaultId != null)
        {
            await EnsureNotInvolvedAsync(incident.VaultId.Value, assigneeUserId, ct);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        incident.AssigneeUserId = assigneeUserId;
        incident.Status = IncidentStatus.InProgress;
        incident.RemindedCount = 0;
        incident.DueAt = now + IncidentRules.ReminderAfter;
        incident.NextReviewAt = now + IncidentRules.MaxReviewInterval;
        incident.UpdatedAt = now;
        AddEvent(incident, IncidentEventKind.Assigned, adminId, note, now);
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.IncidentAssigned,
            AuditTargetType.Incident, incident.Id, null, AuditOutcome.Succeeded, note, now));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToDetail(incident, now);
    }

    public async Task<IncidentDetailResponse> AddNoteAsync(Guid incidentId, string note, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string text = RequireNote(note, "note");
        Incident incident = await LoadAsync(incidentId, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        AddEvent(incident, IncidentEventKind.Note, adminId, text, now);
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToDetail(incident, now);
    }

    public async Task<IncidentDetailResponse> ReviewAsync(
        Guid incidentId, DateTimeOffset nextReviewAt, string note, string etag, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string text = RequireNote(note, "note");
        Incident incident = await LoadOpenAsync(incidentId, etag, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (nextReviewAt <= now || nextReviewAt > now + IncidentRules.MaxReviewInterval)
        {
            throw ValidationErrors.For("nextReviewAt", "Ngày xem lại tiếp theo phải trong vòng 7 ngày tới.");
        }
        incident.NextReviewAt = nextReviewAt;
        incident.UpdatedAt = now;
        AddEvent(incident, IncidentEventKind.Reviewed, adminId, text, now);
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToDetail(incident, now);
    }

    public async Task<IncidentDetailResponse> ResolveAsync(Guid incidentId, string note, string etag, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        string text = RequireNote(note, "note");
        Incident incident = await LoadAsync(incidentId, ct);
        // Gọi lặp trên sự cố đã đóng: trả trạng thái hiện tại.
        if (incident.Status == IncidentStatus.Resolved)
        {
            return OperationsMapper.ToDetail(incident, timeProvider.GetUtcNow());
        }
        if (!Etag.Matches(etag, incident.UpdatedAt))
        {
            throw Conflict();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = now;
        incident.ResolvedByUserId = adminId;
        incident.ResolutionNote = text;
        incident.UpdatedAt = now;
        AddEvent(incident, IncidentEventKind.Resolved, adminId, text, now);
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.IncidentResolved,
            AuditTargetType.Incident, incident.Id, null, AuditOutcome.Succeeded, text, now));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationsMapper.ToDetail(incident, now);
    }

    private async Task EnsureNotInvolvedAsync(Guid vaultId, Guid userId, CancellationToken ct)
    {
        Vault? vault = await vaults.FindByIdAsync(vaultId, ct);
        if (vault != null && vault.OwnerId == userId)
        {
            throw ValidationErrors.For("assigneeUserId", "Không giao cho chủ kho hoặc Executor của chính kho này.");
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vaultId, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                throw ValidationErrors.For("assigneeUserId", "Không giao cho chủ kho hoặc Executor của chính kho này.");
            }
        }
    }

    private async Task<Incident> LoadAsync(Guid incidentId, CancellationToken ct)
    {
        Incident? incident = await incidents.FindByIdAsync(incidentId, ct);
        if (incident == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.IncidentNotFound, "Không tìm thấy sự cố.");
        }
        return incident;
    }

    private async Task<Incident> LoadOpenAsync(Guid incidentId, string etag, CancellationToken ct)
    {
        Incident incident = await LoadAsync(incidentId, ct);
        if (incident.Status == IncidentStatus.Resolved)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.IncidentNotOpen, "Sự cố đã được đóng.");
        }
        if (!Etag.Matches(etag, incident.UpdatedAt))
        {
            throw Conflict();
        }
        return incident;
    }

    private void AddEvent(Incident incident, IncidentEventKind kind, Guid? actor, string? note, DateTimeOffset now)
    {
        IncidentEvent incidentEvent = new IncidentEvent();
        incidentEvent.IncidentId = incident.Id;
        incidentEvent.Kind = kind;
        incidentEvent.ActorUserId = actor;
        incidentEvent.Note = AuditRules.NormalizeReason(note);
        incidentEvent.At = now;
        incident.Events.Add(incidentEvent);
        incidents.AddEvent(incidentEvent);
    }

    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            // Không lộ sự tồn tại của khu vực Admin (SEC-04).
            throw new AppException(AppErrorType.NotFound, ErrorCode.IncidentNotFound, "Không tìm thấy sự cố.");
        }
        return userId;
    }

    private static string RequireNote(string? note, string field)
    {
        string? text = TextInput.NullIfBlank(note);
        if (text == null)
        {
            throw ValidationErrors.For(field, "Vui lòng ghi lý do hoặc ghi chú.");
        }
        if (text.Length > IncidentRules.MaxNoteLength)
        {
            throw ValidationErrors.For(field, "Tối đa 1000 ký tự.");
        }
        return text;
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string field) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToApiString(), value.Trim(), StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        throw ValidationErrors.For(field, "Giá trị lọc không hợp lệ.");
    }

    private static AppException Conflict()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
            "Sự cố vừa được cập nhật ở nơi khác. Vui lòng tải lại rồi thử lại.");
    }
}

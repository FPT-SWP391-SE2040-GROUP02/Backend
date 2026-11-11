using System.Text.Json;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Mẫu đã chọn cho một biên bản: khóa phiên bản ghi vào biên bản và nội dung của phiên bản đó.</summary>
public sealed class ResolvedHandoverTemplate
{
    public string VersionKey { get; set; } = string.Empty;

    public HandoverTemplateContent Content { get; set; } = new HandoverTemplateContent();
}

/// <summary>
/// Mẫu biên bản đồng ý và văn bản từ chối: Admin xem, sửa và xem lịch sử; các service biên bản lấy mẫu theo phiên bản.
/// Chưa có bản nào được lưu thì dùng mẫu gốc (<see cref="HandoverTemplateDefaults"/>), khóa phiên bản là HandoverDocumentRules.TemplateVersion.
/// </summary>
public interface IHandoverTemplateService
{
    /// <summary>Mẫu đang dùng cho biên bản mới, kèm danh sách trường và mẫu gốc. Không phải Admin đang hoạt động → 404.</summary>
    Task<AdminHandoverTemplateResponse> GetCurrentAsync(CancellationToken ct);

    /// <summary>Một phiên bản cũ theo số. Không có → 404.</summary>
    Task<AdminHandoverTemplateResponse> GetVersionAsync(int version, CancellationToken ct);

    /// <summary>Lịch sử các lần lưu, mới nhất trước (tối đa 50).</summary>
    Task<List<HandoverTemplateVersionResponse>> ListVersionsAsync(CancellationToken ct);

    /// <summary>Lưu một phiên bản mới và ghi nhật ký. Sai luật → 400 VALIDATION_ERROR. Biên bản đã tạo giữ nguyên phiên bản cũ.</summary>
    Task<AdminHandoverTemplateResponse> SaveAsync(SaveHandoverTemplateRequest request, CancellationToken ct);

    /// <summary>Khóa phiên bản đang dùng cho biên bản mới: "set-N" hoặc mã mẫu gốc.</summary>
    Task<string> ActiveVersionKeyAsync(CancellationToken ct);

    /// <summary>Mẫu theo khóa đã ghi trên biên bản; khóa lạ hoặc không còn → mẫu gốc.</summary>
    Task<ResolvedHandoverTemplate> ResolveAsync(string versionKey, CancellationToken ct);
}

public sealed class HandoverTemplateService : IHandoverTemplateService
{
    public const int MaxParagraphs = 60;
    public const int MaxParagraphLength = 2000;
    public const int MaxNoticeLength = 500;
    public const int MaxNoteLength = 300;
    private const string KeyPrefix = "set-";

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IHandoverTemplateRepository templates;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public HandoverTemplateService(
        ICurrentUser currentUser,
        IUserRepository users,
        IHandoverTemplateRepository templates,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.templates = templates;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<AdminHandoverTemplateResponse> GetCurrentAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        HandoverTemplateSet? latest = await templates.FindLatestAsync(ct);
        return await ToResponseAsync(latest, ct);
    }

    public async Task<AdminHandoverTemplateResponse> GetVersionAsync(int version, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        HandoverTemplateSet? set = await templates.FindByIdAsync(version, ct);
        if (set == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy phiên bản mẫu.");
        }
        return await ToResponseAsync(set, ct);
    }

    public async Task<List<HandoverTemplateVersionResponse>> ListVersionsAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        List<HandoverTemplateSet> sets = await templates.ListLatestAsync(50, ct);
        List<HandoverTemplateVersionResponse> list = new List<HandoverTemplateVersionResponse>();
        Dictionary<Guid, string> names = new Dictionary<Guid, string>();
        foreach (HandoverTemplateSet set in sets)
        {
            HandoverTemplateVersionResponse row = new HandoverTemplateVersionResponse();
            row.Version = set.Id;
            row.CreatedAt = set.CreatedAt;
            row.Note = set.Note;
            row.CreatedByName = await NameOfAsync(set.CreatedByUserId, names, ct);
            list.Add(row);
        }
        return list;
    }

    public async Task<AdminHandoverTemplateResponse> SaveAsync(SaveHandoverTemplateRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Validate(request);

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverTemplateSet set = new HandoverTemplateSet();
        set.AcceptJson = JsonSerializer.Serialize(Clean(request.Accept));
        set.DeclineJson = JsonSerializer.Serialize(Clean(request.Decline));
        set.DeclineNotice = request.DeclineNotice.Trim();
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            set.Note = request.Note.Trim();
        }
        set.CreatedByUserId = adminId;
        set.CreatedAt = now;
        templates.Add(set);
        // Số phiên bản chỉ có sau khi lưu nên nhật ký ghi ngắn gọn, không kèm số.
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemSettingUpdated,
            AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, "Lưu mẫu biên bản đồng ý và văn bản từ chối", now));
        await unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(set, ct);
    }

    public async Task<string> ActiveVersionKeyAsync(CancellationToken ct)
    {
        HandoverTemplateSet? latest = await templates.FindLatestAsync(ct);
        if (latest == null)
        {
            return HandoverDocumentRules.TemplateVersion;
        }
        return KeyPrefix + latest.Id;
    }

    public async Task<ResolvedHandoverTemplate> ResolveAsync(string versionKey, CancellationToken ct)
    {
        ResolvedHandoverTemplate resolved = new ResolvedHandoverTemplate();
        resolved.VersionKey = HandoverDocumentRules.TemplateVersion;
        resolved.Content = HandoverTemplateDefaults.Create();
        int version;
        if (versionKey.StartsWith(KeyPrefix, StringComparison.Ordinal)
            && int.TryParse(versionKey.Substring(KeyPrefix.Length), out version))
        {
            HandoverTemplateSet? set = await templates.FindByIdAsync(version, ct);
            if (set != null)
            {
                resolved.VersionKey = versionKey;
                resolved.Content = ToContent(set);
            }
        }
        return resolved;
    }

    private async Task<AdminHandoverTemplateResponse> ToResponseAsync(HandoverTemplateSet? set, CancellationToken ct)
    {
        AdminHandoverTemplateResponse response = new AdminHandoverTemplateResponse();
        HandoverTemplateContent content = HandoverTemplateDefaults.Create();
        response.IsDefault = set == null;
        if (set != null)
        {
            content = ToContent(set);
            response.Version = set.Id;
            response.Note = set.Note;
            response.CreatedAt = set.CreatedAt;
            response.CreatedByName = await NameOfAsync(set.CreatedByUserId, new Dictionary<Guid, string>(), ct);
        }
        response.Accept = content.Accept;
        response.Decline = content.Decline;
        response.DeclineNotice = content.DeclineNotice;
        response.Tokens = HandoverTemplateTokens.All();
        response.Default = HandoverTemplateDefaults.Create();
        return response;
    }

    private static HandoverTemplateContent ToContent(HandoverTemplateSet set)
    {
        HandoverTemplateContent content = new HandoverTemplateContent();
        content.Accept = JsonSerializer.Deserialize<List<TemplateParagraph>>(set.AcceptJson) ?? new List<TemplateParagraph>();
        content.Decline = JsonSerializer.Deserialize<List<TemplateParagraph>>(set.DeclineJson) ?? new List<TemplateParagraph>();
        content.DeclineNotice = set.DeclineNotice;
        return content;
    }

    private static List<TemplateParagraph> Clean(List<TemplateParagraph> paragraphs)
    {
        List<TemplateParagraph> list = new List<TemplateParagraph>();
        foreach (TemplateParagraph paragraph in paragraphs)
        {
            // Giữ khoảng trắng đầu dòng (thụt lề của mẫu gốc), chỉ bỏ khoảng trắng cuối và đổi xuống dòng thành dấu cách.
            string text = paragraph.Text.Replace("\r", " ").Replace("\n", " ").TrimEnd();
            list.Add(new TemplateParagraph(text, paragraph.Align, paragraph.Bold));
        }
        return list;
    }

    private static void Validate(SaveHandoverTemplateRequest request)
    {
        ValidateList(request.Accept, "accept", "Biên bản đồng ý");
        ValidateList(request.Decline, "decline", "Văn bản từ chối");
        string notice = (request.DeclineNotice ?? string.Empty).Trim();
        if (notice.Length == 0)
        {
            throw ValidationErrors.For("declineNotice", "Câu báo khi người nhận từ chối không được để trống.");
        }
        if (notice.Length > MaxNoticeLength)
        {
            throw ValidationErrors.For("declineNotice", "Câu báo tối đa " + MaxNoticeLength + " ký tự.");
        }
        if (request.Note != null && request.Note.Trim().Length > MaxNoteLength)
        {
            throw ValidationErrors.For("note", "Ghi chú tối đa " + MaxNoteLength + " ký tự.");
        }
    }

    private static void ValidateList(List<TemplateParagraph>? paragraphs, string field, string label)
    {
        if (paragraphs == null || paragraphs.Count == 0)
        {
            throw ValidationErrors.For(field, label + " phải có ít nhất một đoạn.");
        }
        if (paragraphs.Count > MaxParagraphs)
        {
            throw ValidationErrors.For(field, label + " tối đa " + MaxParagraphs + " đoạn.");
        }
        for (int i = 0; i < paragraphs.Count; i++)
        {
            TemplateParagraph paragraph = paragraphs[i];
            string where = label + ", đoạn " + (i + 1) + ": ";
            string text = (paragraph.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                throw ValidationErrors.For(field, where + "không được để trống (xóa đoạn nếu không cần).");
            }
            if (text.Length > MaxParagraphLength)
            {
                throw ValidationErrors.For(field, where + "tối đa " + MaxParagraphLength + " ký tự.");
            }
            if (paragraph.Align != TemplateParagraph.AlignLeft
                && paragraph.Align != TemplateParagraph.AlignCenter
                && paragraph.Align != TemplateParagraph.AlignJustify)
            {
                throw ValidationErrors.For(field, where + "căn lề phải là left, center hoặc justify.");
            }
            string? unknown = HandoverTemplateTokens.FindUnknown(text);
            if (unknown != null)
            {
                throw ValidationErrors.For(field, where + "trường {" + unknown + "} không có trong danh sách trường được chèn.");
            }
        }
    }

    private async Task<string?> NameOfAsync(Guid? userId, Dictionary<Guid, string> cache, CancellationToken ct)
    {
        if (userId == null)
        {
            return null;
        }
        string? name;
        if (cache.TryGetValue(userId.Value, out name))
        {
            return name;
        }
        User? user = await users.FindByIdAsync(userId.Value, ct);
        if (user == null)
        {
            return null;
        }
        cache[userId.Value] = user.FullName;
        return user.FullName;
    }

    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            // Không lộ sự tồn tại của khu vực Admin (SEC-04), cùng mã lỗi với AdminService.
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return userId;
    }
}

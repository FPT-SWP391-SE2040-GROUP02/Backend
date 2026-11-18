using System.Text.Json;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Mẫu biên bản của Admin: lưu phiên bản mới, đọc phiên bản đã lưu và tra mẫu theo khóa phiên bản.</summary>
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

    /// <summary>Admin xem mẫu đang áp dụng (bản mới nhất, hoặc bộ mẫu gốc khi chưa lưu bản nào).</summary>
    public async Task<AdminHandoverTemplateResponse> GetCurrentAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        HandoverTemplateSet? latest = await templates.FindLatestAsync(ct);
        return await ToResponseAsync(latest, ct);
    }

    /// <summary>Admin xem một phiên bản mẫu đã lưu; không có thì 404.</summary>
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

    /// <summary>Admin xem danh sách các phiên bản mẫu đã lưu, mới nhất trước, tối đa 50 bản.</summary>
    public async Task<List<HandoverTemplateVersionResponse>> ListVersionsAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        List<HandoverTemplateSet> sets = await templates.ListLatestAsync(50, ct);
        List<HandoverTemplateVersionResponse> list = new List<HandoverTemplateVersionResponse>();
        Dictionary<Guid, string> names = new Dictionary<Guid, string>();
        foreach (HandoverTemplateSet set in sets)
        {
            HandoverTemplateVersionResponse row = new HandoverTemplateVersionResponse
            {
                Version = set.Id,
                CreatedAt = set.CreatedAt,
                Note = set.Note,
                CreatedByName = await NameOfAsync(set.CreatedByUserId, names, ct)
            };
            list.Add(row);
        }
        return list;
    }

    /// <summary>Admin lưu một phiên bản mẫu mới. Phiên bản cũ giữ nguyên để biên bản đã tạo vẫn in đúng chữ lúc tạo.</summary>
    public async Task<AdminHandoverTemplateResponse> SaveAsync(SaveHandoverTemplateRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Validate(request);

        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverTemplateSet set = new HandoverTemplateSet
        {
            AcceptJson = JsonSerializer.Serialize(Clean(request.Accept)),
            DeclineJson = JsonSerializer.Serialize(Clean(request.Decline)),
            DeclineNotice = request.DeclineNotice.Trim()
        };
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

    /// <summary>Khóa phiên bản đang áp dụng, ghi vào biên bản mới tạo; chưa có bản nào thì là khóa của bộ mẫu gốc.</summary>
    public async Task<string> ActiveVersionKeyAsync(CancellationToken ct)
    {
        HandoverTemplateSet? latest = await templates.FindLatestAsync(ct);
        if (latest == null)
        {
            return HandoverDocumentRules.TemplateVersion;
        }
        return KeyPrefix + latest.Id;
    }

    /// <summary>Đọc nội dung mẫu theo khóa phiên bản; khóa lạ hoặc bản không còn thì dùng bộ mẫu gốc.</summary>
    public async Task<ResolvedHandoverTemplate> ResolveAsync(string versionKey, CancellationToken ct)
    {
        ResolvedHandoverTemplate resolved = new ResolvedHandoverTemplate
        {
            VersionKey = HandoverDocumentRules.TemplateVersion,
            Content = HandoverTemplateDefaults.Create()
        };
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

    /// <summary>Đổi một bản mẫu đã lưu (hoặc bộ mẫu gốc khi set là null) sang dữ liệu trả cho màn Admin, kèm tên người lưu.</summary>
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

    /// <summary>Đọc hai cột JSON của bản mẫu thành danh sách đoạn chữ.</summary>
    private static HandoverTemplateContent ToContent(HandoverTemplateSet set)
    {
        HandoverTemplateContent content = new HandoverTemplateContent();
        List<TemplateParagraph>? accept = JsonSerializer.Deserialize<List<TemplateParagraph>>(set.AcceptJson);
        if (accept == null)
        {
            accept = new List<TemplateParagraph>();
        }
        List<TemplateParagraph>? decline = JsonSerializer.Deserialize<List<TemplateParagraph>>(set.DeclineJson);
        if (decline == null)
        {
            decline = new List<TemplateParagraph>();
        }
        content.Accept = accept;
        content.Decline = decline;
        content.DeclineNotice = set.DeclineNotice;
        return content;
    }

    /// <summary>Làm sạch chữ Admin nhập: đổi xuống dòng thành dấu cách, bỏ khoảng trắng cuối dòng.</summary>
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

    /// <summary>Kiểm tra bản mẫu Admin gửi lên: có đoạn chữ, có câu báo từ chối, chỉ dùng trường {...} có trong danh sách.</summary>
    private static void Validate(SaveHandoverTemplateRequest request)
    {
        ValidateList(request.Accept, "accept", "Biên bản đồng ý");
        ValidateList(request.Decline, "decline", "Văn bản từ chối");
        string notice = string.Empty;
        if (request.DeclineNotice != null)
        {
            notice = request.DeclineNotice.Trim();
        }
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

    /// <summary>Kiểm tra một danh sách đoạn chữ: không rỗng, không quá số đoạn và độ dài cho phép, không có trường lạ.</summary>
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
            string text = string.Empty;
            if (paragraph.Text != null)
            {
                text = paragraph.Text.Trim();
            }
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

    /// <summary>Họ tên người lưu bản mẫu; tra một lần rồi nhớ trong cache.</summary>
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

    /// <summary>Người đang đăng nhập phải là Admin còn hoạt động; không thì trả 404 như tài khoản không tồn tại.</summary>
    private async Task<Guid> RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            // Không lộ sự tồn tại của khu vực Admin, cùng mã lỗi với AdminService.
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return userId;
    }
}

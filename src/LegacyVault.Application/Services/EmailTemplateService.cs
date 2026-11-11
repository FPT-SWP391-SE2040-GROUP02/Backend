using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Admin xem và sửa chữ của từng loại thư thông báo, và bật hoặc tắt loại thư cho phép tắt. Chưa sửa thì thư dùng chữ gốc trong mã.
/// Sau mỗi lần lưu, bản chụp dùng khi dựng thư (<see cref="EmailTemplateOverrides"/>) được nạp lại ngay.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>Mọi loại thư kèm chữ gốc và chữ đã sửa. Không phải Admin đang hoạt động thì 404.</summary>
    Task<List<EmailTemplateResponse>> ListAsync(CancellationToken ct);

    /// <summary>Lưu chữ cho một loại thư. Khóa lạ thì 404; chữ quá dài hoặc biến lạ thì 400.</summary>
    Task<EmailTemplateResponse> SaveAsync(string key, SaveEmailTemplateRequest request, CancellationToken ct);

    /// <summary>Bỏ chữ đã sửa, về chữ gốc và bật lại.</summary>
    Task<EmailTemplateResponse> ResetAsync(string key, CancellationToken ct);

    /// <summary>Nạp lại bản chụp từ DB (lúc khởi động và định kỳ), không cần quyền Admin.</summary>
    Task RefreshSnapshotAsync(CancellationToken ct);
}

public sealed class EmailTemplateService : IEmailTemplateService
{
    public const int MaxSubjectLength = 200;
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 4000;
    public const int MaxButtonLength = 80;
    public const int MaxNoteLength = 1000;

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IEmailTemplateRepository templates;
    private readonly IEmailTemplateDefaults defaults;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public EmailTemplateService(
        ICurrentUser currentUser,
        IUserRepository users,
        IEmailTemplateRepository templates,
        IEmailTemplateDefaults defaults,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.templates = templates;
        this.defaults = defaults;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<List<EmailTemplateResponse>> ListAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        List<EmailTemplate> saved = await templates.ListAsync(ct);
        Dictionary<Guid, string> names = await NamesAsync(saved, ct);
        List<EmailTemplateResponse> list = new List<EmailTemplateResponse>();
        foreach (EmailTemplateDefinition definition in EmailTemplateCatalog.All)
        {
            EmailTemplate? row = saved.FirstOrDefault(t => t.Key == definition.Key);
            list.Add(ToResponse(definition, row, names));
        }
        return list;
    }

    public async Task<EmailTemplateResponse> SaveAsync(string key, SaveEmailTemplateRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        EmailTemplateDefinition definition = RequireDefinition(key);
        string? subject = Clean(request.Subject);
        string? title = Clean(request.Title);
        string? body = Clean(request.Body);
        string? button = Clean(request.ButtonText);
        string? note = Clean(request.Note);
        if (!definition.EditableBody)
        {
            body = null;
        }
        Validate("subject", subject, MaxSubjectLength, definition);
        Validate("title", title, MaxTitleLength, definition);
        Validate("body", body, MaxBodyLength, definition);
        CheckRequiredBodyTokens(definition, body);
        Validate("buttonText", button, MaxButtonLength, definition);
        Validate("note", note, MaxNoteLength, definition);

        bool enabled = request.Enabled || !definition.CanDisable;
        DateTimeOffset now = timeProvider.GetUtcNow();
        EmailTemplate? row = await templates.FindAsync(key, ct);
        if (row == null)
        {
            row = new EmailTemplate();
            row.Key = key;
            templates.Add(row);
        }
        row.Enabled = enabled;
        row.Subject = subject;
        row.Title = title;
        row.Body = body;
        row.ButtonText = button;
        row.Note = note;
        row.UpdatedByUserId = adminId;
        row.UpdatedAt = now;
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemSettingUpdated,
            AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, "Sửa mẫu thư: " + definition.Label, now));
        await unitOfWork.SaveChangesAsync(ct);
        await RefreshSnapshotAsync(ct);
        return await OneAsync(definition, ct);
    }

    public async Task<EmailTemplateResponse> ResetAsync(string key, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        EmailTemplateDefinition definition = RequireDefinition(key);
        EmailTemplate? row = await templates.FindAsync(key, ct);
        if (row != null)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            templates.Remove(row);
            audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemSettingUpdated,
                AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, "Đưa mẫu thư về chữ gốc: " + definition.Label, now));
            await unitOfWork.SaveChangesAsync(ct);
            await RefreshSnapshotAsync(ct);
        }
        return await OneAsync(definition, ct);
    }

    public async Task RefreshSnapshotAsync(CancellationToken ct)
    {
        List<EmailTemplate> saved = await templates.ListAsync(ct);
        Dictionary<string, EmailTemplateOverride> next = new Dictionary<string, EmailTemplateOverride>();
        foreach (EmailTemplate row in saved)
        {
            next[row.Key] = new EmailTemplateOverride(row.Enabled, row.Subject, row.Title, row.Body, row.ButtonText, row.Note);
        }
        EmailTemplateOverrides.Replace(next);
    }

    private async Task<EmailTemplateResponse> OneAsync(EmailTemplateDefinition definition, CancellationToken ct)
    {
        List<EmailTemplate> saved = await templates.ListAsync(ct);
        Dictionary<Guid, string> names = await NamesAsync(saved, ct);
        return ToResponse(definition, saved.FirstOrDefault(t => t.Key == definition.Key), names);
    }

    private EmailTemplateResponse ToResponse(EmailTemplateDefinition definition, EmailTemplate? row, Dictionary<Guid, string> names)
    {
        EmailTemplateResponse response = new EmailTemplateResponse();
        response.Key = definition.Key;
        response.Label = definition.Label;
        response.Description = definition.Description;
        response.CanDisable = definition.CanDisable;
        response.EditableBody = definition.EditableBody;
        if (definition.RequiredBodyTokens != null)
        {
            response.RequiredBodyTokens.AddRange(definition.RequiredBodyTokens);
        }
        foreach (EmailTemplateToken token in definition.Tokens)
        {
            EmailTemplateTokenResponse item = new EmailTemplateTokenResponse();
            item.Name = token.Name;
            item.Description = token.Description;
            response.Tokens.Add(item);
        }
        EmailParts? original = defaults.Find(definition.Key);
        if (original != null)
        {
            response.Default.Subject = original.Subject;
            response.Default.Title = original.Title;
            response.Default.Body = original.Body;
            response.Default.ButtonText = original.ButtonText;
            response.Default.Note = original.Note;
        }
        if (row != null)
        {
            response.Enabled = row.Enabled;
            response.Custom.Subject = row.Subject;
            response.Custom.Title = row.Title;
            response.Custom.Body = row.Body;
            response.Custom.ButtonText = row.ButtonText;
            response.Custom.Note = row.Note;
            response.UpdatedAt = row.UpdatedAt;
            if (row.UpdatedByUserId != null && names.ContainsKey(row.UpdatedByUserId.Value))
            {
                response.UpdatedByName = names[row.UpdatedByUserId.Value];
            }
        }
        return response;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(List<EmailTemplate> saved, CancellationToken ct)
    {
        Dictionary<Guid, string> names = new Dictionary<Guid, string>();
        foreach (Guid id in saved.Where(t => t.UpdatedByUserId != null).Select(t => t.UpdatedByUserId!.Value).Distinct())
        {
            User? user = await users.FindByIdAsync(id, ct);
            if (user != null)
            {
                names[id] = user.FullName;
            }
        }
        return names;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.Replace("\r\n", "\n").Trim();
    }

    /// <summary>Nội dung Admin sửa phải còn các biến bắt buộc của loại thư; không sửa nội dung (null) thì bỏ qua.</summary>
    public static void CheckRequiredBodyTokens(EmailTemplateDefinition definition, string? body)
    {
        if (body == null || definition.RequiredBodyTokens == null)
        {
            return;
        }
        foreach (string required in definition.RequiredBodyTokens)
        {
            if (!body.Contains("{" + required + "}", StringComparison.Ordinal))
            {
                throw ValidationErrors.For("body", "Nội dung phải giữ biến {" + required + "}.");
            }
        }
    }

    private static void Validate(string field, string? value, int max, EmailTemplateDefinition definition)
    {
        if (value == null)
        {
            return;
        }
        if (value.Length > max)
        {
            throw ValidationErrors.For(field, "Tối đa " + max + " ký tự.");
        }
        int start = value.IndexOf('{');
        while (start >= 0)
        {
            int end = value.IndexOf('}', start + 1);
            if (end < 0)
            {
                throw ValidationErrors.For(field, "Có dấu { chưa đóng bằng }.");
            }
            string name = value.Substring(start + 1, end - start - 1);
            if (!definition.Tokens.Any(t => t.Name == name))
            {
                throw ValidationErrors.For(field, "Biến {" + name + "} không có trong loại thư này.");
            }
            start = value.IndexOf('{', end + 1);
        }
        if (CountOf(value, '}') != CountOf(value, '{'))
        {
            throw ValidationErrors.For(field, "Dấu { } không khớp nhau.");
        }
    }

    private static int CountOf(string value, char c)
    {
        int count = 0;
        foreach (char ch in value)
        {
            if (ch == c)
            {
                count++;
            }
        }
        return count;
    }

    private static EmailTemplateDefinition RequireDefinition(string key)
    {
        EmailTemplateDefinition? definition = EmailTemplateCatalog.Find(key);
        if (definition == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy loại thư.");
        }
        return definition;
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

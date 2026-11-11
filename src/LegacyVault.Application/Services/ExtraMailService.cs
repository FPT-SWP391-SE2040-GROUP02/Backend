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
/// Admin thêm, sửa, bật tắt và xóa các thư bổ sung gửi cho Chủ kho theo ngày tính từ hạn điểm danh (trước hạn hoặc quá hạn).
/// Mỗi thư gửi tối đa một lần cho mỗi kỳ; thư của kỳ đã gửi không bị gửi lại khi Admin sửa chữ.
/// </summary>
public interface IExtraMailService
{
    Task<ExtraMailListResponse> ListAsync(CancellationToken ct);

    Task<ExtraMailResponse> CreateAsync(SaveExtraMailRequest request, CancellationToken ct);

    Task<ExtraMailResponse> UpdateAsync(Guid id, SaveExtraMailRequest request, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class ExtraMailService : IExtraMailService
{
    public const int MaxItems = 20;
    public const int MinDaysFromDue = -60;
    public const int MaxDaysFromDue = 30;
    public const int MaxNameLength = 100;

    /// <summary>Biến dùng được trong thư bổ sung.</summary>
    public static readonly IReadOnlyList<EmailTemplateToken> Tokens = new List<EmailTemplateToken>
    {
        new EmailTemplateToken("TEN_NGUOI_NHAN", "Họ tên Chủ kho nhận thư"),
        new EmailTemplateToken("HAN_DIEM_DANH", "Hạn điểm danh của kỳ, ví dụ 17:00 ngày 20/11/2026"),
        new EmailTemplateToken("HET_THOI_GIAN_CHO", "Lúc hết thời gian chờ, sau đó hệ thống nhờ Người thực thi liên hệ"),
    };

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IExtraMailRepository rules;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ExtraMailService(
        ICurrentUser currentUser,
        IUserRepository users,
        IExtraMailRepository rules,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.rules = rules;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ExtraMailListResponse> ListAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        List<ExtraMailRule> all = await rules.ListAsync(ct);
        ExtraMailListResponse response = new ExtraMailListResponse();
        response.MaxItems = MaxItems;
        response.MinDaysFromDue = MinDaysFromDue;
        response.MaxDaysFromDue = MaxDaysFromDue;
        foreach (EmailTemplateToken token in Tokens)
        {
            EmailTemplateTokenResponse item = new EmailTemplateTokenResponse();
            item.Name = token.Name;
            item.Description = token.Description;
            response.Tokens.Add(item);
        }
        foreach (ExtraMailRule rule in all)
        {
            response.Items.Add(await ToResponseAsync(rule, ct));
        }
        return response;
    }

    public async Task<ExtraMailResponse> CreateAsync(SaveExtraMailRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Validate(request);
        if (await rules.CountAsync(ct) >= MaxItems)
        {
            throw ValidationErrors.For("name", "Tối đa " + MaxItems + " thư bổ sung.");
        }
        DateTimeOffset now = timeProvider.GetUtcNow();
        ExtraMailRule rule = new ExtraMailRule();
        rule.CreatedAt = now;
        Fill(rule, request, adminId, now);
        rules.Add(rule);
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemSettingUpdated,
            AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, "Thêm thư bổ sung: " + rule.Name, now));
        await unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(rule, ct);
    }

    public async Task<ExtraMailResponse> UpdateAsync(Guid id, SaveExtraMailRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        Validate(request);
        ExtraMailRule rule = await RequireRuleAsync(id, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        Fill(rule, request, adminId, now);
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemSettingUpdated,
            AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, "Sửa thư bổ sung: " + rule.Name, now));
        await unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(rule, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        ExtraMailRule rule = await RequireRuleAsync(id, ct);
        DateTimeOffset now = timeProvider.GetUtcNow();
        rules.Remove(rule);
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemSettingUpdated,
            AuditTargetType.Configuration, null, null, AuditOutcome.Succeeded, "Xóa thư bổ sung: " + rule.Name, now));
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static void Fill(ExtraMailRule rule, SaveExtraMailRequest request, Guid adminId, DateTimeOffset now)
    {
        rule.Name = request.Name.Trim();
        rule.Enabled = request.Enabled;
        rule.DaysFromDue = request.DaysFromDue;
        rule.Subject = request.Subject.Trim();
        rule.Title = request.Title.Trim();
        rule.Body = request.Body.Replace("\r\n", "\n").Trim();
        rule.ButtonText = request.ButtonText.Trim();
        rule.Note = request.Note.Trim();
        rule.UpdatedByUserId = adminId;
        rule.UpdatedAt = now;
    }

    private static void Validate(SaveExtraMailRequest request)
    {
        Required("name", request.Name, MaxNameLength);
        Required("subject", request.Subject, 200);
        Required("title", request.Title, 200);
        Required("body", request.Body, 4000);
        Required("buttonText", request.ButtonText, 80);
        Required("note", request.Note, 1000);
        if (request.DaysFromDue < MinDaysFromDue || request.DaysFromDue > MaxDaysFromDue)
        {
            throw ValidationErrors.For("daysFromDue", "Số ngày từ " + MinDaysFromDue + " đến " + MaxDaysFromDue + " (âm là trước hạn).");
        }
        foreach (string field in new[] { request.Subject, request.Title, request.Body, request.ButtonText, request.Note })
        {
            CheckTokens(field);
        }
    }

    private static void Required(string field, string value, int max)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw ValidationErrors.For(field, "Không được để trống.");
        }
        if (trimmed.Length > max)
        {
            throw ValidationErrors.For(field, "Tối đa " + max + " ký tự.");
        }
    }

    private static void CheckTokens(string value)
    {
        int start = value.IndexOf('{');
        while (start >= 0)
        {
            int end = value.IndexOf('}', start + 1);
            if (end < 0)
            {
                throw ValidationErrors.For("body", "Có dấu { chưa đóng bằng }.");
            }
            string name = value.Substring(start + 1, end - start - 1);
            if (!Tokens.Any(t => t.Name == name))
            {
                throw ValidationErrors.For("body", "Biến {" + name + "} không dùng được trong thư bổ sung.");
            }
            start = value.IndexOf('{', end + 1);
        }
        if (value.Count(c => c == '}') != value.Count(c => c == '{'))
        {
            throw ValidationErrors.For("body", "Dấu { } không khớp nhau.");
        }
    }

    private async Task<ExtraMailRule> RequireRuleAsync(Guid id, CancellationToken ct)
    {
        ExtraMailRule? rule = await rules.FindAsync(id, ct);
        if (rule == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy thư bổ sung.");
        }
        return rule;
    }

    private async Task<ExtraMailResponse> ToResponseAsync(ExtraMailRule rule, CancellationToken ct)
    {
        ExtraMailResponse response = new ExtraMailResponse();
        response.Id = rule.Id;
        response.Name = rule.Name;
        response.Enabled = rule.Enabled;
        response.DaysFromDue = rule.DaysFromDue;
        response.Subject = rule.Subject;
        response.Title = rule.Title;
        response.Body = rule.Body;
        response.ButtonText = rule.ButtonText;
        response.Note = rule.Note;
        response.UpdatedAt = rule.UpdatedAt;
        if (rule.UpdatedByUserId != null)
        {
            User? user = await users.FindByIdAsync(rule.UpdatedByUserId.Value, ct);
            if (user != null)
            {
                response.UpdatedByName = user.FullName;
            }
        }
        return response;
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

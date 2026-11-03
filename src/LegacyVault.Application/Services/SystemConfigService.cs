using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Quản trị cấu hình dịch vụ ngoài (model, khóa, dịch vụ dự phòng) trên web Admin.</summary>
public interface ISystemConfigService
{
    Task<SystemConfigListResponse> ListAsync(CancellationToken ct);

    Task<SystemConfigSlotResponse> UpdateAsync(
        string serviceKey, string slot, UpdateSystemConfigRequest request, CancellationToken ct);

    Task ResetAsync(string serviceKey, string slot, ResetSystemConfigRequest request, CancellationToken ct);

    /// <summary>Thử kết nối bằng giá trị Admin đang nhập (khóa để trống thì dùng khóa đang lưu). Không lưu gì.</summary>
    Task<TestSystemConfigResponse> TestAsync(
        string serviceKey, string slot, TestSystemConfigRequest request, CancellationToken ct);
}

/// <remarks>
/// - Mọi thao tác ghi AuditLogs trong cùng giao dịch, chỉ ghi TÊN trường đã đổi, không ghi giá trị (SEC-07).
/// - Khóa bí mật chỉ lưu mã hóa và không bao giờ nằm trong response.
/// - Không phải Admin đang hoạt động → 404 như khu vực không tồn tại (SEC-04).
/// </remarks>
public sealed class SystemConfigService : ISystemConfigService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly ISystemConfigRepository configs;
    private readonly ISystemConfigSecretProtector protector;
    private readonly ISystemConfigResolver resolver;
    private readonly ISystemConfigDefaults defaults;
    private readonly IServiceStatusTracker tracker;
    private readonly IEnumerable<IServiceConnectionTester> testers;
    private readonly IAuditLogRepository audit;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public SystemConfigService(
        ICurrentUser currentUser,
        IUserRepository users,
        ISystemConfigRepository configs,
        ISystemConfigSecretProtector protector,
        ISystemConfigResolver resolver,
        ISystemConfigDefaults defaults,
        IServiceStatusTracker tracker,
        IEnumerable<IServiceConnectionTester> testers,
        IAuditLogRepository audit,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.testers = testers;
        this.currentUser = currentUser;
        this.users = users;
        this.configs = configs;
        this.protector = protector;
        this.resolver = resolver;
        this.defaults = defaults;
        this.tracker = tracker;
        this.audit = audit;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<SystemConfigListResponse> ListAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        List<SystemConfig> rows = await configs.ListAsync(ct);

        SystemConfigListResponse response = new SystemConfigListResponse();
        foreach (string serviceKey in SystemConfigCatalog.WiredServices())
        {
            SystemConfigServiceResponse service = new SystemConfigServiceResponse();
            service.ServiceKey = serviceKey;
            service.SupportsFallback = SystemConfigCatalog.SupportsFallback(serviceKey);
            service.Slots.Add(await BuildSlotAsync(serviceKey, SystemConfigSlot.Primary, FindRow(rows, serviceKey, SystemConfigSlot.Primary), ct));
            if (service.SupportsFallback)
            {
                service.Slots.Add(await BuildSlotAsync(serviceKey, SystemConfigSlot.Fallback, FindRow(rows, serviceKey, SystemConfigSlot.Fallback), ct));
            }
            service.Status = BuildStatus(serviceKey);
            response.Services.Add(service);
        }
        return response;
    }

    public async Task<SystemConfigSlotResponse> UpdateAsync(
        string serviceKey, string slotText, UpdateSystemConfigRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        SystemConfigSlot slot = ParseTarget(serviceKey, slotText);
        string reason = SystemConfigValidator.RequireReason(request.Reason);

        SystemConfigValidator.ValidateSettings(serviceKey, request.Settings);
        Dictionary<string, string?> rawSecrets = request.Secrets ?? new Dictionary<string, string?>();
        SystemConfigValidator.ValidateSecrets(serviceKey, rawSecrets);
        if (slot == SystemConfigSlot.Primary && !request.Enabled)
        {
            throw ValidationErrors.For("enabled", "Ô chính luôn bật, chỉ tắt được ô dự phòng.");
        }
        bool enabled = slot == SystemConfigSlot.Primary || request.Enabled;

        SystemConfig? row = await configs.FindAsync(serviceKey, slot, ct);
        int currentVersion = 0;
        if (row != null)
        {
            currentVersion = row.Version;
        }
        if (request.Version != currentVersion)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Cấu hình vừa được sửa ở nơi khác. Tải lại rồi sửa tiếp.");
        }

        Draft draft = BuildDraft(serviceKey, slot, row, request.Settings, rawSecrets);
        if (SystemConfigCatalog.RequiresConfirm(serviceKey, CurrentSettings(serviceKey, slot, row), draft.Settings)
            && !string.Equals((request.ConfirmText ?? string.Empty).Trim(), serviceKey, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationErrors.For("confirmText", $"Gõ lại {serviceKey} để xác nhận thay đổi này.");
        }
        Dictionary<string, string> settings = draft.Settings;
        Dictionary<string, string> secrets = draft.Secrets;
        List<string> changed = draft.Changed;
        if (slot == SystemConfigSlot.Fallback && (row == null || row.Enabled != enabled) && (row != null || enabled))
        {
            changed.Add("enabled");
        }
        if (slot == SystemConfigSlot.Primary || enabled)
        {
            RequireSecrets(serviceKey, secrets);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (row == null)
        {
            row = new SystemConfig();
            row.ServiceKey = serviceKey;
            row.Slot = slot;
            configs.Add(row);
        }
        row.Enabled = enabled;
        row.SettingsJson = SystemConfigJson.WriteSettings(settings);
        protector.Protect(row, secrets);
        row.Version = currentVersion + 1;
        row.UpdatedAt = now;
        row.UpdatedByUserId = adminId;
        row.UpdateReason = reason;

        string what = "không đổi trường nào";
        if (changed.Count > 0)
        {
            what = "đổi " + string.Join(", ", changed);
        }
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemConfigUpdated,
            AuditTargetType.Configuration, row.Id, row.Version, AuditOutcome.Succeeded,
            $"{serviceKey}/{slot.ToApiString()}: {what}. Lý do: {reason}", now));
        await unitOfWork.SaveChangesAsync(ct);
        resolver.Invalidate(serviceKey);

        return await BuildSlotAsync(serviceKey, slot, row, ct);
    }

    public async Task ResetAsync(string serviceKey, string slotText, ResetSystemConfigRequest request, CancellationToken ct)
    {
        Guid adminId = await RequireAdminAsync(ct);
        SystemConfigSlot slot = ParseTarget(serviceKey, slotText);
        string reason = SystemConfigValidator.RequireReason(request.Reason);

        SystemConfig? row = await configs.FindAsync(serviceKey, slot, ct);
        if (row == null)
        {
            // Đã là cấu hình mặc định: khôi phục lặp lại không phải lỗi và không ghi nhật ký.
            return;
        }
        configs.Remove(row);
        audit.Add(AuditRules.Create(null, adminId, AuditActorRole.Admin, AuditAction.SystemConfigReset,
            AuditTargetType.Configuration, row.Id, row.Version, AuditOutcome.Succeeded,
            $"{serviceKey}/{slot.ToApiString()}: khôi phục về cấu hình mặc định. Lý do: {reason}",
            timeProvider.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(ct);
        resolver.Invalidate(serviceKey);
    }

    public async Task<TestSystemConfigResponse> TestAsync(
        string serviceKey, string slotText, TestSystemConfigRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        SystemConfigSlot slot = ParseTarget(serviceKey, slotText);
        SystemConfigValidator.ValidateSettings(serviceKey, request.Settings);
        Dictionary<string, string?> rawSecrets = request.Secrets ?? new Dictionary<string, string?>();
        SystemConfigValidator.ValidateSecrets(serviceKey, rawSecrets);

        IServiceConnectionTester? tester = FindTester(serviceKey);
        if (tester == null)
        {
            throw ValidationErrors.For("serviceKey", "Dịch vụ này chưa hỗ trợ thử kết nối.");
        }

        // Thử giá trị Admin đang nhập; khóa để trống thì dùng khóa đang lưu. Không ghi DB, không ghi nhật ký.
        SystemConfig? row = await configs.FindAsync(serviceKey, slot, ct);
        Draft draft = BuildDraft(serviceKey, slot, row, request.Settings, rawSecrets);
        RequireSecrets(serviceKey, draft.Secrets);

        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.Database;
        config.Settings = draft.Settings;
        config.Secrets = draft.Secrets;
        return await tester.TestAsync(config, ct);
    }

    /// <summary>Giá trị Admin gửi lên gộp với giá trị đang có: thường (đã bỏ khoảng trắng), bí mật, và tên trường thật sự đổi.</summary>
    private sealed class Draft
    {
        public Dictionary<string, string> Settings { get; } = new Dictionary<string, string>();

        public Dictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();

        public List<string> Changed { get; } = new List<string>();
    }

    private Draft BuildDraft(
        string serviceKey, SystemConfigSlot slot, SystemConfig? row,
        IReadOnlyDictionary<string, string?> rawSettings, IReadOnlyDictionary<string, string?> rawSecrets)
    {
        Draft draft = new Draft();
        Dictionary<string, string> before = CurrentSettings(serviceKey, slot, row);
        draft.Secrets = CurrentSecrets(serviceKey, slot, row);
        foreach (ConfigFieldSpec field in SystemConfigCatalog.Fields(serviceKey))
        {
            if (field.Secret)
            {
                string provided = Trimmed(rawSecrets, field.Name);
                if (provided.Length > 0)
                {
                    draft.Secrets[field.Name] = provided;
                    draft.Changed.Add(field.Name + " (khóa)");
                }
                continue;
            }
            string value = Trimmed(rawSettings, field.Name);
            draft.Settings[field.Name] = value;
            string? old = null;
            if (!before.TryGetValue(field.Name, out old) || old != value)
            {
                draft.Changed.Add(field.Name);
            }
        }
        return draft;
    }

    /// <summary>Mọi khóa bắt buộc của dịch vụ phải có giá trị (nhập mới hoặc đã lưu), nếu không báo 400 ở secrets.&lt;tên&gt;.</summary>
    private static void RequireSecrets(string serviceKey, Dictionary<string, string> secrets)
    {
        foreach (ConfigFieldSpec field in SystemConfigCatalog.Fields(serviceKey))
        {
            if (field.Secret && field.Required && !secrets.ContainsKey(field.Name))
            {
                throw ValidationErrors.For("secrets." + field.Name, "Vui lòng nhập khóa.");
            }
        }
    }

    private IServiceConnectionTester? FindTester(string serviceKey)
    {
        foreach (IServiceConnectionTester tester in testers)
        {
            if (tester.ServiceKey == serviceKey)
            {
                return tester;
            }
        }
        return null;
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

    /// <summary>Dịch vụ phải đã nối và ô phải tồn tại cho dịch vụ đó; sai thì 400.</summary>
    private static SystemConfigSlot ParseTarget(string serviceKey, string slotText)
    {
        if (!SystemConfigCatalog.IsWired(serviceKey))
        {
            throw ValidationErrors.For("serviceKey", "Dịch vụ không được hỗ trợ.");
        }
        foreach (SystemConfigSlot candidate in Enum.GetValues<SystemConfigSlot>())
        {
            if (string.Equals(candidate.ToApiString(), slotText, StringComparison.Ordinal))
            {
                if (candidate == SystemConfigSlot.Fallback && !SystemConfigCatalog.SupportsFallback(serviceKey))
                {
                    throw ValidationErrors.For("slot", "Dịch vụ này không có ô dự phòng.");
                }
                return candidate;
            }
        }
        throw ValidationErrors.For("slot", "Ô phải là PRIMARY hoặc FALLBACK.");
    }

    private static SystemConfig? FindRow(List<SystemConfig> rows, string serviceKey, SystemConfigSlot slot)
    {
        foreach (SystemConfig row in rows)
        {
            if (row.ServiceKey == serviceKey && row.Slot == slot)
            {
                return row;
            }
        }
        return null;
    }

    private static string Trimmed(IReadOnlyDictionary<string, string?> values, string name)
    {
        string? raw = null;
        values.TryGetValue(name, out raw);
        return (raw ?? string.Empty).Trim();
    }

    /// <summary>Giá trị thường đang có hiệu lực của ô, dùng để biết trường nào thật sự đổi.</summary>
    private Dictionary<string, string> CurrentSettings(string serviceKey, SystemConfigSlot slot, SystemConfig? row)
    {
        if (row != null)
        {
            return SystemConfigJson.ReadSettings(serviceKey, row.SettingsJson);
        }
        if (slot == SystemConfigSlot.Primary)
        {
            ResolvedConfig? fromAppSettings = defaults.ReadPrimary(serviceKey);
            if (fromAppSettings != null)
            {
                return fromAppSettings.Settings;
            }
        }
        return SystemConfigCatalog.DefaultSettings(serviceKey);
    }

    /// <summary>
    /// Khóa bí mật hiện có (bản sao để sửa): từ dòng DB; ô chính chưa có dòng thì lấy từ appsettings để lần lưu đầu
    /// không làm mất khóa đang dùng; ô dự phòng chưa có dòng thì rỗng.
    /// </summary>
    private Dictionary<string, string> CurrentSecrets(string serviceKey, SystemConfigSlot slot, SystemConfig? row)
    {
        if (row != null)
        {
            return UnprotectOrEmpty(row);
        }
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        if (slot == SystemConfigSlot.Primary)
        {
            ResolvedConfig? fromAppSettings = defaults.ReadPrimary(serviceKey);
            if (fromAppSettings != null)
            {
                foreach (KeyValuePair<string, string> entry in fromAppSettings.Secrets)
                {
                    secrets[entry.Key] = entry.Value;
                }
            }
        }
        return secrets;
    }

    /// <summary>
    /// Khóa đã lưu của dòng. Không giải mã được (khóa mã hóa KEK cũ đã bị bỏ, dữ liệu hỏng) thì coi như chưa có khóa:
    /// trang vẫn mở được và Admin nhập lại khóa mới để sửa, thay vì cả trang báo lỗi.
    /// </summary>
    private Dictionary<string, string> UnprotectOrEmpty(SystemConfig row)
    {
        try
        {
            return protector.Unprotect(row);
        }
        catch (CryptographicException)
        {
            return new Dictionary<string, string>();
        }
    }

    private async Task<SystemConfigSlotResponse> BuildSlotAsync(
        string serviceKey, SystemConfigSlot slot, SystemConfig? row, CancellationToken ct)
    {
        SystemConfigSlotResponse response = new SystemConfigSlotResponse();
        response.Slot = slot.ToApiString();
        Dictionary<string, string> secrets = new Dictionary<string, string>();
        if (row != null)
        {
            response.Enabled = row.Enabled;
            response.Source = ConfigSources.Database;
            response.Settings = SystemConfigJson.ReadSettings(serviceKey, row.SettingsJson);
            secrets = UnprotectOrEmpty(row);
            response.Version = row.Version;
            response.UpdatedAt = row.UpdatedAt;
            response.UpdatedBy = await FindUserNameAsync(row.UpdatedByUserId, ct);
        }
        else
        {
            response.Enabled = slot == SystemConfigSlot.Primary;
            response.Source = ConfigSources.AppSettings;
            response.Settings = CurrentSettings(serviceKey, slot, null);
            secrets = CurrentSecrets(serviceKey, slot, null);
        }

        foreach (ConfigFieldSpec field in SystemConfigCatalog.Fields(serviceKey))
        {
            if (!field.Secret)
            {
                continue;
            }
            SystemConfigSecretResponse secret = new SystemConfigSecretResponse();
            secret.Name = field.Name;
            string? value = null;
            secret.HasValue = secrets.TryGetValue(field.Name, out value);
            // Chỉ hiện 4 ký tự cuối của khóa đã lưu trong DB và đủ dài; khóa từ appsettings không bao giờ lộ gì.
            if (row != null && value != null && value.Length >= 8)
            {
                secret.Last4 = value.Substring(value.Length - 4);
            }
            response.Secrets.Add(secret);
        }
        return response;
    }

    private ServiceStatusResponse BuildStatus(string serviceKey)
    {
        ServiceStatus status = tracker.Get(serviceKey);
        ServiceStatusResponse response = new ServiceStatusResponse();
        if (status.ActiveSlot.HasValue)
        {
            response.ActiveSlot = status.ActiveSlot.Value.ToApiString();
        }
        if (status.LastSucceeded.HasValue)
        {
            response.LastOutcome = "FAILED";
            if (status.LastSucceeded.Value)
            {
                response.LastOutcome = "SUCCEEDED";
            }
        }
        response.LastAt = status.LastAt;
        response.LastErrorCode = status.LastErrorCode;
        return response;
    }

    private async Task<string?> FindUserNameAsync(Guid userId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }
        return user.Email;
    }
}

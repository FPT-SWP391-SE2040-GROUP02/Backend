using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Billing;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Services;

public sealed class SystemSettingService : ISystemSettingService
{
    private readonly AppDbContext db;
    private readonly IMemoryCache cache;
    private readonly TimeProvider time;
    private readonly ILogger<SystemSettingService> logger;
    private readonly IOptions<BillingSettings> billingOptions;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private const string AllSettingsCacheKey = "SystemSettings:All";

    public SystemSettingService(
        AppDbContext db,
        IMemoryCache cache,
        TimeProvider time,
        ILogger<SystemSettingService> logger,
        IOptions<BillingSettings> billingOptions)
    {
        this.db = db;
        this.cache = cache;
        this.time = time;
        this.logger = logger;
        this.billingOptions = billingOptions;
    }

    /// <summary>
    /// Kiểm tra các giá gói trong một lần lưu trước khi ghi DB: mỗi giá phải là số nguyên dương và bảng giá sau khi ghép
    /// phải hợp lệ (giá XS Max không thấp hơn XS cùng kỳ). Kiểm tra cả lô một lượt vì từng dòng riêng lẻ có thể tạm sai thứ tự.
    /// </summary>
    public async Task ValidatePlanPricesAsync(List<BatchUpdateSystemSettingItem> items, CancellationToken ct = default)
    {
        bool touchesPrice = false;
        foreach (BatchUpdateSystemSettingItem item in items)
        {
            if (PlanPriceRules.IsPriceKey(item.Key.Trim()))
            {
                touchesPrice = true;
            }
        }
        if (!touchesPrice)
        {
            return;
        }

        // Dòng đọc ra không theo dõi thay đổi (AsNoTracking) nên sửa trong bộ nhớ để thử ghép không ghi vào DB.
        List<SystemSetting> rows = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(PlanPriceRules.PlanKeyPrefix))
            .ToListAsync(ct);

        foreach (BatchUpdateSystemSettingItem item in items)
        {
            string key = item.Key.Trim();
            if (!PlanPriceRules.IsPriceKey(key))
            {
                continue;
            }

            long price;
            if (!long.TryParse(item.Value.Trim(), out price) || price <= 0)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                    $"Giá của '{key}' phải là số nguyên dương.");
            }

            SystemSetting? row = null;
            foreach (SystemSetting candidate in rows)
            {
                if (candidate.Key == key)
                {
                    row = candidate;
                }
            }
            if (row == null)
            {
                row = new SystemSetting();
                row.Key = key;
                rows.Add(row);
            }
            row.Value = price.ToString();
            row.UpdatedBy = "validation";
            row.UpdatedAt = time.GetUtcNow();
            if (item.IsEnabled.HasValue)
            {
                row.IsEnabled = item.IsEnabled.Value;
            }
        }

        bool hasOverride;
        BillingPricingSection effective = PlanPriceRules.BuildEffective(billingOptions.Value.Pricing, rows, out hasOverride);
        try
        {
            new BillingPriceSettings(effective);
        }
        catch (InvalidOperationException ex)
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                "Bảng giá không hợp lệ: " + ex.Message);
        }
    }

    public async Task<List<SystemSettingResponse>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureDefaultsSeededAsync(ct);
        var items = await db.SystemSettings.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.Key).ToListAsync(ct);
        return items.Select(Map).ToList();
    }

    public async Task<SystemSettingResponse?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        await EnsureDefaultsSeededAsync(ct);
        var cacheKey = $"SystemSetting:{key.Trim().ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out SystemSettingResponse? cached) && cached != null)
        {
            return cached;
        }

        var entity = await db.SystemSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Key == key.Trim(), ct);
        if (entity == null) return null;

        var mapped = Map(entity);
        cache.Set(cacheKey, mapped, CacheDuration);
        return mapped;
    }

    public async Task<SystemSettingResponse> UpdateAsync(
        string key,
        UpdateSystemSettingRequest request,
        string? updatedBy,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var entity = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == key.Trim(), ct);
        if (entity == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.SettingNotFound, $"Không tìm thấy tham số cấu hình '{key}'.");
        }

        BatchUpdateSystemSettingItem priceCheck = new BatchUpdateSystemSettingItem();
        priceCheck.Key = entity.Key;
        priceCheck.Value = request.Value;
        priceCheck.IsEnabled = request.IsEnabled;
        await ValidatePlanPricesAsync(new List<BatchUpdateSystemSettingItem> { priceCheck }, ct);

        var oldValue = entity.Value;
        var oldEnabled = entity.IsEnabled;

        entity.Value = request.Value.Trim();
        if (request.IsEnabled.HasValue)
        {
            entity.IsEnabled = request.IsEnabled.Value;
        }
        entity.UpdatedAt = time.GetUtcNow();
        entity.UpdatedBy = updatedBy;

        var auditLog = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            ActorRole = AuditActorRole.Admin,
            Action = AuditAction.SystemSettingUpdated,
            TargetType = AuditTargetType.Configuration,
            TargetId = entity.Id,
            Outcome = AuditOutcome.Succeeded,
            Reason = string.IsNullOrWhiteSpace(request.Reason)
                ? $"Cập nhật tham số '{entity.Key}': '{oldValue}' -> '{entity.Value}' (Bật={entity.IsEnabled})"
                : $"[{entity.Key}] '{oldValue}' -> '{entity.Value}'. Lý do: {request.Reason.Trim()}",
            At = time.GetUtcNow()
        };
        db.AuditLogs.Add(auditLog);

        await db.SaveChangesAsync(ct);

        InvalidateCache(entity.Key);
        logger.LogInformation("SystemSetting '{Key}' updated by {User} (AuditId={AuditId}) to Value='{Value}', IsEnabled={IsEnabled}",
            entity.Key, updatedBy ?? "System", auditLog.Id, entity.Value, entity.IsEnabled);

        return Map(entity);
    }

    public async Task<SystemSettingResponse> CreateAsync(
        CreateSystemSettingRequest request,
        string? createdBy,
        CancellationToken ct = default)
    {
        var trimmedKey = request.Key.Trim();
        var existing = await db.SystemSettings.AnyAsync(s => s.Key == trimmedKey, ct);
        if (existing)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict, $"Tham số '{trimmedKey}' đã tồn tại.");
        }

        var entity = new SystemSetting
        {
            Key = trimmedKey,
            Value = request.Value.Trim(),
            DataType = (request.DataType ?? "STRING").Trim().ToUpperInvariant(),
            Category = (request.Category ?? "General").Trim(),
            Description = (request.Description ?? "").Trim(),
            IsEnabled = request.IsEnabled ?? true,
            UpdatedAt = time.GetUtcNow(),
            UpdatedBy = createdBy
        };

        db.SystemSettings.Add(entity);
        await db.SaveChangesAsync(ct);

        InvalidateCache(entity.Key);
        return Map(entity);
    }

    public async Task<string> GetStringAsync(string key, string defaultValue, CancellationToken ct = default)
    {
        var setting = await GetByKeyAsync(key, ct);
        if (setting == null || !setting.IsEnabled) return defaultValue;
        return setting.Value;
    }

    public async Task<long> GetLongAsync(string key, long defaultValue, CancellationToken ct = default)
    {
        var setting = await GetByKeyAsync(key, ct);
        if (setting == null || !setting.IsEnabled) return defaultValue;
        return long.TryParse(setting.Value, out var val) ? val : defaultValue;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken ct = default)
    {
        var setting = await GetByKeyAsync(key, ct);
        if (setting == null) return defaultValue;
        if (!setting.IsEnabled) return false;
        return bool.TryParse(setting.Value, out var val) ? val : defaultValue;
    }

    private void InvalidateCache(string key)
    {
        cache.Remove(AllSettingsCacheKey);
        cache.Remove($"SystemSetting:{key.Trim().ToLowerInvariant()}");
    }

    private static SystemSettingResponse Map(SystemSetting s) =>
        new(s.Id, s.Key, s.Value, s.DataType, s.Category, s.Description, s.IsEnabled, s.UpdatedAt, s.UpdatedBy);

    private async Task EnsureDefaultsSeededAsync(CancellationToken ct)
    {
        var defaults = new List<SystemSetting>
        {
            // Điểm danh (Check-in)
            new() { Key = "CheckIn.DefaultCycleDays", Value = "30", DataType = "NUMBER", Category = "CheckIn", Description = "Chu kỳ điểm danh mặc định của chủ kho (ngày)" },
            new() { Key = "CheckIn.DefaultGraceDays", Value = "7", DataType = "NUMBER", Category = "CheckIn", Description = "Thời gian chờ ân hạn sau hạn điểm danh (ngày)" },

            // Bàn giao & Lưu trữ (Handover & Retention)
            new() { Key = "Handover.ResponseWindowDays", Value = "5", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn người nhận phản hồi lời mời bàn giao di sản (ngày)" },
            new() { Key = "Handover.DecisionWindowDays", Value = "7", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn người nhận quyết định nhận hoặc từ chối (ngày)" },
            new() { Key = "Handover.GrantWindowDays", Value = "30", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn được cấp quyền xem/tải tài sản sau khi nhận (ngày)" },
            new() { Key = "Handover.RetentionWindowDays", Value = "30", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn lưu trữ dữ liệu trước khi xóa hoàn toàn (ngày)" },
            new() { Key = "Handover.ResendCooldownMinutes", Value = "15", DataType = "NUMBER", Category = "Handover", Description = "Khoảng cách tối thiểu giữa hai lần gửi lại lời mời (phút)" },

            // Người thực thi (Executor)
            new() { Key = "Executor.InvitationExpiresDays", Value = "7", DataType = "NUMBER", Category = "Executor", Description = "Thời hạn hiệu lực của lời mời Người thực thi (ngày)" },
            new() { Key = "Executor.ReminderAfterHours", Value = "48", DataType = "NUMBER", Category = "Executor", Description = "Thời gian gửi email nhắc nhở sau khi mời Người thực thi (giờ)" },

            // Hồ sơ chứng tử (DeathCase)
            new() { Key = "DeathCase.SupplementDueDays", Value = "7", DataType = "NUMBER", Category = "DeathCase", Description = "Thời hạn Người thực thi bổ sung hồ sơ chứng tử (ngày)" },
            new() { Key = "DeathCase.MaxFilesPerVersion", Value = "10", DataType = "NUMBER", Category = "DeathCase", Description = "Số lượng tệp tối đa tải lên trong một bản hồ sơ chứng tử" },
            new() { Key = "DeathCase.MaxFileSizeMb", Value = "20", DataType = "NUMBER", Category = "DeathCase", Description = "Dung lượng tối đa của một tệp hồ sơ chứng tử (MiB)" },
            new() { Key = "DeathCase.ReviewDueHours", Value = "48", DataType = "NUMBER", Category = "DeathCase", Description = "Thời hạn Người xét duyệt phải hoàn tất thẩm định (giờ)" },
            new() { Key = "DeathCase.MaxNoticeAttempts", Value = "5", DataType = "NUMBER", Category = "DeathCase", Description = "Số lần gửi thông báo cho Chủ kho trước khi báo lỗi sự cố" },
            new() { Key = "DeathCase.ObjectionHours", Value = "72", DataType = "NUMBER", Category = "DeathCase", Description = "Thời gian Chủ kho được quyền phản đối hồ sơ chứng tử (giờ)" },

            // Chủ kho báo còn sống (Alive / Objection)
            new() { Key = "Alive.SelfieWindowDays", Value = "7", DataType = "NUMBER", Category = "Alive", Description = "Thời hạn Chủ kho hoàn tất selfie chứng minh còn sống (ngày)" },
            new() { Key = "Alive.MaxSelfieFailures", Value = "3", DataType = "NUMBER", Category = "Alive", Description = "Số lần selfie sai tối đa cho phép trong 24 giờ" },
            new() { Key = "Alive.VerifierDecisionHours", Value = "48", DataType = "NUMBER", Category = "Alive", Description = "Thời hạn Người xét duyệt ra quyết định về phản đối còn sống (giờ)" },

            // Sự cố & Tạm giữ (Incident & Hold)
            new() { Key = "Incident.ReminderAfterHours", Value = "48", DataType = "NUMBER", Category = "Incident", Description = "Thời gian nhắc nhở người phụ trách sự cố chưa xử lý (giờ)" },
            new() { Key = "Incident.EscalateAfterHours", Value = "72", DataType = "NUMBER", Category = "Incident", Description = "Thời gian tự động báo cáo lên Quản trị viên (giờ)" },
            new() { Key = "Incident.MaxReviewIntervalDays", Value = "7", DataType = "NUMBER", Category = "Incident", Description = "Khoảng cách tối đa giữa các lần hẹn xem lại sự cố (ngày)" },

            // Giới hạn chống lạm dụng & Quota (Limits)
            new() { Key = "Limits.MaxUploadsPerHour", Value = "60", DataType = "NUMBER", Category = "Limits", Description = "Số lần tải lên tối đa mỗi giờ cho một tài khoản" },
            new() { Key = "Limits.MaxAssetsPerVault", Value = "2000", DataType = "NUMBER", Category = "Limits", Description = "Số lượng tài sản tối đa được lưu trữ trong một kho" },
            new() { Key = "Limits.MaxUploadBytesMb", Value = "500", DataType = "NUMBER", Category = "Limits", Description = "Dung lượng tối đa của mỗi lần tải lên (MiB)" },
            new() { Key = "Limits.ReservationHoldMinutes", Value = "15", DataType = "NUMBER", Category = "Limits", Description = "Thời gian giữ chỗ tài nguyên khi đang tải lên (phút)" },

            // Gói dịch vụ (Plans)
            new() { Key = "Plans.Free.QuotaMb", Value = "20", DataType = "NUMBER", Category = "Plans", Description = "Dung lượng lưu trữ gói Free (MiB)" },
            new() { Key = "Plans.Free.Slots", Value = "0", DataType = "NUMBER", Category = "Plans", Description = "Số người nhận kèm theo gói Free" },
            new() { Key = "Plans.Free.MaxPackages", Value = "50", DataType = "NUMBER", Category = "Plans", Description = "Số gói bàn giao tối đa gói Free" },

            new() { Key = "Plans.Xs.On", Value = "true", DataType = "BOOLEAN", Category = "Plans", Description = "Trạng thái mở bán gói XS" },
            new() { Key = "Plans.Xs.Price1Y", Value = "199000", DataType = "NUMBER", Category = "Plans", Description = "Giá gói XS kỳ 1 năm (VNĐ)" },
            new() { Key = "Plans.Xs.Price3Y", Value = "499000", DataType = "NUMBER", Category = "Plans", Description = "Giá gói XS kỳ 3 năm (VNĐ)" },
            new() { Key = "Plans.Xs.Price5Y", Value = "799000", DataType = "NUMBER", Category = "Plans", Description = "Giá gói XS kỳ 5 năm (VNĐ)" },
            new() { Key = "Plans.Xs.QuotaMb", Value = "200", DataType = "NUMBER", Category = "Plans", Description = "Dung lượng lưu trữ gói XS (MiB)" },
            new() { Key = "Plans.Xs.Slots", Value = "2", DataType = "NUMBER", Category = "Plans", Description = "Số người nhận kèm theo gói XS" },
            new() { Key = "Plans.Xs.MaxPackages", Value = "50", DataType = "NUMBER", Category = "Plans", Description = "Số gói bàn giao tối đa gói XS" },

            new() { Key = "Plans.XsMax.On", Value = "true", DataType = "BOOLEAN", Category = "Plans", Description = "Trạng thái mở bán gói XS Max" },
            new() { Key = "Plans.XsMax.Price1Y", Value = "399000", DataType = "NUMBER", Category = "Plans", Description = "Giá gói XS Max kỳ 1 năm (VNĐ)" },
            new() { Key = "Plans.XsMax.Price3Y", Value = "999000", DataType = "NUMBER", Category = "Plans", Description = "Giá gói XS Max kỳ 3 năm (VNĐ)" },
            new() { Key = "Plans.XsMax.Price5Y", Value = "1599000", DataType = "NUMBER", Category = "Plans", Description = "Giá gói XS Max kỳ 5 năm (VNĐ)" },
            new() { Key = "Plans.XsMax.QuotaMb", Value = "500", DataType = "NUMBER", Category = "Plans", Description = "Dung lượng lưu trữ gói XS Max (MiB)" },
            new() { Key = "Plans.XsMax.Slots", Value = "5", DataType = "NUMBER", Category = "Plans", Description = "Số người nhận kèm theo gói XS Max" },
            new() { Key = "Plans.XsMax.MaxPackages", Value = "50", DataType = "NUMBER", Category = "Plans", Description = "Số gói bàn giao tối đa gói XS Max" },

            new() { Key = "Plans.Addon.SlotPricePerYear", Value = "49000", DataType = "NUMBER", Category = "Plans", Description = "Giá một suất người nhận thêm / năm (VNĐ)" },
            new() { Key = "Plans.Addon.MaxSlotsPerOrder", Value = "20", DataType = "NUMBER", Category = "Plans", Description = "Số suất người nhận thêm tối đa mỗi đơn hàng" },
            new() { Key = "Plans.Addon.OrderTimeoutMinutes", Value = "15", DataType = "NUMBER", Category = "Plans", Description = "Thời hạn đơn hàng chờ thanh toán SePay (phút)" },

            // Security & Features
            new() { Key = "Security.MaxPinAttempts", Value = "5", DataType = "NUMBER", Category = "Security", Description = "Số lần nhập sai mã PIN bảo vệ tối đa trước khi khóa" },
            new() { Key = "Security.PinLockoutMinutes", Value = "15", DataType = "NUMBER", Category = "Security", Description = "Thời gian tạm khóa mã PIN bảo vệ khi vượt số lần thử (phút)" },
            new() { Key = "Features.EnableRegistration", Value = "true", DataType = "BOOLEAN", Category = "Features", Description = "Cho phép đăng ký tài khoản mới" },
            new() { Key = "Features.MaintenanceMode", Value = "false", DataType = "BOOLEAN", Category = "Features", Description = "Bật chế độ bảo trì toàn hệ thống" },
        };

        var existingKeys = await db.SystemSettings.Select(s => s.Key).ToHashSetAsync(ct);
        var missing = defaults.Where(d => !existingKeys.Contains(d.Key)).ToList();
        if (missing.Count > 0)
        {
            db.SystemSettings.AddRange(missing);
            await db.SaveChangesAsync(ct);
        }
    }
}

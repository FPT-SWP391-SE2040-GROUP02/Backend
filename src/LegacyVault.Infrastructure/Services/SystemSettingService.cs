using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Billing;
using LegacyVault.Infrastructure.Data;
using Microsoft.Data.SqlClient;
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
    private const string DefaultsSeededCacheKey = "SystemSettings:DefaultsSeeded";
    private static readonly SemaphoreSlim SeedGate = new(1, 1);

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
            if (PlanPriceRules.IsPricingKey(item.Key.Trim()))
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
            if (!PlanPriceRules.IsPricingKey(key))
            {
                continue;
            }

            string text = item.Value.Trim();
            if (PlanPriceRules.IsPriceKey(key))
            {
                long price;
                if (!long.TryParse(text, out price) || price <= 0)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        $"Giá của '{key}' phải là số nguyên dương.");
                }
                bool isSlotPrice = key == "Plans.Addon.SlotPricePerYear";
                long maxPrice = isSlotPrice ? BillingPrices.MaxSlotPricePerYear : BillingPrices.MaxTierPrice;
                if (price > maxPrice)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        $"Giá của '{key}' tối đa {BillingPrices.FormatMoney(maxPrice)}đ " + (isSlotPrice ? "mỗi năm." : "mỗi kỳ."));
                }
                int? priceYears = PlanPriceRules.PriceKeyYears(key);
                if (priceYears != null && (priceYears.Value < 1 || priceYears.Value > BillingPriceSettings.MaxSaleYears))
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        $"Kỳ của '{key}' phải từ 1 đến {BillingPriceSettings.MaxSaleYears} năm.");
                }
            }
            else if (PlanPriceRules.IsFeaturesKey(key))
            {
                if (PlanPriceRules.ParseFeatures(text) == null)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        $"Mô tả tính năng của '{key}' phải là mảng chuỗi, tối đa {PlanPriceRules.MaxFeatureLines} dòng, mỗi dòng tối đa {PlanPriceRules.MaxFeatureLength} ký tự.");
                }
            }
            else if (PlanPriceRules.IsOrderRuleKey(key))
            {
                bool isSlots = key == PlanPriceRules.MaxSlotsPerOrderKey;
                bool isTotal = key == PlanPriceRules.MaxTotalSlotsKey;
                int maxValue = isTotal ? BillingPrices.MaxTotalSlotsLimit : (isSlots ? 100 : 1440);
                int count;
                if (!int.TryParse(text, out count) || count < 1 || count > maxValue)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        isTotal
                            ? $"Tổng số người nhận tối đa mỗi tài khoản phải là số nguyên từ 1 đến {BillingPrices.MaxTotalSlotsLimit}."
                            : isSlots
                                ? "Số suất thêm tối đa mỗi đơn phải là số nguyên từ 1 đến 100."
                                : "Hạn đơn chờ thanh toán phải là số phút nguyên từ 1 đến 1440.");
                }
            }
            else if (PlanPriceRules.IsLimitKey(key))
            {
                long limit;
                bool isQuota = key.EndsWith(".QuotaMb", StringComparison.Ordinal);
                if (!long.TryParse(text, out limit) || limit > 1000000 || (isQuota ? limit < 1 : limit < 0))
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        isQuota
                            ? $"Dung lượng của '{key}' phải là số nguyên từ 1 đến 1.000.000 MiB."
                            : $"Số suất của '{key}' phải là số nguyên từ 0 đến 1.000.000.");
                }
            }
            else if (key == PlanPriceRules.CatalogKey)
            {
                List<BillingPlanSection>? declared = PlanPriceRules.ParseCatalog(text);
                if (declared == null)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        "Danh sách gói không hợp lệ: mã gói gồm chữ hoa, số, gạch dưới (2 đến 24 ký tự, bắt đầu bằng chữ cái, không trùng XS, XS_MAX, FREE), tên 1 đến 40 ký tự.");
                }
                if (declared.Select(p => p.Code).Distinct().Count() != declared.Count)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        "Danh sách gói có mã bị trùng.");
                }
                // Tên hiển thị không được trùng nhau hay trùng gói gốc, không phân biệt hoa thường và khoảng trắng thừa.
                HashSet<string> usedNames = new HashSet<string>(PlanPriceRules.ReservedDisplayNames, StringComparer.Ordinal);
                foreach (BillingPlanSection plan in declared)
                {
                    string normalized = PlanPriceRules.NormalizeName(plan.Name);
                    if (!usedNames.Add(normalized))
                    {
                        throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                            $"Tên gói \"{plan.Name}\" bị trùng (không phân biệt hoa thường) với một gói khác. Hãy đặt tên khác.");
                    }
                }
            }
            else if (key == PlanPriceRules.PeriodsKey)
            {
                List<int> allYears;
                List<int>? onYears = PlanPriceRules.ParsePeriods(text, out allYears);
                if (onYears == null || allYears.Count == 0 || allYears.Distinct().Count() != allYears.Count)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        "Danh sách kỳ bán không hợp lệ: cần mảng các kỳ, mỗi số năm xuất hiện một lần.");
                }
                if (allYears.Any(y => y < 1 || y > BillingPriceSettings.MaxSaleYears))
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        $"Kỳ bán phải từ 1 đến {BillingPriceSettings.MaxSaleYears} năm.");
                }
                if (onYears.Count == 0)
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        "Phải còn ít nhất một kỳ đang bán.");
                }
            }
            else
            {
                bool flag;
                if (!bool.TryParse(text, out flag))
                {
                    throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                        $"Giá trị của '{key}' phải là true hoặc false.");
                }
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
            row.Value = text;
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
            BillingPriceSettings merged = new BillingPriceSettings(effective);
            PlanInfo? tooMany = merged.FindPlanExceedingTotalSlots();
            if (tooMany != null)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                    $"Gói {tooMany.Name} có {tooMany.IncludedSlots} suất kèm, vượt tổng người nhận tối đa mỗi tài khoản ({merged.MaxTotalSlotsPerAccount}). Hãy tăng số này hoặc giảm suất kèm của gói.");
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError,
                "Bảng giá không hợp lệ: " + ex.Message);
        }
    }

    public async Task ValidateValuesAsync(List<BatchUpdateSystemSettingItem> items, CancellationToken ct = default)
    {
        bool touchesRelation = false;
        List<string> relationKeys = new List<string>(SystemSettingRules.RelationKeys);
        foreach (BatchUpdateSystemSettingItem item in items)
        {
            string key = item.Key.Trim();
            if (item.IsEnabled == false)
            {
                // Tham số bị tắt thì hệ thống dùng mức gốc, giá trị gõ vào không có tác dụng nên không kiểm.
                if (relationKeys.Contains(key))
                {
                    touchesRelation = true;
                }
                continue;
            }
            string? valueError = SystemSettingRules.FindValueError(key, item.Value);
            if (valueError != null)
            {
                throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError, valueError);
            }
            if (relationKeys.Contains(key))
            {
                touchesRelation = true;
            }
        }
        if (!touchesRelation)
        {
            return;
        }

        // Giá trị sau khi lưu = dòng đang bật trong DB, rồi chồng các thay đổi của lần lưu này lên trên.
        Dictionary<string, long> effective = new Dictionary<string, long>();
        List<SystemSetting> rows = await db.SystemSettings
            .AsNoTracking()
            .Where(s => relationKeys.Contains(s.Key))
            .ToListAsync(ct);
        foreach (SystemSetting row in rows)
        {
            long stored;
            if (row.IsEnabled && long.TryParse(row.Value, out stored))
            {
                effective[row.Key] = stored;
            }
        }
        foreach (BatchUpdateSystemSettingItem item in items)
        {
            string key = item.Key.Trim();
            if (!relationKeys.Contains(key))
            {
                continue;
            }
            long changed;
            if (item.IsEnabled == false || !long.TryParse(item.Value.Trim(), out changed))
            {
                effective.Remove(key);
                continue;
            }
            effective[key] = changed;
        }
        string? relationError = SystemSettingRules.FindRelationError(effective);
        if (relationError != null)
        {
            throw new AppException(AppErrorType.Validation, ErrorCode.ValidationError, relationError);
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
        CancellationToken ct = default,
        bool pricesAlreadyValidated = false)
    {
        var entity = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == key.Trim(), ct);
        if (entity == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.SettingNotFound, $"Không tìm thấy tham số cấu hình '{key}'.");
        }

        if (!pricesAlreadyValidated)
        {
            BatchUpdateSystemSettingItem priceCheck = new BatchUpdateSystemSettingItem();
            priceCheck.Key = entity.Key;
            priceCheck.Value = request.Value;
            priceCheck.IsEnabled = request.IsEnabled;
            await ValidatePlanPricesAsync(new List<BatchUpdateSystemSettingItem> { priceCheck }, ct);
            await ValidateValuesAsync(new List<BatchUpdateSystemSettingItem> { priceCheck }, ct);
        }

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
        Guid? actorUserId = null,
        string? reason = null,
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
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            ActorRole = AuditActorRole.Admin,
            Action = AuditAction.SystemSettingUpdated,
            TargetType = AuditTargetType.Configuration,
            TargetId = entity.Id,
            Outcome = AuditOutcome.Succeeded,
            Reason = string.IsNullOrWhiteSpace(reason)
                ? $"Tạo tham số '{entity.Key}' = '{entity.Value}'"
                : $"[{entity.Key}] tạo '{entity.Value}'. Lý do: {reason.Trim()}",
            At = time.GetUtcNow()
        });
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
        // Admin vừa lưu: nạp lại ảnh chụp tham số ngay để cấu hình có hiệu lực không phải đợi vòng nạp định kỳ.
        try
        {
            RuntimeSettingsRefresher.LoadAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Nạp lại tham số Admin sau khi lưu lỗi: {ExceptionType}", ex.GetType().Name);
        }
        cache.Remove(AllSettingsCacheKey);
        cache.Remove($"SystemSetting:{key.Trim().ToLowerInvariant()}");
    }

    private static SystemSettingResponse Map(SystemSetting s) =>
        new(s.Id, s.Key, s.Value, s.DataType, s.Category, s.Description, s.IsEnabled, s.UpdatedAt, s.UpdatedBy);

    private async Task EnsureDefaultsSeededAsync(CancellationToken ct)
    {
        // Đã kiểm tra gần đây thì bỏ qua: BillingService gọi hàm này trên mỗi báo giá và tạo đơn, không truy vấn lại mỗi lần.
        if (cache.TryGetValue(DefaultsSeededCacheKey, out _))
        {
            return;
        }

        // Một request tạo mặc định tại một thời điểm: hai request cùng chèn hàng loạt vào bảng có khóa unique sẽ deadlock.
        await SeedGate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(DefaultsSeededCacheKey, out _))
            {
                return;
            }
            await SeedDefaultsAsync(ct);
        }
        finally
        {
            SeedGate.Release();
        }
    }

    private async Task SeedDefaultsAsync(CancellationToken ct)
    {
        var defaults = new List<SystemSetting>
        {
            // Điểm danh (Check-in)
            new() { Key = "CheckIn.DefaultCycleDays", Value = "30", DataType = "NUMBER", Category = "CheckIn", Description = "Chu kỳ điểm danh mặc định của chủ kho (ngày)" },
            new() { Key = "CheckIn.FirstReminderDaysBeforeDue", Value = "7", DataType = "NUMBER", Category = "CheckIn", Description = "Thư nhắc trước hạn thứ nhất: gửi trước hạn điểm danh bao nhiêu ngày (lớn hơn thư thứ hai, tối đa 60)" },
            new() { Key = "CheckIn.SecondReminderDaysBeforeDue", Value = "1", DataType = "NUMBER", Category = "CheckIn", Description = "Thư nhắc trước hạn thứ hai: gửi trước hạn điểm danh bao nhiêu ngày (từ 1 đến 30)" },
            new() { Key = "CheckIn.LateReminderGapDays", Value = "3", DataType = "NUMBER", Category = "CheckIn", Description = "Khoảng cách giữa hai thư nhắc trễ điểm danh (ngày)" },
            new() { Key = "CheckIn.MaxLateReminders", Value = "3", DataType = "NUMBER", Category = "CheckIn", Description = "Số thư nhắc trễ gửi chủ kho trước khi báo Người thực thi (1 đến 5)" },
            new() { Key = "CheckIn.ExecutorProxyLimit", Value = "3", DataType = "NUMBER", Category = "CheckIn", Description = "Số lần Người thực thi được điểm danh thay liên tiếp; về 0 khi Chủ kho tự điểm danh" },

            // Bàn giao & Lưu trữ (Handover & Retention)
            new() { Key = "Handover.ResponseWindowDays", Value = "5", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn người nhận phản hồi lời mời bàn giao di sản (ngày)" },
            new() { Key = "Handover.DecisionWindowDays", Value = "7", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn người nhận quyết định nhận hoặc từ chối (ngày)" },
            new() { Key = "Handover.GrantWindowDays", Value = "30", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn được cấp quyền xem/tải tài sản sau khi nhận (ngày)" },
            new() { Key = "Handover.RetentionWindowDays", Value = "30", DataType = "NUMBER", Category = "Handover", Description = "Thời hạn lưu trữ dữ liệu trước khi xóa hoàn toàn (ngày)" },
            new() { Key = "Handover.ResendCooldownMinutes", Value = "15", DataType = "NUMBER", Category = "Handover", Description = "Khoảng cách tối thiểu giữa hai lần gửi lại lời mời (phút)" },

            // Người thực thi (Executor)
            new() { Key = "Executor.InvitationExpiresMinutes", Value = "10", DataType = "NUMBER", Category = "Executor", Description = "Thời hạn hiệu lực của lời mời Người thực thi (phút)" },
            new() { Key = "Executor.ReminderAfterHours", Value = "48", DataType = "NUMBER", Category = "Executor", Description = "Thời gian gửi email nhắc nhở sau khi mời Người thực thi (giờ)" },

            // Hồ sơ chứng tử (DeathCase)
            new() { Key = "DeathCase.SupplementDueDays", Value = "7", DataType = "NUMBER", Category = "DeathCase", Description = "Thời hạn Người thực thi bổ sung hồ sơ chứng tử (ngày)" },
            new() { Key = "DeathCase.MaxFilesPerVersion", Value = "10", DataType = "NUMBER", Category = "DeathCase", Description = "Số lượng tệp tối đa tải lên trong một bản hồ sơ chứng tử" },
            new() { Key = "DeathCase.MaxFileSizeMb", Value = "20", DataType = "NUMBER", Category = "DeathCase", Description = "Dung lượng tối đa của một tệp hồ sơ chứng tử (MiB)" },
            new() { Key = "DeathCase.ReviewDueHours", Value = "48", DataType = "NUMBER", Category = "DeathCase", Description = "Thời hạn Người xét duyệt phải hoàn tất thẩm định (giờ)" },

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
            new() { Key = "Plans.Addon.MaxTotalSlots", Value = "50", DataType = "NUMBER", Category = "Plans", Description = "Tổng số người nhận tối đa của một tài khoản (suất kèm gói + suất mua thêm)" },
            new() { Key = "Plans.Addon.OrderTimeoutMinutes", Value = "15", DataType = "NUMBER", Category = "Plans", Description = "Thời hạn đơn hàng chờ thanh toán SePay (phút)" },

            // Thông tin công ty hiện trong mọi thư gửi người nhận (cảnh báo lừa đảo kèm số liên lạc để họ tự xác minh)
            new() { Key = "Company.Name", Value = "LegacyVault", DataType = "STRING", Category = "Company", Description = "Tên công ty hiện trong thư gửi người nhận" },
            new() { Key = "Company.Website", Value = "legacyvault.vn", DataType = "STRING", Category = "Company", Description = "Website công ty hiện trong thư gửi người nhận" },
            new() { Key = "Company.Email", Value = "ho-tro@legacyvault.vn", DataType = "STRING", Category = "Company", Description = "Email liên hệ công ty hiện trong thư gửi người nhận" },
            new() { Key = "Company.Phone", Value = "1900 0000", DataType = "STRING", Category = "Company", Description = "Số điện thoại công ty để người nhận gọi xác minh thư" },
            new() { Key = "Company.Address", Value = "", DataType = "STRING", Category = "Company", Description = "Địa chỉ công ty hiện trong thư gửi người nhận (để trống thì không hiện)" },

            // Security & Features
            new() { Key = "Security.MaxPinAttempts", Value = "5", DataType = "NUMBER", Category = "Security", Description = "Số lần nhập sai mã PIN bảo vệ tối đa trước khi khóa" },
            new() { Key = "Security.PinLockoutMinutes", Value = "15", DataType = "NUMBER", Category = "Security", Description = "Thời gian tạm khóa mã PIN bảo vệ khi vượt số lần thử (phút)" },
            new() { Key = "Features.MaintenanceMode", Value = "false", DataType = "BOOLEAN", Category = "Features", Description = "Bật chế độ bảo trì toàn hệ thống" },
            new() { Key = "Time.DemoSeconds", Value = "false", DataType = "BOOLEAN", Category = "Features", Description = "Chế độ demo: mọi mốc ngày, giờ, phút của nghiệp vụ (điểm danh, ân hạn, hạn xét duyệt, hạn bổ sung, cửa sổ bàn giao...) tính bằng giây" },
            new() { Key = "Time.AuthFollowsDemoClock", Value = "false", DataType = "BOOLEAN", Category = "Features", Description = "Phiên đăng nhập chạy theo giờ đã tua (bật để demo việc hết phiên; tắt thì tua thời gian không làm đăng xuất)" },
        };

        var existingKeys = await db.SystemSettings.Select(s => s.Key).ToHashSetAsync(ct);
        var missing = defaults.Where(d => !existingKeys.Contains(d.Key)).ToList();
        if (missing.Count > 0)
        {
            db.SystemSettings.AddRange(missing);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception exception) when (exception is DbUpdateException || exception.GetBaseException() is SqlException)
            {
                // Tiến trình khác (nhiều instance API) vừa tạo cùng các khóa mặc định (unique hoặc deadlock): bỏ phần mình vừa thêm, lần gọi sau kiểm tra lại.
                foreach (var entry in db.ChangeTracker.Entries<SystemSetting>().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
                return;
            }
        }
        cache.Set(DefaultsSeededCacheKey, true, CacheDuration);
    }
}

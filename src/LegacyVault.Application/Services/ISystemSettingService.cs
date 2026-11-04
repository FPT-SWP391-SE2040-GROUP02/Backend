using LegacyVault.Application.Dtos;

namespace LegacyVault.Application.Services;

public interface ISystemSettingService
{
    Task<List<SystemSettingResponse>> GetAllAsync(CancellationToken ct = default);
    Task<SystemSettingResponse?> GetByKeyAsync(string key, CancellationToken ct = default);
    /// <param name="pricesAlreadyValidated">true khi cả lô đã qua <see cref="ValidatePlanPricesAsync"/>; bỏ kiểm tra riêng từng dòng vì trạng thái giữa chừng của lô có thể tạm thiếu giá.</param>
    Task<SystemSettingResponse> UpdateAsync(string key, UpdateSystemSettingRequest request, string? updatedBy, Guid? actorUserId = null, CancellationToken ct = default, bool pricesAlreadyValidated = false);
    /// <summary>Kiểm tra các giá gói trong lần lưu (số dương, XS Max không thấp hơn XS) trước khi ghi DB.</summary>
    Task ValidatePlanPricesAsync(List<BatchUpdateSystemSettingItem> items, CancellationToken ct = default);
    Task<SystemSettingResponse> CreateAsync(CreateSystemSettingRequest request, string? createdBy, Guid? actorUserId = null, string? reason = null, CancellationToken ct = default);
    Task<string> GetStringAsync(string key, string defaultValue, CancellationToken ct = default);
    Task<long> GetLongAsync(string key, long defaultValue, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken ct = default);
}

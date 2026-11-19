using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng OwnerIdentities (danh tính đã gắn của Owner, EKYC-27).</summary>
public interface IOwnerIdentityRepository
{
    /// <summary>Danh tính đã gắn của Owner. Chưa có → null.</summary>
    Task<OwnerIdentity?> FindByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Danh sách danh tính có 4 số cuối CCCD trùng khớp (không gồm user chỉ định) để giải mã đối chiếu trùng.</summary>
    Task<List<OwnerIdentity>> ListByCitizenIdLast4ExceptUserAsync(string last4, Guid excludeUserId, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" một danh tính.</summary>
    /// <summary>Có danh tính của tài khoản KHÁC đã mang hash CCCD này không.</summary>
    Task<bool> ExistsByCitizenIdHashExceptUserAsync(string citizenIdHash, Guid excludeUserId, CancellationToken ct);

    /// <summary>true khi lỗi lưu là vi phạm unique index (request khác vừa ghi trước).</summary>
    bool IsUniqueViolation(Exception exception);

    void Add(OwnerIdentity identity);
}

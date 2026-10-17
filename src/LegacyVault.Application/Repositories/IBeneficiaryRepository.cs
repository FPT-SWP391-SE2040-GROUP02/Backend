using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Beneficiaries (hồ sơ người nhận cấp kho).</summary>
public interface IBeneficiaryRepository
{
    /// <summary>Một hồ sơ trong ĐÚNG kho (có theo dõi thay đổi). Không thấy / kho khác → null (SEC-04).</summary>
    Task<Beneficiary?> FindByIdAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct);

    /// <summary>
    /// Các hồ sơ của kho có cùng 4 số cuối CCCD (có theo dõi thay đổi), để service giải mã và so số đầy đủ (QUOTA-04).
    /// </summary>
    Task<List<Beneficiary>> ListByCitizenIdLast4Async(Guid vaultId, string last4, CancellationToken ct);

    /// <summary>
    /// Một trang hồ sơ của kho, sắp theo thời điểm tạo, kèm tổng số bản ghi khớp bộ lọc.
    /// last4Filter (1–4 chữ số, service đã kiểm) lọc các hồ sơ có 4 số cuối CCCD KẾT THÚC bằng chuỗi này.
    /// pageIndex bắt đầu từ 1.
    /// </summary>
    Task<(List<Beneficiary> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, string? last4Filter, int pageIndex, int pageSize, bool isAscending, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT" hồ sơ mới.</summary>
    void Add(Beneficiary beneficiary);

    /// <summary>Đánh dấu "sẽ DELETE" hồ sơ.</summary>
    void Remove(Beneficiary beneficiary);

    /// <summary>Lỗi lưu do trùng khóa unique (hai request cùng CCCD đồng thời).</summary>
    bool IsUniqueViolation(Exception exception);

    /// <summary>Bỏ mọi thay đổi chưa lưu của request (ChangeTracker.Clear), gồm cả hồ sơ vừa Add và dòng nhật ký đi kèm.</summary>
    void DiscardPendingChanges();
}

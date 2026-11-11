using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng HandoverTemplateSets: chỉ thêm phiên bản mới và đọc, không sửa hay xóa bản đã lưu.</summary>
public interface IHandoverTemplateRepository
{
    /// <summary>Phiên bản mới nhất; chưa có bản nào → null (dùng mẫu gốc). Không theo dõi thay đổi.</summary>
    Task<HandoverTemplateSet?> FindLatestAsync(CancellationToken ct);

    /// <summary>Phiên bản theo số; không có → null. Không theo dõi thay đổi.</summary>
    Task<HandoverTemplateSet?> FindByIdAsync(int version, CancellationToken ct);

    /// <summary>Các phiên bản mới nhất trước, tối đa count dòng. Không theo dõi thay đổi.</summary>
    Task<List<HandoverTemplateSet>> ListLatestAsync(int count, CancellationToken ct);

    /// <summary>Đánh dấu thêm một phiên bản; người gọi lưu bằng IUnitOfWork.SaveChangesAsync cùng dòng nhật ký.</summary>
    void Add(HandoverTemplateSet set);
}

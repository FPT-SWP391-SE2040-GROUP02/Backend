using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng EmailTemplates: mỗi loại thư tối đa một dòng.</summary>
public interface IEmailTemplateRepository
{
    /// <summary>Mọi dòng đã lưu. Không theo dõi thay đổi.</summary>
    Task<List<EmailTemplate>> ListAsync(CancellationToken ct);

    /// <summary>Dòng của một loại thư; chưa có → null. Có theo dõi thay đổi để sửa tại chỗ.</summary>
    Task<EmailTemplate?> FindAsync(string key, CancellationToken ct);

    void Add(EmailTemplate template);

    void Remove(EmailTemplate template);
}

namespace LegacyVault.Application.Repositories;

/// <summary>
/// Lưu mọi thay đổi đang chờ của request xuống DB trong MỘT lần (STATE-02).
/// </summary>
/// <remarks>
/// Các repository chỉ đánh dấu Add/Remove/sửa entity; service gọi SaveChangesAsync một lần ở cuối use case.
/// Cài đặt ở Infrastructure: bọc AppDbContext.SaveChangesAsync (các repository dùng chung DbContext của request).
/// </remarks>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Thực thi Auth read-modify-write trong transaction Serializable; lỗi rollback, không retry mutation.</summary>
    Task<T> ExecuteSerializableAsync<T>(Func<Task<T>> operation, CancellationToken ct);

}

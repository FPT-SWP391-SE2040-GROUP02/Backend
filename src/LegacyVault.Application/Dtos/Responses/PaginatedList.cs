namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một trang dữ liệu. Tên trường khớp interface PaginatedList&lt;T&gt; của FE (src/shared/types),
/// để baseService.getAll của FE dùng được không cần override.
/// </summary>
public class PaginatedList<T>
{
    public List<T> Items { get; set; } = new List<T>();

    public int TotalCount { get; set; }

    /// <summary>Bắt đầu từ 1.</summary>
    public int PageIndex { get; set; }

    public int PageSize { get; set; }

    public int TotalPages { get; set; }

    public bool HasPreviousPage { get; set; }

    public bool HasNextPage { get; set; }
}

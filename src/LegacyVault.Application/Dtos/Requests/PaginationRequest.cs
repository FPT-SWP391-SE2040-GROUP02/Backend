using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Tham số phân trang đọc từ query string. Tên và mặc định khớp PaginationParams / paginationParamsSchema của FE.
/// </summary>
public class PaginationRequest
{
    /// <summary>Trang muốn lấy, bắt đầu từ 1.</summary>
    [Range(1, int.MaxValue)]
    public int PageIndex { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 10;

    [MaxLength(200)]
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Tên cột sắp xếp. Mỗi service tự quy định danh sách cột cho phép; giá trị lạ → 400 VALIDATION_ERROR
    /// (không đưa thẳng chuỗi FE gửi vào truy vấn).
    /// </summary>
    [MaxLength(50)]
    public string? SortBy { get; set; }

    public bool IsAscending { get; set; } = true;
}

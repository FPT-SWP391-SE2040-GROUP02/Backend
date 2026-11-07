namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Số lượng theo từng nhóm (trạng thái hoặc vai trò) của một danh sách Admin, theo các bộ lọc còn lại.</summary>
public class CountsResponse
{
    /// <summary>Khóa là giá trị dạng API của nhóm (ví dụ OPEN, ADMIN); nhóm không có dòng nào vẫn có mặt với số 0.</summary>
    public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();

    /// <summary>Tổng các nhóm, tức số dòng khi không lọc theo nhóm.</summary>
    public int Total { get; set; }
}

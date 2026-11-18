namespace LegacyVault.Application.Services;

/// <summary>
/// Dựng ảnh văn bản của một hồ sơ người nhận (văn bản từ chối hoặc biên bản đồng ý) và gắn vào hồ sơ.
/// </summary>
public interface IHandoverPaperWriter
{
    /// <summary>
    /// Dựng ảnh, lưu mã hóa và gán các cột Paper* của hồ sơ. Trả khóa lưu để người gọi xóa bù khi lưu bản ghi thất bại;
    /// trả null khi thiếu dữ liệu để dựng (không có hồ sơ người nhận, hoặc có người xét duyệt mà không tìm thấy họ).
    /// </summary>
    /// <remarks>
    /// Có Document thì dựng từ biên bản đã ký (họ tên người nhận, số giấy chứng tử, ngày, chữ ký của người nhận và Người xét duyệt);
    /// không có thì dựng như luồng cũ từ hồ sơ người nhận và hồ sơ chứng tử.
    /// </remarks>
    Task<string?> WriteAsync(HandoverPaperRequest request, CancellationToken ct);
}

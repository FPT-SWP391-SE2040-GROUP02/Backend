namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một tỉnh/thành phố hoặc một phường/xã/đặc khu trong danh mục đơn vị hành chính (docs/api-contract.md mục 6.10a).
/// Dữ liệu tĩnh theo nghị quyết 388/NQ-UBTVQH16, đọc từ tệp của hệ thống, không gọi dịch vụ ngoài.
/// </summary>
public class AdministrativeUnitResponse
{
    /// <summary>Mã đơn vị (tỉnh: 2 ký tự, phường/xã: 5 ký tự), giữ nguyên số 0 đứng đầu nên là chuỗi.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tên đầy đủ, ví dụ "Thành phố Hồ Chí Minh", "Phường Bến Thành".</summary>
    public string Name { get; set; } = string.Empty;
}

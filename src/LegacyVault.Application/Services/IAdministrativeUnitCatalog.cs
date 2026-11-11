using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Danh mục tỉnh/thành phố và phường/xã (34 tỉnh, 3321 phường/xã/đặc khu theo nghị quyết 388/NQ-UBTVQH16).
/// Cài đặt ở Infrastructure, đọc tệp nhúng một lần; không gọi dịch vụ ngoài lúc chạy.
/// </summary>
public interface IAdministrativeUnitCatalog
{
    /// <summary>Mọi tỉnh/thành phố, theo thứ tự trong tệp dữ liệu.</summary>
    List<AdministrativeUnitResponse> GetProvinces();

    /// <summary>Các phường/xã của một tỉnh; mã tỉnh không có trong danh mục thì trả danh sách rỗng.</summary>
    List<AdministrativeUnitResponse> GetWards(string provinceCode);

    /// <summary>Tỉnh theo mã; không có thì null.</summary>
    AdministrativeUnitResponse? FindProvince(string provinceCode);

    /// <summary>Phường/xã theo mã, chỉ khi thuộc đúng tỉnh provinceCode; không có hoặc sai tỉnh thì null.</summary>
    AdministrativeUnitResponse? FindWard(string provinceCode, string wardCode);

    /// <summary>
    /// Nơi đăng ký khai tử in lên biên bản: "UBND {tên phường/xã viết thường chữ đầu}, {tên tỉnh}",
    /// ví dụ "UBND phường Bến Thành, Thành phố Hồ Chí Minh". Thiếu mã hoặc mã không hợp lệ thì trả chuỗi rỗng.
    /// </summary>
    string BuildRegistrationPlace(string? provinceCode, string? wardCode);
}

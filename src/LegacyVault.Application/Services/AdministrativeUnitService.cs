using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;

namespace LegacyVault.Application.Services;

public interface IAdministrativeUnitService
{
    /// <summary>Danh sách tỉnh/thành phố cho ô chọn nơi đăng ký khai tử (DEATH-01).</summary>
    List<AdministrativeUnitResponse> GetProvinces();

    /// <summary>Danh sách phường/xã của một tỉnh. Mã tỉnh không có trong danh mục → 404 ADMINISTRATIVE_UNIT_NOT_FOUND.</summary>
    List<AdministrativeUnitResponse> GetWards(string provinceCode);
}

/// <remarks>Dữ liệu tĩnh, ai đã đăng nhập cũng đọc được; không có dữ liệu cá nhân nên không kiểm vai trò.</remarks>
public sealed class AdministrativeUnitService : IAdministrativeUnitService
{
    private readonly IAdministrativeUnitCatalog catalog;

    public AdministrativeUnitService(IAdministrativeUnitCatalog catalog)
    {
        this.catalog = catalog;
    }

    public List<AdministrativeUnitResponse> GetProvinces()
    {
        return catalog.GetProvinces();
    }

    public List<AdministrativeUnitResponse> GetWards(string provinceCode)
    {
        AdministrativeUnitResponse? province = catalog.FindProvince(provinceCode);
        if (province == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AdministrativeUnitNotFound,
                "Không tìm thấy tỉnh/thành phố.");
        }
        return catalog.GetWards(provinceCode);
    }
}

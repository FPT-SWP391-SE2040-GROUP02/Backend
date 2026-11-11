using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Infrastructure.Reference;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Danh mục tỉnh/phường đọc từ tệp nhúng (không cần DB, không gọi dịch vụ ngoài).</summary>
public sealed class AdministrativeUnitCatalogTests
{
    private readonly AdministrativeUnitCatalog catalog = new AdministrativeUnitCatalog();

    [Fact]
    public void Catalog_HasThirtyFourProvincesAndAllWards()
    {
        // Hành động
        List<AdministrativeUnitResponse> provinces = catalog.GetProvinces();
        int wardCount = 0;
        foreach (AdministrativeUnitResponse province in provinces)
        {
            wardCount = wardCount + catalog.GetWards(province.Code).Count;
        }

        // Kiểm tra: nghị quyết 388/NQ-UBTVQH16 có 34 tỉnh/thành phố và 3321 phường/xã/đặc khu
        Assert.Equal(34, provinces.Count);
        Assert.Equal(3321, wardCount);
    }

    [Fact]
    public void Catalog_ProvinceCodesAreStringsKeepingLeadingZero()
    {
        // Hành động
        AdministrativeUnitResponse? hanoi = catalog.FindProvince("01");

        // Kiểm tra
        Assert.NotNull(hanoi);
        Assert.Equal("Thành phố Hà Nội", hanoi.Name);
        Assert.Null(catalog.FindProvince("1"));
        Assert.Null(catalog.FindProvince("99"));
    }

    [Fact]
    public void Catalog_WardsBelongToTheirOwnProvinceOnly()
    {
        // Chuẩn bị: 26743 là Phường Bến Thành (TP.HCM, 79); 00004 là Phường Ba Đình (Hà Nội, 01)
        AdministrativeUnitResponse? benThanh = catalog.FindWard("79", "26743");
        AdministrativeUnitResponse? benThanhInHanoi = catalog.FindWard("01", "26743");
        AdministrativeUnitResponse? baDinh = catalog.FindWard("01", "00004");

        // Kiểm tra
        Assert.NotNull(benThanh);
        Assert.Equal("Phường Bến Thành", benThanh.Name);
        Assert.Null(benThanhInHanoi);
        Assert.NotNull(baDinh);
        Assert.Equal("Phường Ba Đình", baDinh.Name);
        Assert.Null(catalog.FindWard("99", "26743"));
    }

    [Fact]
    public void Catalog_GetWardsOfUnknownProvinceIsEmpty()
    {
        Assert.Empty(catalog.GetWards("99"));
    }

    [Fact]
    public void Catalog_EveryWardNameStartsWithACapitalLetter()
    {
        // Tệp gốc có một xã viết thường chữ "xã" ở đầu tên; dữ liệu của hệ thống đã chuẩn hóa viết hoa.
        foreach (AdministrativeUnitResponse province in catalog.GetProvinces())
        {
            foreach (AdministrativeUnitResponse ward in catalog.GetWards(province.Code))
            {
                Assert.True(char.IsUpper(ward.Name[0]), ward.Code + " " + ward.Name);
            }
        }
        AdministrativeUnitResponse? bacSon = catalog.FindWard("20", "06325");
        Assert.NotNull(bacSon);
        Assert.Equal("Xã Bắc Sơn", bacSon.Name);
    }

    [Fact]
    public void Catalog_BuildRegistrationPlaceLowercasesTheFirstLetterOfTheWard()
    {
        // Hành động
        string place = catalog.BuildRegistrationPlace("79", "26743");
        string communePlace = catalog.BuildRegistrationPlace("20", "06325");

        // Kiểm tra
        Assert.Equal("UBND phường Bến Thành, Thành phố Hồ Chí Minh", place);
        Assert.Equal("UBND xã Bắc Sơn, Tỉnh Lạng Sơn", communePlace);
    }

    [Fact]
    public void Catalog_BuildRegistrationPlaceIsEmptyWhenCodesAreMissingOrInvalid()
    {
        Assert.Equal(string.Empty, catalog.BuildRegistrationPlace(null, null));
        Assert.Equal(string.Empty, catalog.BuildRegistrationPlace("79", null));
        Assert.Equal(string.Empty, catalog.BuildRegistrationPlace(null, "26743"));
        Assert.Equal(string.Empty, catalog.BuildRegistrationPlace("99", "26743"));
        // Phường của tỉnh khác
        Assert.Equal(string.Empty, catalog.BuildRegistrationPlace("01", "26743"));
    }

    [Fact]
    public void Catalog_ReturnedListsAreCopies()
    {
        // Chuẩn bị
        List<AdministrativeUnitResponse> first = catalog.GetProvinces();

        // Hành động: sửa danh sách trả về không làm hỏng danh mục dùng chung
        first.Clear();

        // Kiểm tra
        Assert.Equal(34, catalog.GetProvinces().Count);
    }
}

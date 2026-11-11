using System.Reflection;
using System.Text.Json;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;

namespace LegacyVault.Infrastructure.Reference;

/// <summary>
/// Danh mục đơn vị hành chính đọc từ tệp JSON nhúng trong assembly (Data/Reference/vn-administrative-units.json).
/// Đọc một lần khi dựng đối tượng (đăng ký Singleton); không gọi dịch vụ ngoài lúc chạy.
/// </summary>
/// <remarks>
/// Nguồn: GitHub thanglequoc/vietnamese-provinces-database, theo nghị quyết 388/NQ-UBTVQH16 (34 tỉnh, 3321 phường/xã/đặc khu).
/// </remarks>
public sealed class AdministrativeUnitCatalog : IAdministrativeUnitCatalog
{
    private const string ResourceName = "LegacyVault.Infrastructure.Data.Reference.vn-administrative-units.json";

    private readonly List<AdministrativeUnitResponse> provinces = new List<AdministrativeUnitResponse>();
    private readonly Dictionary<string, AdministrativeUnitResponse> provinceByCode =
        new Dictionary<string, AdministrativeUnitResponse>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdministrativeUnitResponse>> wardsByProvince =
        new Dictionary<string, List<AdministrativeUnitResponse>>(StringComparer.Ordinal);

    public AdministrativeUnitCatalog()
    {
        Assembly assembly = typeof(AdministrativeUnitCatalog).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException("Thiếu tệp dữ liệu đơn vị hành chính nhúng trong assembly.");
        }

        JsonSerializerOptions options = new JsonSerializerOptions();
        options.PropertyNameCaseInsensitive = true;
        List<ProvinceRecord>? records = JsonSerializer.Deserialize<List<ProvinceRecord>>(stream, options);
        if (records == null)
        {
            throw new InvalidOperationException("Tệp dữ liệu đơn vị hành chính rỗng.");
        }

        foreach (ProvinceRecord record in records)
        {
            AdministrativeUnitResponse province = new AdministrativeUnitResponse();
            province.Code = record.Code;
            province.Name = record.Name;
            provinces.Add(province);
            provinceByCode[province.Code] = province;

            List<AdministrativeUnitResponse> wards = new List<AdministrativeUnitResponse>();
            foreach (WardRecord wardRecord in record.Wards)
            {
                AdministrativeUnitResponse ward = new AdministrativeUnitResponse();
                ward.Code = wardRecord.Code;
                ward.Name = wardRecord.Name;
                wards.Add(ward);
            }
            wardsByProvince[province.Code] = wards;
        }
    }

    public List<AdministrativeUnitResponse> GetProvinces()
    {
        return new List<AdministrativeUnitResponse>(provinces);
    }

    public List<AdministrativeUnitResponse> GetWards(string provinceCode)
    {
        List<AdministrativeUnitResponse>? wards;
        if (!wardsByProvince.TryGetValue(provinceCode, out wards))
        {
            return new List<AdministrativeUnitResponse>();
        }
        return new List<AdministrativeUnitResponse>(wards);
    }

    public AdministrativeUnitResponse? FindProvince(string provinceCode)
    {
        AdministrativeUnitResponse? province;
        if (!provinceByCode.TryGetValue(provinceCode, out province))
        {
            return null;
        }
        return province;
    }

    public AdministrativeUnitResponse? FindWard(string provinceCode, string wardCode)
    {
        List<AdministrativeUnitResponse>? wards;
        if (!wardsByProvince.TryGetValue(provinceCode, out wards))
        {
            return null;
        }
        foreach (AdministrativeUnitResponse ward in wards)
        {
            if (string.Equals(ward.Code, wardCode, StringComparison.Ordinal))
            {
                return ward;
            }
        }
        return null;
    }

    public string BuildRegistrationPlace(string? provinceCode, string? wardCode)
    {
        if (string.IsNullOrEmpty(provinceCode) || string.IsNullOrEmpty(wardCode))
        {
            return string.Empty;
        }
        AdministrativeUnitResponse? province = FindProvince(provinceCode);
        if (province == null)
        {
            return string.Empty;
        }
        AdministrativeUnitResponse? ward = FindWard(provinceCode, wardCode);
        if (ward == null)
        {
            return string.Empty;
        }

        // Tên phường/xã viết thường chữ đầu để nằm giữa câu: "Phường Bến Thành" → "phường Bến Thành".
        string wardName = ward.Name;
        if (wardName.Length > 0)
        {
            wardName = char.ToLowerInvariant(wardName[0]) + wardName.Substring(1);
        }
        return "UBND " + wardName + ", " + province.Name;
    }

    private sealed class ProvinceRecord
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public List<WardRecord> Wards { get; set; } = new List<WardRecord>();
    }

    private sealed class WardRecord
    {
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}

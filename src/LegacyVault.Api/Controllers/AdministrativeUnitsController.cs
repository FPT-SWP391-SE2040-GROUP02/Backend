using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Danh mục tỉnh/thành phố và phường/xã (docs/api-contract.md mục 6.10a). Chỉ cần đăng nhập.</summary>
[ApiController]
[Authorize]
[Route("api/v1/administrative-units")]
public sealed class AdministrativeUnitsController : ControllerBase
{
    private readonly IAdministrativeUnitService service;

    public AdministrativeUnitsController(IAdministrativeUnitService service)
    {
        this.service = service;
    }

    /// <summary>Mọi tỉnh/thành phố (34).</summary>
    [HttpGet("provinces")]
    [ProducesResponseType<List<AdministrativeUnitResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public List<AdministrativeUnitResponse> GetProvinces()
    {
        return service.GetProvinces();
    }

    /// <summary>Các phường/xã của một tỉnh.</summary>
    [HttpGet("provinces/{code}/wards")]
    [ProducesResponseType<List<AdministrativeUnitResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public List<AdministrativeUnitResponse> GetWards(string code)
    {
        return service.GetWards(code);
    }
}

using LegacyVault.Application.Dtos;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin/settings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly ISystemSettingService settingService;
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;

    public AdminSettingsController(
        ISystemSettingService settingService,
        ICurrentUser currentUser,
        IUserRepository users)
    {
        this.settingService = settingService;
        this.currentUser = currentUser;
        this.users = users;
    }

    private async Task<(Guid Id, string Email)> RequireAdminAsync(CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var admin = await users.FindByIdAsync(userId, ct);
        if (admin is null || admin.Role != UserRole.Admin || admin.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy nội dung yêu cầu.");
        }
        return (admin.Id, admin.Email);
    }

    [HttpGet]
    [ProducesResponseType<List<SystemSettingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SystemSettingResponse>>> GetAll(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        return Ok(await settingService.GetAllAsync(ct));
    }

    [HttpGet("{key}")]
    [ProducesResponseType<SystemSettingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SystemSettingResponse>> GetByKey(string key, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var setting = await settingService.GetByKeyAsync(key, ct);
        if (setting == null)
        {
            return NotFound(new ProblemDetails { Title = "Không tìm thấy tham số", Detail = $"Tham số '{key}' không tồn tại." });
        }
        return Ok(setting);
    }

    [HttpPut("{key}")]
    [ProducesResponseType<SystemSettingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SystemSettingResponse>> Update(
        string key,
        UpdateSystemSettingRequest request,
        CancellationToken ct)
    {
        var admin = await RequireAdminAsync(ct);
        var updated = await settingService.UpdateAsync(key, request, admin.Email, admin.Id, ct);
        return Ok(updated);
    }

    [HttpPut("batch")]
    [ProducesResponseType<List<SystemSettingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SystemSettingResponse>>> BatchUpdate(
        BatchUpdateSystemSettingsRequest request,
        CancellationToken ct)
    {
        var admin = await RequireAdminAsync(ct);
        var updated = new List<SystemSettingResponse>();
        foreach (var item in request.Items)
        {
            var res = await settingService.UpdateAsync(
                item.Key,
                new UpdateSystemSettingRequest(item.Value, item.IsEnabled, request.Reason),
                admin.Email,
                admin.Id,
                ct);
            updated.Add(res);
        }
        return Ok(updated);
    }

    [HttpPost]
    [ProducesResponseType<SystemSettingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SystemSettingResponse>> Create(
        CreateSystemSettingRequest request,
        CancellationToken ct)
    {
        var admin = await RequireAdminAsync(ct);
        var created = await settingService.CreateAsync(request, admin.Email, ct);
        return Created($"/api/v1/admin/settings/{created.Key}", created);
    }
}

/// <summary>
/// Public/Client settings: cho phép Client đọc các giới hạn quota và cờ tính năng hiện tại.
/// </summary>
[ApiController]
[Route("api/v1/settings")]
public sealed class PublicSettingsController(ISystemSettingService settingService) : ControllerBase
{
    [HttpGet("public")]
    public async Task<ActionResult<Dictionary<string, object>>> GetPublicSettings(CancellationToken ct)
    {
        var all = await settingService.GetAllAsync(ct);
        var result = new Dictionary<string, object>();
        foreach (var s in all.Where(s => s.IsEnabled))
        {
            if (s.DataType == "NUMBER" && long.TryParse(s.Value, out var n))
                result[s.Key] = n;
            else if (s.DataType == "BOOLEAN" && bool.TryParse(s.Value, out var b))
                result[s.Key] = b;
            else
                result[s.Key] = s.Value;
        }
        return Ok(result);
    }
}

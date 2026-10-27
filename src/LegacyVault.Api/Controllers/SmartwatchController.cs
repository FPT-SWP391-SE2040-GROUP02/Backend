using System.ComponentModel.DataAnnotations;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

public sealed class PairSmartwatchRequest
{
    [Required(ErrorMessage = "Thiếu mã ghép nối.")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã ghép nối gồm đúng 6 chữ số.")]
    public string PairingCode { get; set; } = string.Empty;

    /// <summary>Mã định danh thiết bị mà đồng hồ tự khai (vd Build.MODEL); dùng để bám claim token, không bắt buộc.</summary>
    [StringLength(64)]
    public string? DeviceId { get; set; }

    /// <summary>Tên hiển thị của thiết bị (vd "Samsung Galaxy Watch").</summary>
    [StringLength(128)]
    public string? DeviceName { get; set; }
}

public sealed class PairingCodeResponse
{
    public string Code { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

public sealed class SmartwatchPairingStatusResponse
{
    public bool Paired { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public DateTimeOffset? PairedAt { get; set; }
}

public sealed class SmartwatchPairResultResponse
{
    public bool Success { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public Guid VaultId { get; set; }
}

[ApiController]
[Route("api/v1/smartwatch")]
public sealed class SmartwatchController : ControllerBase
{
    private readonly ISmartwatchPairingStore pairingStore;
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IUserRepository userRepository;
    private readonly ITokenService tokenService;

    public SmartwatchController(
        ISmartwatchPairingStore pairingStore,
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IUserRepository userRepository,
        ITokenService tokenService)
    {
        this.pairingStore = pairingStore;
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.userRepository = userRepository;
        this.tokenService = tokenService;
    }

    /// <summary>
    /// Web gọi để sinh mã PIN 6 số hiển thị trên màn hình cho người dùng nhập vào đồng hồ bất kỳ.
    /// </summary>
    [HttpPost("pairing-code")]
    [Authorize]
    [ProducesResponseType<PairingCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PairingCodeResponse>> GeneratePairingCode(CancellationToken ct)
    {
        if (currentUser.UserId == null)
        {
            return Unauthorized();
        }

        Vault? vault = await vaultRepository.FindByOwnerIdAsync(currentUser.UserId.Value, ct);
        if (vault == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy kho của chủ tài khoản.",
                Status = StatusCodes.Status404NotFound
            });
        }

        string code = pairingStore.CreateCode(currentUser.UserId.Value, vault.Id, TimeSpan.FromMinutes(5));
        return Ok(new PairingCodeResponse
        {
            Code = code,
            ExpiresInSeconds = 300
        });
    }

    /// <summary>
    /// Web định kỳ kiểm tra xem mã PIN đã được đồng hồ nào ghép nối thành công chưa.
    /// </summary>
    [HttpGet("pairing-status")]
    [Authorize]
    [ProducesResponseType<SmartwatchPairingStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<SmartwatchPairingStatusResponse> GetPairingStatus([FromQuery] string code)
    {
        if (currentUser.UserId == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Mã ghép nối không được để trống.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (!pairingStore.TryGetStatus(code.Trim(), currentUser.UserId.Value, out var session))
        {
            return Ok(new SmartwatchPairingStatusResponse
            {
                Paired = false,
                Status = "expired"
            });
        }

        if (session.IsPaired)
        {
            return Ok(new SmartwatchPairingStatusResponse
            {
                Paired = true,
                Status = "paired",
                DeviceName = session.DeviceName,
                PairedAt = session.PairedAt
            });
        }

        return Ok(new SmartwatchPairingStatusResponse
        {
            Paired = false,
            Status = "pending"
        });
    }

    /// <summary>
    /// Đồng hồ bất kỳ gọi API này kèm mã 6 số để liên kết với tài khoản tương ứng.
    /// </summary>
    [HttpPost("pair")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType<SmartwatchPairResultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SmartwatchPairResultResponse>> PairDevice(
        [FromBody] PairSmartwatchRequest request, CancellationToken ct)
    {
        string? deviceLabel = !string.IsNullOrWhiteSpace(request.DeviceName)
            ? request.DeviceName
            : request.DeviceId;

        if (!pairingStore.TryPair(request.PairingCode.Trim(), deviceLabel, out var session))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Mã ghép nối không hợp lệ hoặc đã hết hạn (chỉ có hiệu lực trong 5 phút).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        User? user = await userRepository.FindByIdAsync(session.UserId, ct);
        if (user == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Không tìm thấy thông tin chủ tài khoản.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // R2 (audit 2026-10-27): cấp token thiết bị hạn chế phạm vi, KHÔNG phải JWT chủ kho đầy đủ quyền.
        // Thiết bị chỉ gọi được /check-ins/smartwatch (role claim "SmartwatchDevice" + scope check-in:write).
        AccessToken token = tokenService.CreateSmartwatchToken(user, session.VaultId, request.DeviceId ?? "unknown-device");

        return Ok(new SmartwatchPairResultResponse
        {
            Success = true,
            AccessToken = token.Value,
            ExpiresIn = (int)Math.Max(0, (token.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds),
            OwnerEmail = user.Email,
            VaultId = session.VaultId
        });
    }
}

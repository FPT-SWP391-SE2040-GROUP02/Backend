using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>
/// Bộ kiểm mã PIN cho qua hết, ApiFactory dùng khi <see cref="ApiFactory.EnforceVaultPin"/> = false.
/// Chỉ hai hàm mà các API khác gọi vào (kiểm PIN, chặn thêm tài sản) được cài đặt; các API quản lý PIN
/// không dùng được ở chế độ này.
/// </summary>
public sealed class PassthroughVaultPinService : IVaultPinService
{
    public Task VerifyAsync(Guid vaultId, string? pin, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task EnsurePinSetBeforeNewAssetAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<VaultPinStatusResponse> GetStatusAsync(Guid vaultId, CancellationToken ct)
    {
        throw new NotSupportedException("Bật ApiFactory.EnforceVaultPin để dùng API PIN.");
    }

    public Task<VaultPinStatusResponse> SetPinAsync(Guid vaultId, SetVaultPinRequest request, CancellationToken ct)
    {
        throw new NotSupportedException("Bật ApiFactory.EnforceVaultPin để dùng API PIN.");
    }

    public Task<VaultPinStatusResponse> ChangePinAsync(Guid vaultId, ChangeVaultPinRequest request, CancellationToken ct)
    {
        throw new NotSupportedException("Bật ApiFactory.EnforceVaultPin để dùng API PIN.");
    }

    public Task<VaultPinStatusResponse> ResetPinAsync(Guid vaultId, ResetVaultPinRequest request, CancellationToken ct)
    {
        throw new NotSupportedException("Bật ApiFactory.EnforceVaultPin để dùng API PIN.");
    }
}

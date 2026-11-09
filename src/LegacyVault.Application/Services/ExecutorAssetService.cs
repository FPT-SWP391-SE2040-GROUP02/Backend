using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class ExecutorAssetService : IExecutorAssetService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorReader executorReader;
    private readonly IExecutorAssetReader assetReader;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;

    public ExecutorAssetService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorReader executorReader,
        IExecutorAssetReader assetReader,
        IBeneficiaryDataProtector beneficiaryProtector)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.executorReader = executorReader;
        this.assetReader = assetReader;
        this.beneficiaryProtector = beneficiaryProtector;
    }

    public async Task<List<ExecutorVaultPackageResponse>> ListPackagesAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault == null || !await IsActiveExecutorAsync(vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        List<Container> packages = await assetReader.FindReadyPackagesAsync(vault.Id, ct);
        List<ExecutorVaultPackageResponse> responses = new List<ExecutorVaultPackageResponse>();
        foreach (Container package in packages)
        {
            responses.Add(ToResponse(package));
        }
        return responses;
    }

    private ExecutorVaultPackageResponse ToResponse(Container package)
    {
        ExecutorVaultPackageResponse response = new ExecutorVaultPackageResponse();
        response.ContainerId = package.Id;
        response.ContainerName = package.Name;

        List<Designation> designations = new List<Designation>(package.Designations);
        designations.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
        foreach (Designation designation in designations)
        {
            ExecutorPackageRecipientResponse recipient = new ExecutorPackageRecipientResponse();
            recipient.CitizenIdLast4 = designation.Beneficiary.CitizenIdLast4;
            recipient.FullName = ReadFullName(designation.Beneficiary);
            response.Recipients.Add(recipient);
        }

        List<ContainerAsset> links = new List<ContainerAsset>(package.Assets);
        links.Sort((left, right) => left.AddedAt.CompareTo(right.AddedAt));
        foreach (ContainerAsset link in links)
        {
            Asset asset = link.Asset;
            if (asset.ContentDeletedAt != null)
            {
                continue;
            }
            ExecutorPackageAssetResponse item = new ExecutorPackageAssetResponse();
            item.Id = asset.Id;
            item.Name = asset.Name;
            item.Kind = ToKind(asset.Kind);
            item.SizeBytes = asset.SizeBytes;
            response.Assets.Add(item);
        }
        return response;
    }

    private static string ToKind(AssetKind kind)
    {
        if (kind == AssetKind.Text)
        {
            return "TEXT";
        }
        return "FILE";
    }

    /// <summary>Họ tên người nhận; không giải mã được thì null để màn hình hiện 4 số cuối CCCD thay vì lỗi cả danh sách.</summary>
    private string? ReadFullName(Beneficiary beneficiary)
    {
        try
        {
            BeneficiaryPersonalData personal = beneficiaryProtector.Unprotect(beneficiary);
            return personal.FullName;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private async Task<bool> IsActiveExecutorAsync(Vault vault, Guid userId, CancellationToken ct)
    {
        if (vault.OwnerId == userId)
        {
            return false;
        }
        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId == userId)
            {
                return true;
            }
        }
        return false;
    }
}

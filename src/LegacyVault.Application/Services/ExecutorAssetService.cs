using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Màn Tài sản của Người thực thi: các gói sẵn sàng của kho, chỉ có tên, không có nội dung tài sản.</summary>
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

    // Màn Tài sản của Người thực thi: các gói sẵn sàng của kho, mỗi gói kèm người nhận và tên tài sản (không có nội dung).
    public async Task<List<ExecutorVaultPackageResponse>> ListPackagesAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Người gọi phải là Người thực thi đang phụ trách kho; không thì báo không tìm thấy kho
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault == null || !await HandoverAccess.IsActiveExecutorAsync(executorReader, vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        // Trước khi luật sư chốt biên bản bàn giao, Người thực thi chưa có quyền gì: không xem được tài sản,
        // tránh lạm quyền khi Chủ kho còn sống.
        if (vault.Plan.Status != PlanStatus.HandoverInProgress && vault.Plan.Status != PlanStatus.Closed)
        {
            throw HandoverAccess.NotAllowed("HANDOVER_NOT_STARTED",
                "Chỉ xem được tài sản sau khi luật sư chốt biên bản bàn giao.");
        }

        // 2. Lấy các gói sẵn sàng rồi dựng kết quả từng gói (bỏ tài sản đã bị xóa nội dung)
        List<Container> packages = await assetReader.FindReadyPackagesAsync(vault.Id, ct);
        List<ExecutorVaultPackageResponse> responses = new List<ExecutorVaultPackageResponse>();
        foreach (Container package in packages)
        {
            responses.Add(ToResponse(package));
        }
        return responses;
    }

    /// <summary>Đổi một gói sang dữ liệu trả ra màn hình: các người nhận (theo ngày tạo) và các tài sản còn nội dung (theo ngày thêm). Không có nội dung tài sản.</summary>
    private ExecutorVaultPackageResponse ToResponse(Container package)
    {
        ExecutorVaultPackageResponse response = new ExecutorVaultPackageResponse
        {
            ContainerId = package.Id,
            ContainerName = package.Name
        };

        List<Designation> designations = package.Designations.OrderBy(designation => designation.CreatedAt).ToList();
        foreach (Designation designation in designations)
        {
            ExecutorPackageRecipientResponse recipient = new ExecutorPackageRecipientResponse
            {
                CitizenId = ReadCitizenId(designation.Beneficiary),
                FullName = ReadFullName(designation.Beneficiary)
            };
            response.Recipients.Add(recipient);
        }

        List<ContainerAsset> links = package.Assets.OrderBy(link => link.AddedAt).ToList();
        foreach (ContainerAsset link in links)
        {
            Asset asset = link.Asset;
            if (asset.ContentDeletedAt != null)
            {
                continue;
            }
            ExecutorPackageAssetResponse item = new ExecutorPackageAssetResponse
            {
                Id = asset.Id,
                Name = asset.Name,
                Kind = ToKind(asset.Kind),
                SizeBytes = asset.SizeBytes
            };
            response.Assets.Add(item);
        }
        return response;
    }

    /// <summary>Loại tài sản dạng chữ trả ra API: TEXT cho ghi chú văn bản, FILE cho tệp.</summary>
    private static string ToKind(AssetKind kind)
    {
        if (kind == AssetKind.Text)
        {
            return "TEXT";
        }
        return "FILE";
    }

    /// <summary>Họ tên người nhận; không giải mã được thì null để màn hình hiện số CCCD thay vì lỗi cả danh sách.</summary>
    /// <summary>Số CCCD đầy đủ của người nhận; không giải mã được thì chuỗi rỗng.</summary>
    private string ReadCitizenId(Beneficiary beneficiary)
    {
        try
        {
            return beneficiaryProtector.Unprotect(beneficiary).CitizenId;
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

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
}

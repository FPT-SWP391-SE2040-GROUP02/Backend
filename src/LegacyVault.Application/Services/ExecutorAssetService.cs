using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Màn Tài sản của Người thực thi, chỉ có danh sách, không có nội dung tài sản: trước khi bàn giao chỉ tên và loại,
/// từ lúc luật sư chốt biên bản thêm người nhận kèm tài sản được gán.
/// </summary>
public sealed class ExecutorAssetService : IExecutorAssetService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorReader executorReader;
    private readonly IAssetAssignmentRepository assignmentRepository;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly IHandoverRecordRepository recordRepository;

    public ExecutorAssetService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorReader executorReader,
        IAssetAssignmentRepository assignmentRepository,
        IBeneficiaryDataProtector beneficiaryProtector,
        IHandoverRecordRepository recordRepository)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.executorReader = executorReader;
        this.assignmentRepository = assignmentRepository;
        this.beneficiaryProtector = beneficiaryProtector;
        this.recordRepository = recordRepository;
    }

    // Màn Tài sản của Người thực thi: các người nhận của kho, mỗi người kèm tên tài sản được gán (không có nội dung).
    public async Task<List<ExecutorVaultRecipientResponse>> ListRecipientsAsync(Guid vaultId, CancellationToken ct)
    {
        // 1. Người gọi phải là Người thực thi đang phụ trách kho; không thì báo không tìm thấy kho
        Vault vault = await FindMyExecutorVaultAsync(vaultId, ct);

        // Trước khi luật sư chốt biên bản bàn giao, Người thực thi chưa được xem người nhận (họ tên, CCCD) và ai nhận
        // tài sản nào, tránh lạm quyền khi Chủ kho còn sống. Lúc đó chỉ xem được tên và loại qua ListAssetNamesAsync.
        if (vault.Plan.Status != PlanStatus.HandoverInProgress && vault.Plan.Status != PlanStatus.Closed)
        {
            throw HandoverAccess.NotAllowed("HANDOVER_NOT_STARTED",
                "Chỉ xem được tài sản sau khi luật sư chốt biên bản bàn giao.");
        }

        // 2. Lấy các dòng gán của kho rồi gom theo người nhận (theo thứ tự gán), bỏ tài sản đã bị xóa nội dung
        List<AssetAssignment> assignments = await assignmentRepository.FindDetailedByVaultIdAsync(vault.Id, ct);
        Dictionary<Guid, int> recipientCountByAsset = new Dictionary<Guid, int>();
        foreach (AssetAssignment assignment in assignments)
        {
            if (recipientCountByAsset.ContainsKey(assignment.AssetId))
            {
                recipientCountByAsset[assignment.AssetId] = recipientCountByAsset[assignment.AssetId] + 1;
            }
            else
            {
                recipientCountByAsset[assignment.AssetId] = 1;
            }
        }

        List<ExecutorVaultRecipientResponse> responses = new List<ExecutorVaultRecipientResponse>();
        Dictionary<Guid, ExecutorVaultRecipientResponse> byBeneficiary = new Dictionary<Guid, ExecutorVaultRecipientResponse>();
        foreach (AssetAssignment assignment in assignments)
        {
            Asset asset = assignment.Asset;
            if (asset.ContentDeletedAt != null)
            {
                continue;
            }
            if (!byBeneficiary.ContainsKey(assignment.BeneficiaryId))
            {
                ExecutorVaultRecipientResponse recipient = new ExecutorVaultRecipientResponse
                {
                    BeneficiaryId = assignment.BeneficiaryId,
                    CitizenId = ReadCitizenId(assignment.Beneficiary),
                    FullName = ReadFullName(assignment.Beneficiary)
                };
                byBeneficiary[assignment.BeneficiaryId] = recipient;
                responses.Add(recipient);
            }
            ExecutorRecipientAssetResponse item = new ExecutorRecipientAssetResponse
            {
                Id = asset.Id,
                Name = asset.Name,
                Kind = ToKind(asset.Kind),
                SizeBytes = asset.SizeBytes,
                SharedWithCount = recipientCountByAsset[asset.Id]
            };
            byBeneficiary[assignment.BeneficiaryId].Assets.Add(item);
        }
        return responses;
    }

    // Tên và loại tài sản: Người thực thi đang phụ trách kho xem được cả trước khi bàn giao để biết mình giữ hộ những gì.
    public async Task<List<ExecutorAssetNameResponse>> ListAssetNamesAsync(Guid vaultId, CancellationToken ct)
    {
        Vault vault = await FindMyExecutorVaultAsync(vaultId, ct);

        // Gồm cả tài sản chưa gán cho ai; tài sản đã xóa nội dung đã được lọc ở câu truy vấn
        List<Asset> assets = await recordRepository.ListVaultAssetsAsync(vault.Id, ct);
        List<ExecutorAssetNameResponse> responses = new List<ExecutorAssetNameResponse>();
        foreach (Asset asset in assets)
        {
            ExecutorAssetNameResponse item = new ExecutorAssetNameResponse
            {
                Name = asset.Name,
                Kind = ToKind(asset.Kind)
            };
            responses.Add(item);
        }
        return responses;
    }

    /// <summary>Kho mà người đang đăng nhập là Người thực thi đang hiệu lực; kho lạ và người ngoài cùng nhận một lỗi.</summary>
    private async Task<Vault> FindMyExecutorVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        if (vault == null || !await HandoverAccess.IsActiveExecutorAsync(executorReader, vault, userId, ct))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }
        return vault;
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

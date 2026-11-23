using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Màn Tài sản của Người thực thi: các người nhận của kho kèm tài sản được gán, chỉ có tên, không có nội dung tài sản.</summary>
public sealed class ExecutorAssetService : IExecutorAssetService
{
    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IExecutorReader executorReader;
    private readonly IAssetAssignmentRepository assignmentRepository;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;

    public ExecutorAssetService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IExecutorReader executorReader,
        IAssetAssignmentRepository assignmentRepository,
        IBeneficiaryDataProtector beneficiaryProtector)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.executorReader = executorReader;
        this.assignmentRepository = assignmentRepository;
        this.beneficiaryProtector = beneficiaryProtector;
    }

    // Màn Tài sản của Người thực thi: các người nhận của kho, mỗi người kèm tên tài sản được gán (không có nội dung).
    public async Task<List<ExecutorVaultRecipientResponse>> ListRecipientsAsync(Guid vaultId, CancellationToken ct)
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

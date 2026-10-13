using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <remarks>
/// Chưa có module người nhận nên số chỉ định luôn là 0 (gói luôn DRAFT, SETUP-01);
/// nhánh người nhận sẽ truyền số thật vào ContainerRules và ToResponse.
/// </remarks>
public sealed class ContainerService : IContainerService
{
    private const string DefaultSortBy = "createdAt";

    /// <summary>Chưa có module người nhận.</summary>
    private const int NoRecipients = 0;

    private static readonly Dictionary<string, string> SortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["createdAt"] = "createdAt",
        ["name"] = "name",
    };

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IContainerRepository containerRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ContainerService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IContainerRepository containerRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.containerRepository = containerRepository;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<ContainerResponse> CreateContainerAsync(
        Guid vaultId, CreateContainerRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: tạo gói là thao tác "thêm". SETUP-17: hạng Free vẫn được tạo gói rỗng nên không kiểm hạng.
        VaultRules.EnsureEditable(vault);

        string? name = TextInput.NullIfBlank(request.Name);
        if (name == null)
        {
            throw ValidationErrors.For("name", "Tên gói không được để trống.");
        }

        // NFR-14: tối đa 50 gói chưa lưu trữ.
        // Hạn chế đã biết: hai request tạo đồng thời có thể cùng qua bước đếm này và vượt 50
        // (chờ chốt concurrency token cho HandoverPlan.ConfigVersion).
        int activeCount = await containerRepository.CountActiveByVaultIdAsync(vault.Id, ct);
        if (activeCount >= ContainerRules.MaxActiveContainers)
        {
            throw LimitExceeded();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Container container = new Container();
        container.VaultId = vault.Id;
        container.Name = name;
        container.Description = TextInput.NullIfBlank(request.Description);
        container.Status = ContainerStatus.Draft;
        container.CreatedAt = now;
        // Gói mới chưa sửa lần nào: UpdatedAt = null → etag "0" (docs/api-contract.md mục 6.6).
        ContainerRules.RecalculateStatus(container, NoRecipients);

        containerRepository.Add(container);
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse(NoRecipients);
    }

    public async Task<PaginatedList<ContainerResponse>> GetContainersAsync(
        Guid vaultId, bool includeArchived, PaginationRequest pagination, CancellationToken ct)
    {
        // Xem không cần kho đang cho sửa, chỉ cần kho thuộc người dùng.
        await LoadOwnedVaultAsync(vaultId, ct);

        string sortBy = DefaultSortBy;
        if (!string.IsNullOrWhiteSpace(pagination.SortBy))
        {
            string? column;
            if (!SortColumns.TryGetValue(pagination.SortBy, out column))
            {
                throw ValidationErrors.For("sortBy", "Chỉ sắp xếp theo name hoặc createdAt.");
            }
            sortBy = column;
        }

        (List<Container> Items, int TotalCount) page = await containerRepository.FindPageByVaultIdAsync(
            vaultId, includeArchived, pagination.PageIndex, pagination.PageSize,
            TextInput.NullIfBlank(pagination.SearchTerm), sortBy, pagination.IsAscending, ct);

        List<ContainerResponse> responses = new List<ContainerResponse>();
        foreach (Container container in page.Items)
        {
            responses.Add(container.ToResponse(NoRecipients));
        }
        return responses.ToPaginatedList(page.TotalCount, pagination);
    }

    public async Task<ContainerResponse> GetContainerAsync(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);
        return container.ToResponse(NoRecipients);
    }

    public async Task<ContainerResponse> UpdateContainerAsync(
        Guid vaultId, Guid containerId, UpdateContainerRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: đổi tên/mô tả gói là thao tác "thêm".
        VaultRules.EnsureEditable(vault);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);

        if (container.Status == ContainerStatus.Archived)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.ContainerArchived,
                "Gói đã lưu trữ, không sửa được.");
        }
        if (!Etag.Matches(request.Etag, container.UpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Gói vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
        }

        // null = giữ nguyên; description "" = xóa mô tả.
        bool changed = false;
        if (request.Name != null)
        {
            string name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw ValidationErrors.For("name", "Tên gói không được để trống.");
            }
            if (name != container.Name)
            {
                container.Name = name;
                changed = true;
            }
        }
        if (request.Description != null)
        {
            string? description = TextInput.NullIfBlank(request.Description);
            if (description != container.Description)
            {
                container.Description = description;
                changed = true;
            }
        }

        // Không có gì đổi: không lưu, không tăng ConfigVersion.
        if (!changed)
        {
            return container.ToResponse(NoRecipients);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        container.UpdatedAt = now;
        // SETUP-04: đổi tên/mô tả gói cũng tăng ConfigVersion.
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse(NoRecipients);
    }

    public async Task<ContainerResponse> ArchiveContainerAsync(
        Guid vaultId, Guid containerId, ConfirmPlanImpactRequest? request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: lưu trữ là thao tác "giảm", vẫn cho khi dịch vụ đang ân hạn.
        VaultRules.EnsureCanReduce(vault);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);

        // Đã lưu trữ: trả nguyên trạng, không lưu, không tăng ConfigVersion.
        if (container.Status == ContainerStatus.Archived)
        {
            return container.ToResponse(NoRecipients);
        }

        bool confirmed = false;
        if (request != null)
        {
            confirmed = request.ConfirmPlanDeactivation;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        // SETUP-16: lưu trữ gói READY có thể làm kế hoạch mất gói READY cuối cùng.
        bool willLoseReady = container.Status == ContainerStatus.Ready;
        await EnsurePlanImpactConfirmedAsync(vault, willLoseReady, confirmed, now, ct);

        // SETUP-16: gói lưu trữ không còn tài sản (tài sản không bị xóa, QUOTA-02).
        // Chép ra danh sách riêng vì RemoveAssetLink có thể làm đổi container.Assets.
        List<ContainerAsset> links = new List<ContainerAsset>(container.Assets);
        foreach (ContainerAsset link in links)
        {
            containerRepository.RemoveAssetLink(link);
        }

        container.Status = ContainerStatus.Archived;
        container.ArchivedAt = now;
        container.UpdatedAt = now;
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse(NoRecipients);
    }

    public Task<ContainerResponse> AddAssetAsync(
        Guid vaultId, Guid containerId, AddContainerAssetRequest request, CancellationToken ct)
    {
        throw new NotImplementedException("Làm ở nhánh feature/container-assets");
    }

    public Task<ContainerResponse> RemoveAssetAsync(
        Guid vaultId, Guid containerId, Guid assetId, ConfirmPlanImpactRequest? request, CancellationToken ct)
    {
        throw new NotImplementedException("Làm ở nhánh feature/container-assets");
    }

    /// <summary>
    /// SETUP-16: kế hoạch đang chạy mà thao tác làm mất gói READY cuối cùng thì phải được Owner xác nhận.
    /// Chưa xác nhận → 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED; đã xác nhận → kế hoạch về DRAFT.
    /// Dùng chung cho lưu trữ gói, gỡ tài sản, gỡ người nhận.
    /// </summary>
    private async Task EnsurePlanImpactConfirmedAsync(
        Vault vault, bool willLoseReady, bool confirmed, DateTimeOffset now, CancellationToken ct)
    {
        if (!willLoseReady || !PlanRules.IsRunning(vault.Plan.Status))
        {
            return;
        }

        int readyCount = await containerRepository.CountReadyByVaultIdAsync(vault.Id, ct);
        if (readyCount != 1)
        {
            return;
        }

        if (!confirmed)
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.PlanDeactivationConfirmationRequired,
                "Thao tác này làm kế hoạch không còn gói sẵn sàng và kế hoạch sẽ trở về bản nháp. Vui lòng xác nhận.");
        }
        PlanRules.Deactivate(vault.Plan, now);
    }

    /// <summary>
    /// SETUP-04: mọi thay đổi gói tăng ConfigVersion.
    /// Hạn chế đã biết: HandoverPlan chưa có concurrency token nên hai request đồng thời có thể làm mất một lần tăng
    /// (chờ chốt concurrency token cho HandoverPlan.ConfigVersion).
    /// </summary>
    private static void BumpConfigVersion(Vault vault, DateTimeOffset now)
    {
        PlanRules.BumpConfigVersion(vault.Plan, now);
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    /// <summary>Gói không có hoặc thuộc kho khác → 404 CONTAINER_NOT_FOUND (SEC-04).</summary>
    private async Task<Container> LoadContainerAsync(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        Container? container = await containerRepository.FindByIdAsync(vaultId, containerId, ct);
        if (container == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.ContainerNotFound, "Không tìm thấy gói.");
        }
        return container;
    }

    private static AppException LimitExceeded()
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["maxContainers"] = ContainerRules.MaxActiveContainers;
        return new AppException(AppErrorType.Conflict, ErrorCode.ContainerLimitExceeded,
            "Kho đã có đủ 50 gói chưa lưu trữ.", extensions);
    }
}

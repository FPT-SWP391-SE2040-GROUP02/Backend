using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class ContainerService : IContainerService
{
    private const string DefaultSortBy = "createdAt";

    private static readonly Dictionary<string, string> SortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["createdAt"] = "createdAt",
        ["name"] = "name",
    };

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IContainerRepository containerRepository;
    private readonly IAssetRepository assetRepository;
    private readonly IBeneficiaryRepository beneficiaryRepository;
    private readonly ICheckInScheduler checkInScheduler;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public ContainerService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IContainerRepository containerRepository,
        IAssetRepository assetRepository,
        IBeneficiaryRepository beneficiaryRepository,
        ICheckInScheduler checkInScheduler,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.containerRepository = containerRepository;
        this.assetRepository = assetRepository;
        this.beneficiaryRepository = beneficiaryRepository;
        this.checkInScheduler = checkInScheduler;
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
        ContainerRules.RecalculateStatus(container);

        containerRepository.Add(container);
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse();
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
            responses.Add(container.ToResponse());
        }
        return responses.ToPaginatedList(page.TotalCount, pagination);
    }

    public async Task<ContainerResponse> GetContainerAsync(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);
        return container.ToResponse();
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
            throw Archived();
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
            return container.ToResponse();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        container.UpdatedAt = now;
        // SETUP-04: đổi tên/mô tả gói cũng tăng ConfigVersion.
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse();
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
            return container.ToResponse();
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
        // SETUP-16: gói lưu trữ cũng không còn người nhận (hồ sơ không bị xóa). Người không còn ở gói nào
        // thì tự hết chiếm suất vì suất luôn đếm lại từ chỉ định (QUOTA-03).
        List<Designation> designations = new List<Designation>(container.Designations);
        foreach (Designation designation in designations)
        {
            containerRepository.RemoveDesignation(designation);
        }

        container.Status = ContainerStatus.Archived;
        container.ArchivedAt = now;
        container.UpdatedAt = now;
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse();
    }

    public async Task<ContainerResponse> AddAssetAsync(
        Guid vaultId, Guid containerId, AddContainerAssetRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: thêm tài sản vào gói là thao tác "thêm".
        VaultRules.EnsureEditable(vault);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);

        if (container.Status == ContainerStatus.Archived)
        {
            throw Archived();
        }
        // SETUP-17 / AC-04: hạng Free không được đưa tài sản vào gói; chặn ở API, không chỉ ẩn nút.
        if (vault.Entitlement.Tier == ServiceTier.Free)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.PaidTierRequired,
                "Hạng miễn phí chưa được đưa tài sản vào gói. Vui lòng nâng cấp dịch vụ.");
        }

        Guid assetId = request.AssetId;
        Asset? asset = await assetRepository.FindByIdAsync(vault.Id, assetId, ct);
        if (asset == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.AssetNotFound, "Không tìm thấy tài sản.");
        }

        // SETUP-03: một tài sản thuộc tối đa một gói chưa lưu trữ (gói lưu trữ không còn liên kết nào).
        ContainerAsset? existing = await containerRepository.FindLinkByAssetIdAsync(assetId, ct);
        if (existing != null)
        {
            if (existing.ContainerId == container.Id)
            {
                // Đã ở đúng gói này: trả nguyên trạng, không ghi DB, không tăng ConfigVersion.
                return container.ToResponse();
            }
            throw AlreadyInContainer(existing.ContainerId);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ContainerAsset link = new ContainerAsset();
        link.ContainerId = container.Id;
        link.AssetId = assetId;
        link.AddedAt = now;
        // Thêm vào danh sách trước để RecalculateStatus và assetIds trong response tính cả tài sản mới.
        container.Assets.Add(link);
        container.UpdatedAt = now;
        ContainerRules.RecalculateStatus(container);
        BumpConfigVersion(vault, now);

        // Lưu liên kết cùng các thay đổi đang chờ của Container và Plan trong một lần SaveChanges.
        // Hai request cùng đưa tài sản vào CÙNG gói có thể vấp concurrency token Container.UpdatedAt
        // → GlobalExceptionHandler trả 409 EDIT_CONFLICT; không bắt ở đây.
        bool added = await containerRepository.TryAddAssetLinkAsync(link, ct);
        if (added)
        {
            return container.ToResponse();
        }

        // Unique AssetId chặn: request khác vừa đưa tài sản này vào một gói.
        // Container/Plan đang theo dõi đã bị sửa trong bộ nhớ nhưng KHÔNG được lưu, nên bỏ hết rồi đọc lại từ DB.
        containerRepository.DiscardPendingChanges();
        ContainerAsset? winner = await containerRepository.FindLinkByAssetIdAsync(assetId, ct);
        if (winner == null)
        {
            // Liên kết của request kia đã bị gỡ ngay sau đó: không đoán được trạng thái đúng,
            // báo dữ liệu vừa thay đổi để FE tải lại.
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa thay đổi. Vui lòng tải lại.");
        }
        if (winner.ContainerId != containerId)
        {
            throw AlreadyInContainer(winner.ContainerId);
        }

        // Request kia đưa đúng vào gói này: trả kết quả dựng từ dữ liệu mới đọc lại.
        Container reloaded = await LoadContainerAsync(vault.Id, containerId, ct);
        return reloaded.ToResponse();
    }

    public async Task<ContainerResponse> RemoveAssetAsync(
        Guid vaultId, Guid containerId, Guid assetId, ConfirmPlanImpactRequest? request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: gỡ tài sản là thao tác "giảm", vẫn cho khi dịch vụ đang ân hạn.
        VaultRules.EnsureCanReduce(vault);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);

        if (container.Status == ContainerStatus.Archived)
        {
            throw Archived();
        }

        ContainerAsset? link = FindLinkInContainer(container, assetId);
        if (link == null)
        {
            // Tài sản không ở trong gói: trả nguyên trạng, không ghi DB.
            return container.ToResponse();
        }

        bool confirmed = false;
        if (request != null)
        {
            confirmed = request.ConfirmPlanDeactivation;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        // SETUP-16: gỡ tài sản cuối cùng của gói READY làm gói về DRAFT, có thể làm kế hoạch mất gói READY cuối cùng.
        bool willLoseReady = container.Status == ContainerStatus.Ready && container.Assets.Count == 1;
        await EnsurePlanImpactConfirmedAsync(vault, willLoseReady, confirmed, now, ct);

        // QUOTA-02: chỉ gỡ liên kết; không xóa tài sản, không đổi dung lượng đã dùng.
        // Gỡ cả khỏi danh sách để RecalculateStatus và assetIds trong response đúng ngay.
        container.Assets.Remove(link);
        containerRepository.RemoveAssetLink(link);
        container.UpdatedAt = now;
        ContainerRules.RecalculateStatus(container);
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse();
    }

    public async Task<ContainerResponse> AddBeneficiaryAsync(
        Guid vaultId, Guid containerId, AddContainerBeneficiaryRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: chỉ định người nhận là thao tác "thêm".
        VaultRules.EnsureEditable(vault);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);

        if (container.Status == ContainerStatus.Archived)
        {
            throw Archived();
        }
        // SETUP-17 / AC-04: hạng Free không được thêm người nhận; chặn ở API, không chỉ ẩn nút.
        if (vault.Entitlement.Tier == ServiceTier.Free)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.PaidTierRequired,
                "Hạng miễn phí chưa được thêm người nhận vào gói. Vui lòng nâng cấp dịch vụ.");
        }

        Guid beneficiaryId = request.BeneficiaryId;
        Beneficiary? beneficiary = await beneficiaryRepository.FindByIdAsync(vault.Id, beneficiaryId, ct);
        if (beneficiary == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.BeneficiaryNotFound, "Không tìm thấy người nhận.");
        }

        if (FindDesignationInContainer(container, beneficiaryId) != null)
        {
            // Đã ở đúng gói này: trả nguyên trạng, không ghi DB, không tăng ConfigVersion.
            return container.ToResponse();
        }

        // QUOTA-03 / QUOTA-05: suất đếm theo NGƯỜI khác nhau có chỉ định trong kho.
        // Người đã ở gói khác thì không tốn thêm suất; người mới thì phải còn suất.
        // TODO: hai request gắn 2 người MỚI khác nhau cùng lúc có thể cùng qua bước đếm này và vượt suất
        // (chờ chốt concurrency token cho HandoverPlan.ConfigVersion).
        bool alreadyDesignated = await containerRepository.IsBeneficiaryDesignatedAsync(vault.Id, beneficiaryId, ct);
        if (!alreadyDesignated)
        {
            int usedSlots = await containerRepository.CountUsedBeneficiarySlotsAsync(vault.Id, ct);
            int maxSlots = vault.Entitlement.IncludedBeneficiarySlots + vault.Entitlement.ExtraBeneficiarySlots;
            if (usedSlots + 1 > maxSlots)
            {
                throw SlotsExceeded(usedSlots, maxSlots);
            }
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Designation designation = new Designation();
        designation.ContainerId = container.Id;
        designation.BeneficiaryId = beneficiaryId;
        designation.CreatedAt = now;
        // Thêm vào danh sách trước để RecalculateStatus và beneficiaryIds trong response tính cả người mới.
        // SETUP-08 / SETUP-19: mọi hồ sơ đã lưu đều hợp lệ, kể cả người chưa đủ 18 tuổi.
        container.Designations.Add(designation);
        container.UpdatedAt = now;
        ContainerRules.RecalculateStatus(container);
        BumpConfigVersion(vault, now);

        // Lưu chỉ định cùng các thay đổi đang chờ của Container và Plan trong một lần SaveChanges.
        // Concurrency token Container.UpdatedAt lệch → GlobalExceptionHandler trả 409 EDIT_CONFLICT; không bắt ở đây.
        bool added = await containerRepository.TryAddDesignationAsync(designation, ct);
        if (added)
        {
            return container.ToResponse();
        }

        // Unique (ContainerId, BeneficiaryId) chặn: request khác (bấm lặp) vừa gắn đúng người này vào đúng gói này.
        // Container/Plan đang theo dõi đã bị sửa trong bộ nhớ nhưng KHÔNG được lưu, nên bỏ hết rồi đọc lại từ DB.
        containerRepository.DiscardPendingChanges();
        Container reloaded = await LoadContainerAsync(vault.Id, containerId, ct);
        return reloaded.ToResponse();
    }

    public async Task<ContainerResponse> RemoveBeneficiaryAsync(
        Guid vaultId, Guid containerId, Guid beneficiaryId, ConfirmPlanImpactRequest? request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // OPLAN-01: gỡ người nhận là thao tác "giảm", vẫn cho khi dịch vụ đang ân hạn.
        VaultRules.EnsureCanReduce(vault);
        Container container = await LoadContainerAsync(vault.Id, containerId, ct);

        if (container.Status == ContainerStatus.Archived)
        {
            throw Archived();
        }

        Designation? designation = FindDesignationInContainer(container, beneficiaryId);
        if (designation == null)
        {
            // Người nhận không ở trong gói: trả nguyên trạng, không ghi DB.
            return container.ToResponse();
        }

        bool confirmed = false;
        if (request != null)
        {
            confirmed = request.ConfirmPlanDeactivation;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        // SETUP-16: gỡ người nhận cuối cùng của gói READY làm gói về DRAFT, có thể làm kế hoạch mất gói READY cuối cùng.
        bool willLoseReady = container.Status == ContainerStatus.Ready && container.Designations.Count == 1;
        await EnsurePlanImpactConfirmedAsync(vault, willLoseReady, confirmed, now, ct);

        // Không cần "trả suất": suất luôn đếm lại từ chỉ định; người còn ở gói khác vẫn chiếm suất (AC-103).
        // Gỡ cả khỏi danh sách để RecalculateStatus và beneficiaryIds trong response đúng ngay.
        container.Designations.Remove(designation);
        containerRepository.RemoveDesignation(designation);
        container.UpdatedAt = now;
        ContainerRules.RecalculateStatus(container);
        BumpConfigVersion(vault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return container.ToResponse();
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
        PlanRules.Deactivate(vault, now);
        // SETUP-16: kế hoạch về DRAFT thì lịch điểm danh dừng; người gọi lưu cùng giao dịch.
        await checkInScheduler.StopAsync(vault.Plan, ct);
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

    /// <summary>Liên kết của tài sản trong gói đã nạp Assets; không có → null.</summary>
    private static ContainerAsset? FindLinkInContainer(Container container, Guid assetId)
    {
        foreach (ContainerAsset link in container.Assets)
        {
            if (link.AssetId == assetId)
            {
                return link;
            }
        }
        return null;
    }

    /// <summary>Chỉ định của hồ sơ trong gói đã nạp Designations; không có → null.</summary>
    private static Designation? FindDesignationInContainer(Container container, Guid beneficiaryId)
    {
        foreach (Designation designation in container.Designations)
        {
            if (designation.BeneficiaryId == beneficiaryId)
            {
                return designation;
            }
        }
        return null;
    }

    private static AppException Archived()
    {
        return new AppException(AppErrorType.Conflict, ErrorCode.ContainerArchived,
            "Gói đã lưu trữ, không sửa được.");
    }

    /// <summary>SETUP-03: tài sản đang ở gói khác; containerId để FE chỉ ra gói đó.</summary>
    private static AppException AlreadyInContainer(Guid containerId)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["containerId"] = containerId;
        return new AppException(AppErrorType.Conflict, ErrorCode.AssetAlreadyInContainer,
            "Tài sản này đang ở một gói khác.", extensions);
    }

    /// <summary>QUOTA-05: vượt suất thì từ chối, kèm số đang dùng và tối đa.</summary>
    private static AppException SlotsExceeded(int usedSlots, int maxSlots)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["usedSlots"] = usedSlots;
        extensions["maxSlots"] = maxSlots;
        return new AppException(AppErrorType.Conflict, ErrorCode.BeneficiarySlotsExceeded,
            "Kho đã dùng hết suất người nhận. Vui lòng mua thêm suất.", extensions);
    }

    private static AppException LimitExceeded()
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["maxContainers"] = ContainerRules.MaxActiveContainers;
        return new AppException(AppErrorType.Conflict, ErrorCode.ContainerLimitExceeded,
            "Kho đã có đủ 50 gói chưa lưu trữ.", extensions);
    }
}

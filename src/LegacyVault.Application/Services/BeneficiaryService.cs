using LegacyVault.Application.Notifications;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
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
/// không ghi CCCD, họ tên, ngày sinh, email, số điện thoại vào log hay message lỗi.
/// không gửi thông báo, không tạo tài khoản cho người nhận.
/// </remarks>
public sealed partial class BeneficiaryService : IBeneficiaryService
{
    private const string DefaultSortBy = "createdAt";
    private const int MaxSearchDigits = 4;

    /// <summary>Ngày sinh và tuổi tính theo giờ Việt Nam (UTC+07:00).</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Tuổi tối thiểu của người nhận (đủ năng lực hành vi dân sự đầy đủ).</summary>
    private const int MinimumRecipientAge = 18;

    private readonly ICurrentUser currentUser;
    private readonly IVaultRepository vaultRepository;
    private readonly IBeneficiaryRepository beneficiaryRepository;
    private readonly IAssetAssignmentRepository assignmentRepository;
    private readonly IBeneficiaryDataProtector dataProtector;
    private readonly IAuditLogRepository auditLogRepository;
    private readonly IUserRepository userRepository;
    private readonly IOwnerIdentityReader ownerIdentityReader;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly IEmailDomainChecker domainChecker;
    private readonly IEmailBounceStore bounces;
    private readonly IExecutorInvitationRepository invitationRepository;

    public BeneficiaryService(
        ICurrentUser currentUser,
        IVaultRepository vaultRepository,
        IBeneficiaryRepository beneficiaryRepository,
        IAssetAssignmentRepository assignmentRepository,
        IBeneficiaryDataProtector dataProtector,
        IAuditLogRepository auditLogRepository,
        IUserRepository userRepository,
        IOwnerIdentityReader ownerIdentityReader,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IEmailDomainChecker domainChecker,
        IEmailBounceStore bounces,
        IExecutorInvitationRepository invitationRepository)
    {
        this.invitationRepository = invitationRepository;
        this.domainChecker = domainChecker;
        this.bounces = bounces;
        this.currentUser = currentUser;
        this.vaultRepository = vaultRepository;
        this.beneficiaryRepository = beneficiaryRepository;
        this.assignmentRepository = assignmentRepository;
        this.dataProtector = dataProtector;
        this.auditLogRepository = auditLogRepository;
        this.userRepository = userRepository;
        this.ownerIdentityReader = ownerIdentityReader;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<CreateBeneficiaryResult> CreateBeneficiaryAsync(
        Guid vaultId, CreateBeneficiaryRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // tạo hồ sơ người nhận là thao tác "thêm".
        VaultRules.EnsureEditable(vault);
        // hạng Free không được thêm người nhận; chặn ở API, không chỉ ẩn nút.
        if (vault.Entitlement.Tier == ServiceTier.Free)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.PaidTierRequired,
                "Hạng miễn phí chưa được thêm người nhận. Vui lòng nâng cấp dịch vụ.");
        }

        // đủ số CCCD, họ tên, ngày sinh.
        BeneficiaryPersonalData data = new BeneficiaryPersonalData();
        data.CitizenId = CitizenIdRules.NormalizeCitizenId(request.CitizenId, "citizenId");
        data.FullName = NormalizeFullName(request.FullName);
        if (request.DateOfBirth == null)
        {
            throw ValidationErrors.For("dateOfBirth", "Ngày sinh là bắt buộc.");
        }
        data.DateOfBirth = request.DateOfBirth.Value;
        data.Email = request.Email;
        data.Phone = request.Phone;
        data.Message = NormalizeMessage(request.Message);
        NormalizeContact(data);
        string? ownerCitizenId = await ownerIdentityReader.FindBoundCitizenIdAsync(vault.OwnerId, ct);
        if (ownerCitizenId != null && string.Equals(data.CitizenId, ownerCitizenId, StringComparison.Ordinal))
        {
            throw ValidationErrors.For("citizenId", "Số CCCD người thụ hưởng không được trùng với CCCD của Chủ kho.");
        }
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            User? owner = await userRepository.FindByIdAsync(vault.OwnerId, ct);
            if (owner != null && !string.IsNullOrWhiteSpace(owner.Email) &&
                string.Equals(data.Email.Trim(), owner.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw ValidationErrors.For("email", "Email người thụ hưởng không được trùng với email của Chủ kho.");
            }
            await EnsureEmailIsNotStaffAsync(data.Email, ct);
            await EmailDomainGuard.EnsureAsync(domainChecker, data.Email.Trim().ToLowerInvariant(), "email", ct);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateOnly today = GetTodayInVietnam();
        Beneficiary beneficiary = new Beneficiary();
        beneficiary.VaultId = vault.Id;
        EnsureDateOfBirthNotInFuture(data.DateOfBirth, today);

        // hồ sơ chung trong kho, một số CCCD chỉ có một hồ sơ.
        MatchedBeneficiary? existing = await FindSameCitizenIdAsync(vault.Id, data.CitizenId, ct);
        if (existing != null)
        {
            return await ResultForExistingAsync(existing, data, ct);
        }

        // Sau bước tìm trùng: gửi lại đúng hồ sơ cũ (chưa đủ tuổi từ trước) vẫn trả 200, chỉ hồ sơ mới mới bị kiểm tuổi.
        EnsureAdult(data.DateOfBirth, today);

        // Hồ sơ mới thật sự được tạo: email không được trùng Chủ kho, nhân sự, Người thực thi hay người nhận khác.
        // Kiểm sau bước tìm trùng CCCD để gửi lại đúng người đã có (cùng email) vẫn trả hồ sơ cũ.
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            await EnsureEmailAvailableAsync(data.Email, vault, null, ct, data.CitizenId);
        }

        // Giới hạn số hồ sơ người nhận theo tổng suất của kho (suất kèm hạng + suất mua thêm), tính cả hồ sơ chưa ở gói nào.
        // Kiểm sau bước tìm trùng CCCD: gửi lại đúng người đã có vẫn trả hồ sơ cũ dù kho đã đủ suất.
        int profileCount = await beneficiaryRepository.CountByVaultIdAsync(vault.Id, ct);
        int maxSlots = EntitlementLimits.EffectiveTotalSlots(vault.Entitlement);
        if (profileCount + 1 > maxSlots)
        {
            throw SlotsExceeded(profileCount, maxSlots);
        }

        dataProtector.Protect(beneficiary, data);
        // Hồ sơ mới chưa sửa lần nào: UpdatedAt = null → etag "0".
        beneficiary.CreatedAt = now;
        beneficiaryRepository.Add(beneficiary);
        // chưa gắn vào gói thì chưa thuộc cấu hình bàn giao, chưa chiếm suất → KHÔNG tăng ConfigVersion.
        // chỉ ghi id hồ sơ; họ tên, số CCCD, liên hệ không vào nhật ký.
        AddAudit(vault, AuditAction.BeneficiaryCreated, beneficiary.Id, null, now);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (beneficiaryRepository.IsUniqueViolation(ex))
        {
            // Hai request cùng CCCD đến cùng lúc: chỉ mục unique (VaultId, CitizenIdHash) cho một bên thắng.
            // Bên thua bỏ hồ sơ của mình rồi xử lý như gặp hồ sơ có sẵn.
            beneficiaryRepository.DiscardPendingChanges();
            MatchedBeneficiary? winner = await FindSameCitizenIdAsync(vault.Id, data.CitizenId, ct);
            if (winner == null)
            {
                throw;
            }
            return await ResultForExistingAsync(winner, data, ct);
        }

        CreateBeneficiaryResult result = new CreateBeneficiaryResult();
        result.Created = true;
        // Hồ sơ mới chưa ở gói nào.
        result.Beneficiary = beneficiary.ToResponse(data, new List<Guid>());
        return result;
    }

    /// <summary>
    /// Đã có hồ sơ cùng CCCD: cùng họ tên và ngày sinh thì trả hồ sơ cũ nguyên trạng (không ghi DB, liên hệ mới không được
    /// cập nhật); khác thì 409 BENEFICIARY_IDENTITY_CONFLICT để Owner sửa cho thống nhất.
    /// </summary>
    private async Task<CreateBeneficiaryResult> ResultForExistingAsync(
        MatchedBeneficiary existing, BeneficiaryPersonalData data, CancellationToken ct)
    {
        if (!IsSameIdentity(existing.Data, data))
        {
            throw IdentityConflict(existing.Beneficiary.Id);
        }
        List<Guid> existingAssetIds = await LoadAssetIdsAsync(existing.Beneficiary.Id, ct);
        CreateBeneficiaryResult same = new CreateBeneficiaryResult();
        same.Created = false;
        same.Beneficiary = existing.Beneficiary.ToResponse(existing.Data, existingAssetIds);
        same.Beneficiary.IsExisting = true;
        return same;
    }

    public async Task<PaginatedList<BeneficiaryResponse>> GetBeneficiariesAsync(
        Guid vaultId, PaginationRequest pagination, CancellationToken ct)
    {
        // Xem không cần kho đang cho sửa, chỉ cần kho thuộc người dùng.
        await LoadOwnedVaultAsync(vaultId, ct);

        if (!string.IsNullOrWhiteSpace(pagination.SortBy)
            && !string.Equals(pagination.SortBy.Trim(), DefaultSortBy, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationErrors.For("sortBy", "Chỉ sắp xếp theo createdAt.");
        }

        // Họ tên đã mã hóa nên chỉ tìm được theo 4 số cuối CCCD.
        string? searchTerm = TextInput.NullIfBlank(pagination.SearchTerm);
        if (searchTerm != null && !CitizenIdRules.IsDigits(searchTerm, 1, MaxSearchDigits))
        {
            throw ValidationErrors.For("searchTerm", "Chỉ tìm theo 1 đến 4 số cuối CCCD.");
        }

        (List<Beneficiary> Items, int TotalCount) page = await beneficiaryRepository.FindPageByVaultIdAsync(
            vaultId, searchTerm, pagination.PageIndex, pagination.PageSize, pagination.IsAscending, ct);

        // Lấy assetIds của cả trang bằng một truy vấn (tránh N+1).
        List<Guid> beneficiaryIds = new List<Guid>();
        foreach (Beneficiary beneficiary in page.Items)
        {
            beneficiaryIds.Add(beneficiary.Id);
        }
        Dictionary<Guid, List<Guid>> assetIdsByBeneficiary =
            await assignmentRepository.FindAssetIdsByBeneficiaryIdsAsync(beneficiaryIds, ct);

        List<BeneficiaryResponse> responses = new List<BeneficiaryResponse>();
        foreach (Beneficiary beneficiary in page.Items)
        {
            BeneficiaryPersonalData data = dataProtector.Unprotect(beneficiary);
            List<Guid> assetIds = GetAssetIds(assetIdsByBeneficiary, beneficiary.Id);
            responses.Add(beneficiary.ToResponse(data, assetIds));
        }
        HashSet<string> bouncedEmails = await bounces.FindBouncedAsync(responses.Select(r => r.Email ?? string.Empty), ct);
        foreach (BeneficiaryResponse response in responses)
        {
            response.EmailUndeliverable = !string.IsNullOrWhiteSpace(response.Email)
                && bouncedEmails.Contains(response.Email.Trim().ToLowerInvariant());
        }
        return responses.ToPaginatedList(page.TotalCount, pagination);
    }

    /// <summary>
    /// Tài khoản nhân sự LegacyVault (Executor, Verifier, Admin) không được làm người nhận: họ xử lý hồ sơ bàn giao
    /// nên không thể đồng thời là bên nhận tài sản.
    /// </summary>
    private async Task EnsureEmailIsNotStaffAsync(string email, CancellationToken ct)
    {
        string normalized = email.Trim().ToLowerInvariant();
        User? account = await userRepository.FindByEmailAsync(normalized, ct);
        if (account == null)
        {
            return;
        }
        if (account.Role == UserRole.Executor || account.Role == UserRole.Verifier || account.Role == UserRole.Admin)
        {
            throw ValidationErrors.For("email",
                "Email này thuộc tài khoản nhân sự của LegacyVault nên không thể làm người nhận.");
        }
    }

    /// <summary>
    /// Email không trùng Chủ kho, Người thực thi của kho hay người nhận khác (xem RecipientRules). Hồ sơ đang sửa không tính.
    /// </summary>
    private async Task EnsureEmailAvailableAsync(
        string email, Vault vault, Guid? exceptBeneficiaryId, CancellationToken ct, string? sameCitizenId = null)
    {
        (List<Beneficiary> Items, int TotalCount) page = await beneficiaryRepository.FindPageByVaultIdAsync(
            vault.Id, null, 1, 1000, true, ct);
        await RecipientRules.EnsureEmailAvailableAsync(
            email, vault, exceptBeneficiaryId, userRepository, invitationRepository, dataProtector, page.Items, ct, sameCitizenId);
    }

    public async Task<BeneficiaryResponse> GetBeneficiaryAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        Beneficiary beneficiary = await LoadBeneficiaryAsync(vault.Id, beneficiaryId, ct);
        BeneficiaryPersonalData data = dataProtector.Unprotect(beneficiary);
        List<Guid> assetIds = await LoadAssetIdsAsync(beneficiary.Id, ct);
        return beneficiary.ToResponse(data, assetIds);
    }

    public async Task<BeneficiaryResponse> UpdateBeneficiaryAsync(
        Guid vaultId, Guid beneficiaryId, UpdateBeneficiaryRequest request, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // sửa hồ sơ người nhận là thao tác "thêm".
        VaultRules.EnsureEditable(vault);
        Beneficiary beneficiary = await LoadBeneficiaryAsync(vault.Id, beneficiaryId, ct);

        if (!Etag.Matches(request.Etag, beneficiary.UpdatedAt))
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Hồ sơ người nhận vừa được sửa ở nơi khác. Vui lòng tải lại rồi sửa tiếp.");
        }

        BeneficiaryPersonalData current = dataProtector.Unprotect(beneficiary);

        // Không cho đổi CCCD. Họ tên/ngày sinh null = giữ nguyên; email/phone null = giữ nguyên, "" = xóa.
        BeneficiaryPersonalData updated = new BeneficiaryPersonalData();
        updated.CitizenId = current.CitizenId;
        updated.FullName = current.FullName;
        updated.DateOfBirth = current.DateOfBirth;
        updated.Email = current.Email;
        updated.Phone = current.Phone;
        updated.Message = current.Message;
        if (request.Message != null)
        {
            updated.Message = NormalizeMessage(request.Message);
        }
        if (request.FullName != null)
        {
            updated.FullName = NormalizeFullName(request.FullName);
        }
        if (request.DateOfBirth != null)
        {
            updated.DateOfBirth = request.DateOfBirth.Value;
        }
        if (request.Email != null)
        {
            updated.Email = request.Email;
        }
        if (request.Phone != null)
        {
            updated.Phone = request.Phone;
        }
        NormalizeContact(updated);
        if (!string.IsNullOrWhiteSpace(updated.Email))
        {
            User? owner = await userRepository.FindByIdAsync(vault.OwnerId, ct);
            if (owner != null && !string.IsNullOrWhiteSpace(owner.Email) &&
                string.Equals(updated.Email.Trim(), owner.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw ValidationErrors.For("email", "Email người thụ hưởng không được trùng với email của Chủ kho.");
            }
            await EnsureEmailIsNotStaffAsync(updated.Email, ct);
            // Chỉ kiểm khi đổi email, để sửa tên hay số điện thoại không bị chặn bởi email cũ.
            if (!string.Equals(updated.Email, current.Email, StringComparison.OrdinalIgnoreCase))
            {
                await EnsureEmailAvailableAsync(updated.Email, vault, beneficiaryId, ct);
            }
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateOnly today = GetTodayInVietnam();
        EnsureDateOfBirthNotInFuture(updated.DateOfBirth, today);
        // Chỉ kiểm tuổi khi ngày sinh được đổi, để hồ sơ cũ chưa đủ tuổi vẫn sửa được các trường khác.
        if (updated.DateOfBirth != current.DateOfBirth)
        {
            EnsureAdult(updated.DateOfBirth, today);
        }

        bool changed = !string.Equals(updated.FullName, current.FullName, StringComparison.Ordinal)
            || updated.DateOfBirth != current.DateOfBirth
            || !string.Equals(updated.Email, current.Email, StringComparison.Ordinal)
            || !string.Equals(updated.Phone, current.Phone, StringComparison.Ordinal)
            || !string.Equals(updated.Message, current.Message, StringComparison.Ordinal);

        List<Guid> assetIds = await LoadAssetIdsAsync(beneficiary.Id, ct);

        // Không có gì đổi: không mã hóa lại, không lưu, etag giữ nguyên.
        if (!changed)
        {
            return beneficiary.ToResponse(current, assetIds);
        }

        // Mỗi lần lưu dùng DEK mới.
        dataProtector.Protect(beneficiary, updated);
        beneficiary.UpdatedAt = now;
        // hồ sơ đang được gán tài sản thì sửa người nhận là đổi cấu hình bàn giao → tăng ConfigVersion
        // (lưu cùng một lần SaveChanges với hồ sơ).
        int? configVersion = null;
        if (assetIds.Count > 0)
        {
            PlanRules.BumpConfigVersion(vault.Plan, now);
            configVersion = vault.Plan.ConfigVersion;
        }
        AddAudit(vault, AuditAction.BeneficiaryUpdated, beneficiary.Id, configVersion, now);
        await unitOfWork.SaveChangesAsync(ct);
        return beneficiary.ToResponse(updated, assetIds);
    }

    public async Task DeleteBeneficiaryAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        Vault vault = await LoadOwnedVaultAsync(vaultId, ct);
        // xóa hồ sơ là thao tác "giảm", vẫn cho khi dịch vụ đang ân hạn.
        VaultRules.EnsureCanReduce(vault);
        Beneficiary beneficiary = await LoadBeneficiaryAsync(vault.Id, beneficiaryId, ct);

        // Hồ sơ đang được gán tài sản thì phải gỡ gán trước; assetIds để FE chỉ ra các tài sản đó.
        List<Guid> assetIds = await LoadAssetIdsAsync(beneficiary.Id, ct);
        if (assetIds.Count > 0)
        {
            throw InUse(assetIds);
        }

        // TODO: request khác gán tài sản cho hồ sơ này đúng lúc này thì FK Restrict ở DB chặn việc xóa,
        // nhưng lỗi đó hiện ra 500 thay vì 409 BENEFICIARY_IN_USE.
        beneficiaryRepository.Remove(beneficiary);
        // AuditLogs.TargetId không có khóa ngoại nên dòng nhật ký vẫn giữ sau khi hồ sơ bị xóa.
        AddAudit(vault, AuditAction.BeneficiaryDeleted, beneficiary.Id, null, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// một dòng nhật ký cho thao tác trên hồ sơ người nhận, lưu cùng lần SaveChanges.
    /// configVersion chỉ có khi thao tác đổi cấu hình bàn giao (hồ sơ đang ở gói).
    /// </summary>
    private void AddAudit(Vault vault, AuditAction action, Guid beneficiaryId, int? configVersion, DateTimeOffset now)
    {
        auditLogRepository.Add(AuditRules.OwnerAction(
            vault.Id, vault.OwnerId, action, AuditTargetType.Beneficiary, beneficiaryId, configVersion, now));
    }

    /// <summary>chuẩn hóa Unicode NFC, bỏ khoảng trắng đầu/cuối, gộp nhiều khoảng trắng thành một.</summary>
    private static string NormalizeFullName(string? fullName)
    {
        if (fullName == null)
        {
            throw ValidationErrors.For("fullName", "Họ tên không được để trống.");
        }

        string composed = fullName.Normalize(NormalizationForm.FormC).Trim();
        StringBuilder builder = new StringBuilder();
        bool previousIsSpace = false;
        foreach (char c in composed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousIsSpace)
                {
                    builder.Append(' ');
                }
                previousIsSpace = true;
            }
            else
            {
                builder.Append(c);
                previousIsSpace = false;
            }
        }

        string normalized = builder.ToString();
        if (normalized.Length < 2)
        {
            throw ValidationErrors.For("fullName", "Họ và tên phải có ít nhất 2 ký tự.");
        }
        if (normalized.Length > 200)
        {
            throw ValidationErrors.For("fullName", "Họ tên tối đa 200 ký tự.");
        }
        foreach (char c in normalized)
        {
            if (!char.IsLetter(c) && !char.IsWhiteSpace(c))
            {
                throw ValidationErrors.For("fullName", "Họ và tên chỉ được chứa chữ cái và khoảng trắng.");
            }
        }
        return normalized;
    }

    /// <summary>
    /// Chuẩn hóa email/số điện thoại ngay trên data. Email là bắt buộc. "" hoặc chỉ khoảng trắng = không có.
    /// Email: trim, chữ thường, phải đúng định dạng email. Phone: nếu có thì bỏ khoảng trắng, dấu chấm, dấu gạch rồi khớp ^\+?[0-9]{9,15}$.
    /// </summary>
    private static void NormalizeContact(BeneficiaryPersonalData data)
    {
        string? email = TextInput.NullIfBlank(data.Email);
        if (email == null)
        {
            throw ValidationErrors.For("email", "Email là bắt buộc.");
        }

        email = email.ToLowerInvariant();
        EmailAddressAttribute emailAttribute = new EmailAddressAttribute();
        if (!emailAttribute.IsValid(email) || !email.Contains('@') || !email.Contains('.'))
        {
            throw ValidationErrors.For("email", "Email không hợp lệ.");
        }

        string? phone = TextInput.NullIfBlank(data.Phone);
        if (phone != null)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in phone)
            {
                if (!char.IsWhiteSpace(c) && c != '.' && c != '-')
                {
                    builder.Append(c);
                }
            }
            phone = builder.ToString();
            if (!PhonePattern().IsMatch(phone))
            {
                throw ValidationErrors.For("phone", "Số điện thoại không hợp lệ (9 đến 15 chữ số).");
            }
        }

        data.Email = email;
        data.Phone = phone;
    }

    /// <summary>Lời nhắn: bỏ khoảng trắng đầu cuối, gộp xuống dòng thành khoảng trắng; rỗng thì null; quá 300 ký tự thì báo lỗi.</summary>
    private static string? NormalizeMessage(string? message)
    {
        string? trimmed = TextInput.NullIfBlank(message);
        if (trimmed == null)
        {
            return null;
        }
        string single = trimmed.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (single.Length > BeneficiaryPersonalData.MessageMaxLength)
        {
            throw ValidationErrors.For("message", "Lời nhắn tối đa 300 ký tự.");
        }
        return single;
    }

    /// <summary>Ngày hiện tại theo giờ Việt Nam (UTC+07:00).</summary>
    private DateOnly GetTodayInVietnam()
    {
        DateTimeOffset nowInVietnam = timeProvider.GetUtcNow().ToOffset(VietnamOffset);
        return DateOnly.FromDateTime(nowInVietnam.DateTime);
    }

    /// <summary>Ngày sinh không được ở tương lai (400).</summary>
    private static void EnsureDateOfBirthNotInFuture(DateOnly dateOfBirth, DateOnly today)
    {
        if (dateOfBirth > today)
        {
            throw ValidationErrors.For("dateOfBirth", "Ngày sinh không được ở tương lai.");
        }
    }

    /// <summary>
    /// Người nhận phải đủ 18 tuổi tính đến hôm nay (giờ Việt Nam). Đủ tuổi khi ngày sinh không muộn hơn
    /// ngày này của 18 năm trước (AddYears xử lý đúng 29/2). Thiếu tuổi thì 400 ở trường dateOfBirth.
    /// </summary>
    private static void EnsureAdult(DateOnly dateOfBirth, DateOnly today)
    {
        DateOnly latestAllowed = today.AddYears(-MinimumRecipientAge);
        if (dateOfBirth > latestAllowed)
        {
            throw ValidationErrors.For("dateOfBirth", "Người nhận phải đủ 18 tuổi.");
        }
    }

    /// <summary>
    /// tìm hồ sơ cùng số CCCD trong kho. Lọc theo 4 số cuối rồi giải mã từng hồ sơ và so số đầy đủ.
    /// Không có → null.
    /// </summary>
    private async Task<MatchedBeneficiary?> FindSameCitizenIdAsync(Guid vaultId, string citizenId, CancellationToken ct)
    {
        string last4 = citizenId.Substring(citizenId.Length - 4);
        List<Beneficiary> candidates = await beneficiaryRepository.ListByCitizenIdLast4Async(vaultId, last4, ct);
        foreach (Beneficiary candidate in candidates)
        {
            BeneficiaryPersonalData data = dataProtector.Unprotect(candidate);
            if (string.Equals(data.CitizenId, citizenId, StringComparison.Ordinal))
            {
                MatchedBeneficiary matched = new MatchedBeneficiary();
                matched.Beneficiary = candidate;
                matched.Data = data;
                return matched;
            }
        }
        return null;
    }

    /// <summary>
    /// cùng người khi họ tên (đã chuẩn hóa) giống nhau không phân biệt hoa thường và cùng ngày sinh.
    /// Không bỏ dấu, không so liên hệ.
    /// </summary>
    private static bool IsSameIdentity(BeneficiaryPersonalData left, BeneficiaryPersonalData right)
    {
        return string.Equals(left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase)
            && left.DateOfBirth == right.DateOfBirth;
    }

    private async Task<Vault> LoadOwnedVaultAsync(Guid vaultId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        Vault? vault = await vaultRepository.FindByIdAsync(vaultId, ct);
        return VaultRules.EnsureOwnedBy(vault, userId);
    }

    /// <summary>Hồ sơ không có hoặc thuộc kho khác → 404 BENEFICIARY_NOT_FOUND.</summary>
    private async Task<Beneficiary> LoadBeneficiaryAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        Beneficiary? beneficiary = await beneficiaryRepository.FindByIdAsync(vaultId, beneficiaryId, ct);
        if (beneficiary == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.BeneficiaryNotFound, "Không tìm thấy người nhận.");
        }
        return beneficiary;
    }

    /// <summary>Các tài sản đang được gán cho một hồ sơ (sắp theo thời điểm gán); không có → danh sách rỗng.</summary>
    private async Task<List<Guid>> LoadAssetIdsAsync(Guid beneficiaryId, CancellationToken ct)
    {
        List<Guid> beneficiaryIds = new List<Guid>();
        beneficiaryIds.Add(beneficiaryId);
        Dictionary<Guid, List<Guid>> assetIdsByBeneficiary =
            await assignmentRepository.FindAssetIdsByBeneficiaryIdsAsync(beneficiaryIds, ct);
        return GetAssetIds(assetIdsByBeneficiary, beneficiaryId);
    }

    /// <summary>Hồ sơ chưa được gán tài sản nào thì không có key trong dictionary → danh sách rỗng.</summary>
    private static List<Guid> GetAssetIds(Dictionary<Guid, List<Guid>> assetIdsByBeneficiary, Guid beneficiaryId)
    {
        List<Guid>? assetIds;
        if (assetIdsByBeneficiary.TryGetValue(beneficiaryId, out assetIds))
        {
            return assetIds;
        }
        return new List<Guid>();
    }

    private static AppException InUse(List<Guid> assetIds)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["assetIds"] = assetIds;
        return new AppException(AppErrorType.Conflict, ErrorCode.BeneficiaryInUse,
            "Người nhận đang được gán tài sản. Vui lòng gỡ gán trước khi xóa.", extensions);
    }

    /// <summary>Đã đủ số hồ sơ theo suất của kho; kèm số hồ sơ hiện có và số suất tối đa để FE gợi ý mua thêm.</summary>
    private static AppException SlotsExceeded(int usedSlots, int maxSlots)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["usedSlots"] = usedSlots;
        extensions["maxSlots"] = maxSlots;
        return new AppException(AppErrorType.Conflict, ErrorCode.BeneficiarySlotsExceeded,
            "Kho đã đủ số người nhận theo suất hiện có. Vui lòng mua thêm suất hoặc xóa hồ sơ chưa dùng.", extensions);
    }

    /// <summary>beneficiaryId để FE mở hồ sơ đang có cùng CCCD cho Owner sửa.</summary>
    private static AppException IdentityConflict(Guid beneficiaryId)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["beneficiaryId"] = beneficiaryId;
        return new AppException(AppErrorType.Conflict, ErrorCode.BeneficiaryIdentityConflict,
            "Số CCCD này đã tồn tại trong danh sách người nhận của kho (với thông tin khác). Vui lòng kiểm tra lại.", extensions);
    }

    [GeneratedRegex("^\\+?[0-9]{9,15}$")]
    private static partial Regex PhonePattern();

    /// <summary>Hồ sơ cùng CCCD tìm được và dữ liệu đã giải mã của nó.</summary>
    private sealed class MatchedBeneficiary
    {
        public Beneficiary Beneficiary { get; set; } = null!;

        public BeneficiaryPersonalData Data { get; set; } = null!;
    }
}

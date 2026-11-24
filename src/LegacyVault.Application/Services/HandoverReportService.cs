using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Application.Services;

// Luồng hồ sơ báo tin:
//   1. Người thân nộp hồ sơ ......... SubmitAsync (chưa khóa kho, không cho biết có kho hay không)
//   2. Luật sư xét giấy tờ .......... ReviewAsync (chấp nhận thì tạo biên bản bàn giao, khóa kho, báo Chủ kho)
//   3. Từ đây đi tiếp như biên bản thường: HandoverRecordService (thêm người nhận, lập, chốt, ký)
/// <summary>Hồ sơ báo tin Chủ kho đã mất. Hệ thống chỉ lưu giấy tờ; luật sư xác minh ở ngoài hệ thống.</summary>
public sealed partial class HandoverReportService : IHandoverReportService
{
    /// <summary>Số hồ sơ đang chờ xét tối đa của một tài khoản, để chống nộp hàng loạt.</summary>
    public const int MaxOpenReports = 3;

    public const int MaxRecipients = 20;

    /// <summary>Số hồ sơ tối đa một tài khoản nộp cho cùng một người mất trong ReportLimitDays ngày.</summary>
    public const int MaxReportsPerPerson = 3;

    public const int ReportLimitDays = 30;

    /// <summary>Tổng số tệp của một hồ sơ, gồm cả các lần bổ sung.</summary>
    public const int MaxFilesPerReport = 30;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IVaultRepository vaults;
    private readonly IOwnerIdentityRepository identities;
    private readonly IOwnerIdentityProtector identityProtector;
    private readonly IExecutorReader executorReader;
    private readonly IHandoverRecordRepository records;
    private readonly IHandoverReportRepository reports;
    private readonly IHandoverRepository handover;
    private readonly IRefreshTokenRepository refreshTokens;
    private readonly IBeneficiaryDataProtector beneficiaryProtector;
    private readonly IHandoverReportProtector protector;
    private readonly IAssetContentStore contentStore;
    private readonly IHandoverRequestNoticeSender noticeSender;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<HandoverReportService> logger;

    public HandoverReportService(
        ICurrentUser currentUser,
        IUserRepository users,
        IVaultRepository vaults,
        IOwnerIdentityRepository identities,
        IOwnerIdentityProtector identityProtector,
        IExecutorReader executorReader,
        IHandoverRecordRepository records,
        IHandoverReportRepository reports,
        IHandoverRepository handover,
        IRefreshTokenRepository refreshTokens,
        IBeneficiaryDataProtector beneficiaryProtector,
        IHandoverReportProtector protector,
        IAssetContentStore contentStore,
        IHandoverRequestNoticeSender noticeSender,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<HandoverReportService> logger)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.vaults = vaults;
        this.identities = identities;
        this.identityProtector = identityProtector;
        this.executorReader = executorReader;
        this.records = records;
        this.reports = reports;
        this.handover = handover;
        this.refreshTokens = refreshTokens;
        this.beneficiaryProtector = beneficiaryProtector;
        this.protector = protector;
        this.contentStore = contentStore;
        this.noticeSender = noticeSender;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    // ---------------- Người thân ----------------

    public async Task<MyHandoverReportResponse> SubmitAsync(
        SubmitHandoverReportRequest request, HandoverReportUploads uploads, CancellationToken ct)
    {
        // 1. Tài khoản người dùng đã xác minh email; thông tin khai và tệp phải hợp lệ
        User user = await RequireSubmitterAsync(ct);
        HandoverReportData data = ReadRequest(request, RecipientRules.TodayInVietnam(timeProvider));
        string note = NormalizeNote(request.Note);
        ValidateUploads(uploads);

        // 2. Chống nộp trùng và nộp hàng loạt (chỉ xét hồ sơ của chính người này)
        await EnsureNotDuplicateAsync(user.Id, data.DeceasedCitizenId, ct);

        // 3. Tìm kho theo số căn cước, rồi phân công luật sư. Không tìm thấy vẫn tiếp nhận như thường.
        Vault? vault = await FindVaultAsync(data.DeceasedCitizenId, ct);
        if (vault != null && vault.OwnerId == user.Id)
        {
            throw HandoverAccess.NotAllowed("OWNER_CANNOT_CLAIM", "Bạn không nộp hồ sơ báo tin cho chính mình.");
        }
        Guid? verifierId = await PickVerifierAsync(vault, user.Id, ct);
        if (verifierId == null)
        {
            throw HandoverAccess.NotAllowed("NO_VERIFIER",
                "Hiện chưa có luật sư nào nhận được hồ sơ này. Vui lòng thử lại sau.");
        }

        // 4. Ghi hồ sơ và tệp (mã hóa), lưu một lần; lưu lỗi thì xóa tệp đã ghi
        DateTimeOffset now = timeProvider.GetUtcNow();
        HandoverReport report = new HandoverReport
        {
            SubmitterUserId = user.Id,
            VerifierUserId = verifierId.Value,
            Status = HandoverClaimStatus.Submitted,
            Note = note,
            DataProtected = protector.Protect(data),
            CreatedAt = now,
            UpdatedAt = now
        };
        if (vault != null)
        {
            report.VaultId = vault.Id;
        }
        reports.Add(report);
        List<HandoverRecordFile> saved = new List<HandoverRecordFile>();
        try
        {
            await StoreFilesAsync(report, uploads.DeathCertificates, HandoverReportFileKind.DeathCertificate, user.Id, now, saved, ct);
            await StoreFilesAsync(report, uploads.Wills, HandoverReportFileKind.Will, user.Id, now, saved, ct);
            await StoreFilesAsync(report, uploads.Others, HandoverReportFileKind.Other, user.Id, now, saved, ct);
            await SaveAsync(ct);
        }
        catch (Exception)
        {
            await HandoverRecordFileStorage.DeleteAllAsync(contentStore, saved);
            throw;
        }

        // 5. Báo luật sư sau khi lưu
        await NotifyAsync(report.VerifierUserId, HandoverRequestNotice.Verifier, HandoverRequestNotice.StageReport,
            data.DeceasedFullName, user.FullName, ct);
        return await BuildMineAsync(report, ct);
    }

    public async Task<List<MyHandoverReportResponse>> ListMineAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        List<MyHandoverReportResponse> result = new List<MyHandoverReportResponse>();
        foreach (HandoverReport report in await reports.ListBySubmitterAsync(userId, ct))
        {
            result.Add(await BuildMineAsync(report, ct));
        }
        return result;
    }

    public async Task<MyHandoverReportResponse> SupplementAsync(
        Guid reportId, string? note, List<UploadedFile> files, CancellationToken ct)
    {
        // 1. Hồ sơ của tôi và luật sư đang yêu cầu bổ sung
        HandoverReport report = await RequireMineAsync(reportId, ct);
        if (report.Status != HandoverClaimStatus.SupplementRequested)
        {
            throw HandoverAccess.NotAllowed("NOT_SUPPLEMENT_REQUESTED", "Luật sư chưa yêu cầu bổ sung giấy tờ cho hồ sơ này.");
        }
        HandoverRecordFileStorage.Validate(files, true);
        if (report.Files.Count + files.Count > MaxFilesPerReport)
        {
            throw ValidationErrors.For("files", "Mỗi hồ sơ tối đa 30 tệp, gồm cả các lần bổ sung.");
        }
        string cleanNote = NormalizeNote(note);

        // 2. Thêm tệp, quay về chờ xét
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (cleanNote.Length > 0)
        {
            report.Note = cleanNote;
        }
        report.Status = HandoverClaimStatus.Submitted;
        report.UpdatedAt = now;
        List<HandoverRecordFile> saved = new List<HandoverRecordFile>();
        try
        {
            await StoreFilesAsync(report, files, HandoverReportFileKind.Other, report.SubmitterUserId, now, saved, ct);
            await SaveAsync(ct);
        }
        catch (Exception)
        {
            await HandoverRecordFileStorage.DeleteAllAsync(contentStore, saved);
            throw;
        }
        return await BuildMineAsync(report, ct);
    }

    public async Task<PreparedDownload> PrepareMyFileAsync(Guid reportId, Guid fileId, CancellationToken ct)
    {
        HandoverReport report = await RequireMineAsync(reportId, ct);
        return await PrepareFileAsync(report, fileId, ct);
    }

    // ---------------- Luật sư ----------------

    public async Task<List<VerifierHandoverReportResponse>> ListForVerifierAsync(CancellationToken ct)
    {
        Guid userId = await RequireActiveVerifierAsync(ct);
        List<VerifierHandoverReportResponse> result = new List<VerifierHandoverReportResponse>();
        foreach (HandoverReport report in await reports.ListByVerifierAsync(userId, ct))
        {
            result.Add(await BuildForVerifierAsync(report, ct));
        }
        return result;
    }

    public async Task<VerifierHandoverReportResponse> ReviewAsync(
        Guid reportId, ReviewHandoverReportRequest request, CancellationToken ct)
    {
        // 1. Hồ sơ được giao cho luật sư này và chưa có kết quả cuối
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverReport report = await RequireAssignedAsync(reportId, userId, ct);
        if (report.Status == HandoverClaimStatus.Accepted || report.Status == HandoverClaimStatus.Rejected)
        {
            throw HandoverAccess.NotAllowed("ALREADY_REVIEWED", "Hồ sơ này đã có kết quả xét.");
        }
        string decision = request.Decision.Trim().ToUpperInvariant();
        string reason = string.Empty;
        if (request.Reason != null)
        {
            reason = request.Reason.Trim();
        }
        DateTimeOffset now = timeProvider.GetUtcNow();

        // 2. Yêu cầu bổ sung hoặc từ chối: phải có lý do
        Vault? lockedVault = null;
        if (decision == "REQUEST_SUPPLEMENT" || decision == "REJECT")
        {
            if (reason.Length == 0)
            {
                throw ValidationErrors.For("reason", "Vui lòng ghi lý do để người nộp biết.");
            }
            report.ReviewReason = reason;
            if (decision == "REJECT")
            {
                report.Status = HandoverClaimStatus.Rejected;
            }
            else
            {
                report.Status = HandoverClaimStatus.SupplementRequested;
            }
        }
        // 3. Chấp nhận: gắn vào biên bản đang mở của kho, chưa có thì tạo mới và khóa kho
        else if (decision == "ACCEPT")
        {
            await EnsureWarningsConfirmedAsync(report, request.ConfirmDocuments, reason, ct);
            lockedVault = await AttachToRecordAsync(report, userId, now, ct);
            report.ReviewReason = null;
            report.AcceptNote = null;
            if (reason.Length > 0)
            {
                report.AcceptNote = reason;
            }
            report.Status = HandoverClaimStatus.Accepted;
        }
        else
        {
            throw ValidationErrors.For("decision", "Kết quả xét phải là ACCEPT, REQUEST_SUPPLEMENT hoặc REJECT.");
        }
        report.ReviewedAt = now;
        report.UpdatedAt = now;
        await SaveAsync(ct);

        // 4. Kho vừa khóa: báo Chủ kho (còn sống thì hủy được) và Người thực thi
        if (lockedVault != null)
        {
            await NotifyVaultLockedAsync(lockedVault, report, ct);
        }
        return await BuildForVerifierAsync(report, ct);
    }

    public async Task<PreparedDownload> PrepareVerifierFileAsync(Guid reportId, Guid fileId, CancellationToken ct)
    {
        Guid userId = await RequireActiveVerifierAsync(ct);
        HandoverReport report = await RequireAssignedAsync(reportId, userId, ct);
        return await PrepareFileAsync(report, fileId, ct);
    }

    // ---------------- Hàm phụ: nghiệp vụ ----------------

    /// <summary>Trả về kho vừa được khóa, hoặc null khi kho đã có biên bản đang mở.</summary>
    private async Task<Vault?> AttachToRecordAsync(HandoverReport report, Guid verifierId, DateTimeOffset now, CancellationToken ct)
    {
        Vault? vault = null;
        if (report.VaultId != null)
        {
            vault = await vaults.FindByIdAsync(report.VaultId.Value, ct);
        }
        if (vault == null)
        {
            throw HandoverAccess.NotAllowed("VAULT_NOT_FOUND",
                "Không có kho nào của người mất theo số căn cước này nên không chấp nhận được.");
        }
        HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
        if (record != null)
        {
            if (record.Status == HandoverRecordStatus.Completed)
            {
                throw HandoverAccess.NotAllowed("RECORD_COMPLETED", "Biên bản bàn giao của kho này đã đủ chữ ký.");
            }
            if (record.VerifierUserId != verifierId)
            {
                // Kho đã do luật sư khác phụ trách: chuyển hồ sơ cho đúng người, người đó xét tiếp.
                report.VerifierUserId = record.VerifierUserId;
                report.UpdatedAt = now;
                await SaveAsync(ct);
                throw HandoverAccess.NotAllowed("REPORT_TRANSFERRED",
                    "Kho này đang do luật sư khác lập biên bản. Hồ sơ đã được chuyển cho luật sư đó.");
            }
            report.RecordId = record.Id;
            return null;
        }
        if (vault.Plan.Status != PlanStatus.Active)
        {
            throw HandoverAccess.NotAllowed("PLAN_NOT_ACTIVE",
                "Kho này chưa kích hoạt kế hoạch bàn giao nên chưa mở biên bản được.");
        }
        record = new HandoverRecord
        {
            VaultId = vault.Id,
            PlanId = vault.Plan.Id,
            Status = HandoverRecordStatus.Drafting,
            RevisionNo = 1,
            RequestedByUserId = report.SubmitterUserId,
            VerifierUserId = verifierId,
            RequestedAt = now,
            UpdatedAt = now
        };
        records.Add(record);
        report.RecordId = record.Id;
        vault.Plan.Status = PlanStatus.HandoverRequested;
        vault.Plan.UpdatedAt = now;
        return vault;
    }

    /// <summary>Hồ sơ có cảnh báo thì luật sư phải xác nhận đã đối chiếu giấy tờ và ghi lý do mới chấp nhận được.</summary>
    private async Task EnsureWarningsConfirmedAsync(
        HandoverReport report, bool confirmDocuments, string reason, CancellationToken ct)
    {
        VerifierHandoverReportResponse view = await BuildForVerifierAsync(report, ct);
        bool hasWarning = view.VaultFound && (!view.NameMatches || view.ActiveAfterDeath);
        if (!hasWarning)
        {
            return;
        }
        if (!confirmDocuments)
        {
            throw ValidationErrors.For("confirmDocuments",
                "Hồ sơ có cảnh báo. Vui lòng xác nhận đã đối chiếu giấy chứng tử.");
        }
        if (reason.Length == 0)
        {
            throw ValidationErrors.For("reason", "Hồ sơ có cảnh báo. Vui lòng ghi lý do chấp nhận.");
        }
    }

    /// <summary>Lần hoạt động gần nhất: đăng nhập, hoặc gia hạn phiên đang còn hiệu lực.</summary>
    private async Task<DateTimeOffset?> FindLastActiveAsync(User owner, CancellationToken ct)
    {
        DateTimeOffset? last = owner.LastLoginAt;
        foreach (RefreshToken token in await refreshTokens.FindActiveByUserAsync(owner.Id, ct))
        {
            if (last == null || token.CreatedAt > last.Value)
            {
                last = token.CreatedAt;
            }
        }
        return last;
    }

    /// <summary>Kho có Chủ kho đã eKYC mang đúng số căn cước này; null khi không có.</summary>
    private async Task<Vault?> FindVaultAsync(string citizenId, CancellationToken ct)
    {
        List<OwnerIdentity> candidates = await identities.ListByCitizenIdLast4ExceptUserAsync(
            CitizenIdRules.Last4(citizenId), Guid.Empty, ct);
        foreach (OwnerIdentity candidate in candidates)
        {
            OwnerIdentityData? data = ReadIdentity(candidate);
            if (data != null && string.Equals(data.CitizenId, citizenId, StringComparison.Ordinal))
            {
                return await vaults.FindByOwnerIdAsync(candidate.UserId, ct);
            }
        }
        return null;
    }

    private OwnerIdentityData? ReadIdentity(OwnerIdentity identity)
    {
        try
        {
            return identityProtector.Unprotect(identity);
        }
        catch (CryptographicException)
        {
            // Danh tính không giải mã được: bỏ qua, không làm hỏng việc nộp hồ sơ của người khác.
            return null;
        }
    }

    /// <summary>Kho đã có biên bản thì giao luật sư của biên bản; không thì luật sư ít việc nhất, không dính tới kho.</summary>
    private async Task<Guid?> PickVerifierAsync(Vault? vault, Guid submitterId, CancellationToken ct)
    {
        List<Guid> excluded = new List<Guid>();
        excluded.Add(submitterId);
        List<string> excludedEmails = new List<string>();
        if (vault != null)
        {
            HandoverRecord? record = await records.FindByVaultIdAsync(vault.Id, ct);
            if (record != null)
            {
                return record.VerifierUserId;
            }
            excluded.Add(vault.OwnerId);
            foreach (Executor executor in await executorReader.GetActiveAsync(vault.Id, ct))
            {
                if (executor.UserId != null)
                {
                    excluded.Add(executor.UserId.Value);
                }
            }
            foreach (Beneficiary beneficiary in await records.ListVaultBeneficiariesAsync(vault.Id, ct))
            {
                string email = ReadBeneficiaryEmail(beneficiary);
                if (email.Length > 0)
                {
                    excludedEmails.Add(email);
                }
            }
        }
        return await records.FindVerifierCandidateAsync(excluded, excludedEmails, ct);
    }

    private string ReadBeneficiaryEmail(Beneficiary beneficiary)
    {
        try
        {
            string? email = beneficiaryProtector.Unprotect(beneficiary).Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                return string.Empty;
            }
            return email.Trim().ToLowerInvariant();
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
    }

    private async Task EnsureNotDuplicateAsync(Guid userId, string citizenId, CancellationToken ct)
    {
        int open = 0;
        int recentForPerson = 0;
        DateTimeOffset since = timeProvider.GetUtcNow().AddDays(-ReportLimitDays);
        foreach (HandoverReport existing in await reports.ListBySubmitterAsync(userId, ct))
        {
            bool samePerson = string.Equals(ReadData(existing).DeceasedCitizenId, citizenId, StringComparison.Ordinal);
            if (samePerson && existing.CreatedAt >= since)
            {
                recentForPerson = recentForPerson + 1;
            }
            bool waiting = existing.Status == HandoverClaimStatus.Submitted
                || existing.Status == HandoverClaimStatus.SupplementRequested;
            if (!waiting)
            {
                continue;
            }
            open = open + 1;
            if (samePerson)
            {
                throw HandoverAccess.NotAllowed("ALREADY_REPORTED",
                    "Bạn đã nộp hồ sơ cho người này và luật sư đang xét.");
            }
        }
        if (recentForPerson >= MaxReportsPerPerson)
        {
            throw HandoverAccess.NotAllowed("REPORT_LIMIT_REACHED",
                "Bạn đã nộp 3 hồ sơ cho người này trong 30 ngày. Vui lòng liên hệ luật sư hoặc thử lại sau.");
        }
        if (open >= MaxOpenReports)
        {
            throw HandoverAccess.NotAllowed("TOO_MANY_REPORTS",
                "Bạn đang có 3 hồ sơ chờ xét. Vui lòng chờ kết quả trước khi nộp thêm.");
        }
    }

    private async Task NotifyVaultLockedAsync(Vault vault, HandoverReport report, CancellationToken ct)
    {
        string ownerName = await FindUserNameAsync(vault.OwnerId, ct);
        string submitterName = await FindUserNameAsync(report.SubmitterUserId, ct);
        await NotifyAsync(vault.OwnerId, HandoverRequestNotice.Owner, HandoverRequestNotice.StageRequest, ownerName, submitterName, ct);
        foreach (Executor executor in await executorReader.GetActiveAsync(vault.Id, ct))
        {
            if (executor.UserId != null)
            {
                await NotifyAsync(executor.UserId.Value, HandoverRequestNotice.Executor, HandoverRequestNotice.StageRequest,
                    ownerName, submitterName, ct);
            }
        }
    }

    /// <summary>Gửi một thư báo. Lỗi thư không làm hỏng thao tác đã lưu; chỉ ghi log, không ghi email hay tên.</summary>
    private async Task NotifyAsync(
        Guid targetId, string audience, string stage, string ownerName, string requesterName, CancellationToken ct)
    {
        try
        {
            User? target = await users.FindByIdAsync(targetId, ct);
            if (target == null || string.IsNullOrWhiteSpace(target.Email))
            {
                return;
            }
            HandoverRequestNotice notice = new HandoverRequestNotice
            {
                ToEmail = target.Email,
                FullName = target.FullName,
                Audience = audience,
                Stage = stage,
                OwnerName = ownerName,
                RequesterName = requesterName
            };
            await noticeSender.SendAsync(notice, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Không gửi được thư báo hồ sơ ({Audience}): {Error}", audience, ex.GetType().Name);
        }
    }

    // ---------------- Hàm phụ: kiểm dữ liệu ----------------

    private static HandoverReportData ReadRequest(SubmitHandoverReportRequest request, DateOnly today)
    {
        HandoverReportData data = new HandoverReportData();
        data.DeceasedFullName = CleanText(request.DeceasedFullName);
        if (data.DeceasedFullName.Length == 0 || data.DeceasedFullName.Length > 200)
        {
            throw ValidationErrors.For("deceasedFullName", "Vui lòng nhập họ tên người mất (tối đa 200 ký tự).");
        }
        data.DeceasedCitizenId = CitizenIdRules.NormalizeCitizenId(request.DeceasedCitizenId, "deceasedCitizenId");
        if (request.DateOfDeath == null)
        {
            throw ValidationErrors.For("dateOfDeath", "Vui lòng nhập ngày mất.");
        }
        if (request.DateOfDeath.Value > today || request.DateOfDeath.Value < EarliestDateOfDeath)
        {
            throw ValidationErrors.For("dateOfDeath", "Ngày mất không hợp lệ hoặc ở tương lai.");
        }
        data.DateOfDeath = request.DateOfDeath;
        if (!request.Attested)
        {
            throw ValidationErrors.For("attested", "Vui lòng cam đoan giấy tờ là thật.");
        }
        data.Recipients = ReadRecipients(request.Recipients);
        return data;
    }

    private static readonly DateOnly EarliestDateOfDeath = new DateOnly(1900, 1, 1);

    private static List<SuggestedRecipient> ReadRecipients(string? json)
    {
        List<SuggestedRecipient> result = new List<SuggestedRecipient>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }
        List<SuggestedRecipient?>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<SuggestedRecipient?>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw ValidationErrors.For("recipients", "Danh sách người nhận không đúng định dạng.");
        }
        if (items == null)
        {
            return result;
        }
        if (items.Count > MaxRecipients)
        {
            throw ValidationErrors.For("recipients", "Khai tối đa 20 người nhận.");
        }
        HashSet<string> emails = new HashSet<string>();
        foreach (SuggestedRecipient? item in items)
        {
            if (item == null)
            {
                throw ValidationErrors.For("recipients", "Danh sách người nhận không đúng định dạng.");
            }
            SuggestedRecipient clean = new SuggestedRecipient();
            clean.FullName = CleanText(item.FullName);
            clean.Email = CleanText(item.Email).ToLowerInvariant();
            clean.Phone = NormalizePhone(item.Phone);
            if (clean.FullName.Length == 0 || clean.FullName.Length > 200)
            {
                throw ValidationErrors.For("recipients", "Mỗi người nhận cần có họ tên (tối đa 200 ký tự).");
            }
            if (clean.Email.Length == 0 && clean.Phone.Length == 0)
            {
                throw ValidationErrors.For("recipients", "Mỗi người nhận cần có email hoặc số điện thoại.");
            }
            if (clean.Email.Length > 256 || (clean.Email.Length > 0 && !IsEmail(clean.Email)))
            {
                throw ValidationErrors.For("recipients", "Email của người nhận không hợp lệ.");
            }
            if (clean.Email.Length > 0 && !emails.Add(clean.Email))
            {
                throw ValidationErrors.For("recipients", "Email của người nhận bị trùng.");
            }
            if (clean.Phone.Length > 0 && !PhonePattern().IsMatch(clean.Phone))
            {
                throw ValidationErrors.For("recipients", "Số điện thoại của người nhận không hợp lệ (9 đến 15 chữ số).");
            }
            result.Add(clean);
        }
        return result;
    }

    /// <summary>Bỏ khoảng trắng, dấu chấm, dấu gạch (cùng luật với người nhận của Chủ kho).</summary>
    private static string NormalizePhone(string? phone)
    {
        StringBuilder builder = new StringBuilder();
        foreach (char c in CleanText(phone))
        {
            if (!char.IsWhiteSpace(c) && c != '.' && c != '-')
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    [GeneratedRegex("^\\+?[0-9]{9,15}$")]
    private static partial Regex PhonePattern();

    private static bool IsEmail(string email)
    {
        System.Net.Mail.MailAddress? address;
        return System.Net.Mail.MailAddress.TryCreate(email, out address) && address.Address == email;
    }

    private static string CleanText(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }
        return value.Trim().Normalize();
    }

    private static string NormalizeNote(string? note)
    {
        string text = CleanText(note);
        if (text.Length > 1000)
        {
            throw ValidationErrors.For("note", "Lời nhắn tối đa 1000 ký tự.");
        }
        return text;
    }

    private static void ValidateUploads(HandoverReportUploads uploads)
    {
        if (uploads.DeathCertificates.Count == 0)
        {
            throw ValidationErrors.For("deathCertificates", "Vui lòng đính kèm giấy chứng tử.");
        }
        if (uploads.Wills.Count == 0)
        {
            throw ValidationErrors.For("wills", "Vui lòng đính kèm di chúc.");
        }
        List<UploadedFile> all = new List<UploadedFile>();
        all.AddRange(uploads.DeathCertificates);
        all.AddRange(uploads.Wills);
        all.AddRange(uploads.Others);
        HandoverRecordFileStorage.Validate(all, true);
    }

    // ---------------- Hàm phụ: tệp ----------------

    private async Task StoreFilesAsync(
        HandoverReport report, List<UploadedFile> files, HandoverReportFileKind kind, Guid userId, DateTimeOffset now,
        List<HandoverRecordFile> saved, CancellationToken ct)
    {
        // Dùng lại bộ lưu tệp của biên bản (cùng luật loại tệp, dung lượng, mã hóa) rồi chép sang tệp của hồ sơ.
        List<HandoverRecordFile> stored = await HandoverRecordFileStorage.SaveAllAsync(
            contentStore, files, Guid.Empty, null, HandoverRecordFileKind.ClaimDocument, userId, now, ct);
        saved.AddRange(stored);
        foreach (HandoverRecordFile item in stored)
        {
            HandoverReportFile file = new HandoverReportFile
            {
                ReportId = report.Id,
                Kind = kind,
                FileName = item.FileName,
                ContentType = item.ContentType,
                SizeBytes = item.SizeBytes,
                StorageKey = item.StorageKey,
                EncryptionKeyId = item.EncryptionKeyId,
                WrappedDataKey = item.WrappedDataKey,
                CreatedAt = item.CreatedAt
            };
            reports.AddFile(file);
            // EF tự gắn tệp vào hồ sơ đang theo dõi; chỉ gắn tay khi chưa có.
            if (!report.Files.Contains(file))
            {
                report.Files.Add(file);
            }
        }
    }

    private async Task<PreparedDownload> PrepareFileAsync(HandoverReport report, Guid fileId, CancellationToken ct)
    {
        foreach (HandoverReportFile file in report.Files)
        {
            if (file.Id == fileId)
            {
                HandoverRecordFile stored = new HandoverRecordFile
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    SizeBytes = file.SizeBytes,
                    StorageKey = file.StorageKey,
                    EncryptionKeyId = file.EncryptionKeyId,
                    WrappedDataKey = file.WrappedDataKey
                };
                return await HandoverRecordFileStorage.PrepareAsync(contentStore, stored, ct);
            }
        }
        throw HandoverAccess.NotFound();
    }

    // ---------------- Hàm phụ: phản hồi ----------------

    private async Task<MyHandoverReportResponse> BuildMineAsync(HandoverReport report, CancellationToken ct)
    {
        MyHandoverReportResponse response = new MyHandoverReportResponse();
        await FillAsync(response, report, ReadData(report), ct);
        return response;
    }

    private async Task<VerifierHandoverReportResponse> BuildForVerifierAsync(HandoverReport report, CancellationToken ct)
    {
        HandoverReportData data = ReadData(report);
        VerifierHandoverReportResponse response = new VerifierHandoverReportResponse();
        await FillAsync(response, report, data, ct);
        response.DeceasedCitizenId = data.DeceasedCitizenId;
        response.RecordId = report.RecordId;
        response.AcceptNote = report.AcceptNote;
        User? submitter = await users.FindByIdAsync(report.SubmitterUserId, ct);
        if (submitter != null)
        {
            response.SubmitterName = submitter.FullName;
            response.SubmitterEmail = submitter.Email;
            if (submitter.Phone != null)
            {
                response.SubmitterPhone = submitter.Phone;
            }
        }
        if (report.VaultId != null)
        {
            Vault? vault = await vaults.FindByIdAsync(report.VaultId.Value, ct);
            if (vault != null)
            {
                response.VaultFound = true;
                User? owner = await users.FindByIdAsync(vault.OwnerId, ct);
                if (owner != null)
                {
                    response.OwnerName = owner.FullName;
                    response.OwnerLastActiveAt = await FindLastActiveAsync(owner, ct);
                    response.ActiveAfterDeath = IsAfterDeath(response.OwnerLastActiveAt, data.DateOfDeath);
                }
                OwnerIdentity? identity = await identities.FindByUserIdAsync(vault.OwnerId, ct);
                if (identity != null)
                {
                    OwnerIdentityData? ownerData = ReadIdentity(identity);
                    response.NameMatches = ownerData != null
                        && EkycRules.NormalizeNameForCompare(ownerData.FullName)
                            == EkycRules.NormalizeNameForCompare(data.DeceasedFullName);
                }
            }
        }
        return response;
    }

    /// <summary>So theo ngày ở Việt Nam; hoạt động đúng ngày mất không tính là cảnh báo.</summary>
    private static bool IsAfterDeath(DateTimeOffset? lastActive, DateOnly? dateOfDeath)
    {
        if (lastActive == null || dateOfDeath == null)
        {
            return false;
        }
        DateOnly activeDay = DateOnly.FromDateTime(lastActive.Value.ToOffset(TimeSpan.FromHours(7)).DateTime);
        return activeDay > dateOfDeath.Value;
    }

    private async Task FillAsync(
        MyHandoverReportResponse response, HandoverReport report, HandoverReportData data, CancellationToken ct)
    {
        response.Id = report.Id;
        response.DeceasedFullName = data.DeceasedFullName;
        response.Status = report.Status.ToApiString();
        response.DateOfDeath = data.DateOfDeath;
        response.Note = report.Note;
        response.ReviewReason = report.ReviewReason;
        response.CreatedAt = report.CreatedAt;
        response.ReviewedAt = report.ReviewedAt;
        if (report.RecordId != null)
        {
            HandoverRecord? record = await records.FindByIdAsync(report.RecordId.Value, ct);
            if (record != null && record.Status == HandoverRecordStatus.Cancelled)
            {
                response.CancelledByOwner = record.CancelledByVerifierUserId == null;
                response.CancelReason = record.CancelReason;
            }
        }
        foreach (SuggestedRecipient item in data.Recipients)
        {
            SuggestedRecipientResponse recipient = new SuggestedRecipientResponse
            {
                FullName = item.FullName,
                Email = item.Email,
                Phone = item.Phone
            };
            response.Recipients.Add(recipient);
        }
        foreach (HandoverReportFile file in report.Files.OrderBy(f => f.Kind).ThenBy(f => f.CreatedAt))
        {
            HandoverReportFileResponse item = new HandoverReportFileResponse
            {
                Id = file.Id,
                Kind = file.Kind.ToApiString(),
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                CreatedAt = file.CreatedAt
            };
            response.Files.Add(item);
        }
    }

    private HandoverReportData ReadData(HandoverReport report)
    {
        try
        {
            return protector.Unprotect(report.DataProtected);
        }
        catch (CryptographicException)
        {
            // Hồ sơ không giải mã được: vẫn hiện trong danh sách nhưng không có thông tin cá nhân.
            return new HandoverReportData();
        }
    }

    private async Task<string> FindUserNameAsync(Guid userId, CancellationToken ct)
    {
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null)
        {
            return string.Empty;
        }
        return user.FullName;
    }

    // ---------------- Hàm phụ: quyền ----------------

    private async Task<User> RequireSubmitterAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản không hoạt động.");
        }
        if (user.Role != UserRole.User)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.RoleNotAllowed,
                "Tài khoản nhân sự không nộp hồ sơ báo tin.");
        }
        if (user.EmailVerifiedAt == null)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.EmailNotVerified,
                "Vui lòng xác minh email trước khi nộp hồ sơ.");
        }
        return user;
    }

    private async Task<HandoverReport> RequireMineAsync(Guid reportId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        HandoverReport? report = await reports.FindByIdAsync(reportId, ct);
        if (report == null || report.SubmitterUserId != userId)
        {
            throw HandoverAccess.NotFound();
        }
        return report;
    }

    private async Task<Guid> RequireActiveVerifierAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.AccountNotActive, "Tài khoản không hoạt động.");
        }
        if (user.Role != UserRole.Verifier)
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.RoleNotAllowed, "Chỉ luật sư mới xét được hồ sơ báo tin.");
        }
        return userId;
    }

    private async Task<HandoverReport> RequireAssignedAsync(Guid reportId, Guid userId, CancellationToken ct)
    {
        HandoverReport? report = await reports.FindByIdAsync(reportId, ct);
        if (report == null || report.VerifierUserId != userId)
        {
            throw HandoverAccess.NotFound();
        }
        return report;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (handover.IsUniqueViolation(ex) || ex.GetType().Name == "DbUpdateConcurrencyException")
        {
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Dữ liệu vừa được sửa ở nơi khác. Vui lòng tải lại rồi thử lại.");
        }
    }
}

using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.DeathCases;

// Các class giả để test hồ sơ chứng tử mà không cần SQL Server. Dữ liệu chỉ nằm trong List.

public sealed class FakeDeathCaseRepository : IDeathCaseRepository
{
    public List<DeathCase> Cases { get; } = new List<DeathCase>();
    public List<DeathCaseNotice> Notices { get; } = new List<DeathCaseNotice>();
    public List<DeathCaseFile> Files { get; } = new List<DeathCaseFile>();
    public List<DeathCaseFlag> Flags { get; } = new List<DeathCaseFlag>();
    public List<DeathCaseAssignment> Assignments { get; } = new List<DeathCaseAssignment>();
    public List<DeathCaseVersion> Versions { get; } = new List<DeathCaseVersion>();

    public Task<DeathCase?> FindByIdAsync(Guid caseId, CancellationToken ct)
    {
        return Task.FromResult(Cases.FirstOrDefault(c => c.Id == caseId));
    }

    public Task<DeathCase?> FindOpenByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(Cases.FirstOrDefault(c => c.VaultId == vaultId && c.Status != DeathCaseStatus.Rejected));
    }

    public Task<List<ExecutorVaultRow>> FindExecutorVaultsAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(new List<ExecutorVaultRow>());
    }

    public Task<DeathCase?> FindLatestRejectedByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(Cases
            .Where(c => c.VaultId == vaultId && c.Status == DeathCaseStatus.Rejected)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());
    }

    public Task<bool> TryAddAsync(DeathCase deathCase, CancellationToken ct)
    {
        if (Cases.Any(c => c.VaultId == deathCase.VaultId && c.Status != DeathCaseStatus.Rejected))
        {
            return Task.FromResult(false);
        }
        Cases.Add(deathCase);
        return Task.FromResult(true);
    }

    public void AddVersion(DeathCaseVersion version)
    {
        Versions.Add(version);
    }

    public void AddFile(DeathCaseFile file)
    {
        Files.Add(file);
    }

    public void RemoveFile(DeathCaseFile file)
    {
        Files.Remove(file);
    }

    public void AddFlag(DeathCaseFlag flag)
    {
        Flags.Add(flag);
    }

    public void AddNotice(DeathCaseNotice notice)
    {
        Notices.Add(notice);
    }

    public void AddAssignment(DeathCaseAssignment assignment)
    {
        Assignments.Add(assignment);
    }

    public Task<int> CountFilesUsingStorageKeyAsync(string storageKey, CancellationToken ct)
    {
        return Task.FromResult(Files.Count(f => f.StorageKey == storageKey));
    }

    public Task<bool> HasCheckInAtOrAfterAsync(Guid planId, DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        return Task.FromResult(false);
    }

    public Task<Guid?> FindVerifierCandidateAsync(List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct)
    {
        return Task.FromResult<Guid?>(null);
    }

    public Task<List<Guid>> FindPendingNoticeIdsAsync(int maxCount, CancellationToken ct)
    {
        return Task.FromResult(Notices
            .Where(n => n.Status == DeathCaseNoticeStatus.Pending)
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Id)
            .Take(maxCount)
            .ToList());
    }

    public Task<DeathCaseNotice?> FindNoticeForJobAsync(Guid noticeId, CancellationToken ct)
    {
        return Task.FromResult(Notices.FirstOrDefault(n => n.Id == noticeId));
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return false;
    }

    public void DiscardPendingChanges()
    {
    }
}

/// <summary>Bộ gửi thư nhắc Verifier giả: ghi lại thư đã gửi; FailNext = true thì lần gọi kế tiếp ném lỗi như SMTP không nhận thư.</summary>
public sealed class FakeDeathCaseVerifierReminderSender : IDeathCaseVerifierReminderSender
{
    public List<DeathCaseVerifierReminder> Sent { get; } = new List<DeathCaseVerifierReminder>();

    public bool FailNext { get; set; }

    public int CallCount { get; private set; }

    public Task SendAsync(DeathCaseVerifierReminder reminder, CancellationToken ct)
    {
        CallCount++;
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("SMTP giả không nhận thư.");
        }
        Sent.Add(reminder);
        return Task.CompletedTask;
    }
}

/// <summary>Bộ gửi giả: ghi lại thông báo đã gửi; Fail = true thì ném lỗi như SMTP không nhận thư.</summary>
public sealed class FakeDeathCaseNoticeSender : IDeathCaseNoticeSender
{
    public List<DeathCaseOwnerNotice> Sent { get; } = new List<DeathCaseOwnerNotice>();

    public bool Fail { get; set; }

    public int CallCount { get; private set; }

    public Task SendAsync(DeathCaseOwnerNotice notice, CancellationToken ct)
    {
        CallCount++;
        if (Fail)
        {
            throw new InvalidOperationException("SMTP giả không nhận thư.");
        }
        Sent.Add(notice);
        return Task.CompletedTask;
    }
}

/// <summary>Dịch vụ sự cố giả: chỉ ghi lại các sự cố được mở, các thao tác của Admin không dùng trong test job.</summary>
public sealed class FakeIncidentService : IIncidentService
{
    public List<(IncidentType Type, Guid? VaultId, string TargetType, Guid? TargetId, Guid? Preferred)> Raised { get; }
        = new List<(IncidentType, Guid?, string, Guid?, Guid?)>();

    public Task<Incident> RaiseAsync(
        IncidentType type, Guid? vaultId, string targetType, Guid? targetId, string summary,
        Guid? preferredAssigneeUserId, CancellationToken ct)
    {
        Raised.Add((type, vaultId, targetType, targetId, preferredAssigneeUserId));
        return Task.FromResult(new Incident());
    }

    public Task ResolveByTargetAsync(IncidentType type, string targetType, Guid? targetId, string note, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task ReassignByTargetAsync(
        IncidentType type, string targetType, Guid? targetId, Guid newAssigneeUserId, string note, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<PaginatedList<IncidentSummaryResponse>> ListAsync(
        string? status, string? type, Guid? assigneeUserId, bool overdueOnly, int pageIndex, int pageSize, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public Task<IncidentDetailResponse> GetAsync(Guid incidentId, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public Task<IncidentDetailResponse> AssignAsync(Guid incidentId, Guid assigneeUserId, string reason, string etag, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public Task<IncidentDetailResponse> AddNoteAsync(Guid incidentId, string note, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public Task<IncidentDetailResponse> ReviewAsync(Guid incidentId, DateTimeOffset nextReviewAt, string note, string etag, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public Task<IncidentDetailResponse> ResolveAsync(Guid incidentId, string note, string etag, CancellationToken ct)
    {
        throw new NotSupportedException();
    }
}

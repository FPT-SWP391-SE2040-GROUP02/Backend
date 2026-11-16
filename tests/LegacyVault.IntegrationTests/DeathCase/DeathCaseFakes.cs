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
    public List<DeathCaseFile> Files { get; } = new List<DeathCaseFile>();
    public List<DeathCaseFlag> Flags { get; } = new List<DeathCaseFlag>();
    public List<DeathCaseAssignment> Assignments { get; } = new List<DeathCaseAssignment>();
    public List<DeathCaseVersion> Versions { get; } = new List<DeathCaseVersion>();

    public Task<DeathCase?> FindByIdAsync(Guid caseId, CancellationToken ct)
    {
        return Task.FromResult(Cases.FirstOrDefault(c => c.Id == caseId));
    }

    public Task<DeathCaseAssignment?> FindAliveConfirmedAssignmentAsync(Guid caseId, Guid verifierUserId, CancellationToken ct)
    {
        return Task.FromResult<DeathCaseAssignment?>(null);
    }

    public Task<DeathCase?> FindOpenByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        return Task.FromResult(Cases.FirstOrDefault(c => c.VaultId == vaultId && c.Status != DeathCaseStatus.Rejected));
    }

    /// <summary>Các kho mà người gọi làm Người thực thi, đặt sẵn trong test; mặc định rỗng.</summary>
    public List<ExecutorVaultRow> ExecutorRows { get; } = new List<ExecutorVaultRow>();

    public Task<List<ExecutorVaultRow>> FindExecutorVaultsAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(new List<ExecutorVaultRow>(ExecutorRows));
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

    public void RemoveVersion(DeathCaseVersion version)
    {
        Versions.Remove(version);
    }

    public void RemoveCase(DeathCase deathCase)
    {
        Cases.Remove(deathCase);
    }

    public void AddFlag(DeathCaseFlag flag)
    {
        Flags.Add(flag);
    }

    public void AddAssignment(DeathCaseAssignment assignment)
    {
        Assignments.Add(assignment);
    }

    public Task<int> CountFilesUsingStorageKeyAsync(string storageKey, CancellationToken ct)
    {
        return Task.FromResult(Files.Count(f => f.StorageKey == storageKey));
    }

    public Task<Guid?> FindVerifierCandidateAsync(List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct)
    {
        return Task.FromResult<Guid?>(null);
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


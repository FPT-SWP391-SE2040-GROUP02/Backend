using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>Biên bản bàn giao giả, lưu trong List (không cần SQL Server).</summary>
public sealed class FakeHandoverRecordRepository : IHandoverRecordRepository
{
    public List<HandoverRecord> Records { get; } = new List<HandoverRecord>();

    public List<Asset> Assets { get; } = new List<Asset>();

    public List<Beneficiary> Beneficiaries { get; } = new List<Beneficiary>();

    public List<Executor> Executors { get; } = new List<Executor>();

    /// <summary>Luật sư trả về khi phân công; null là không còn ai.</summary>
    public Guid? VerifierCandidate { get; set; }

    public Task<HandoverRecord?> FindByIdAsync(Guid recordId, CancellationToken ct)
    {
        foreach (HandoverRecord record in Records)
        {
            if (record.Id == recordId)
            {
                return Task.FromResult<HandoverRecord?>(record);
            }
        }
        return Task.FromResult<HandoverRecord?>(null);
    }

    public Task<HandoverRecord?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        foreach (HandoverRecord record in Records)
        {
            if (record.VaultId == vaultId)
            {
                return Task.FromResult<HandoverRecord?>(record);
            }
        }
        return Task.FromResult<HandoverRecord?>(null);
    }

    public Task<List<HandoverRecord>> ListByVerifierAsync(Guid verifierUserId, CancellationToken ct)
    {
        List<HandoverRecord> result = new List<HandoverRecord>();
        foreach (HandoverRecord record in Records)
        {
            if (record.VerifierUserId == verifierUserId)
            {
                result.Add(record);
            }
        }
        return Task.FromResult(result);
    }

    public Task<Guid?> FindVerifierCandidateAsync(List<Guid> excludedUserIds, List<string> excludedEmails, CancellationToken ct)
    {
        if (VerifierCandidate != null && excludedUserIds.Contains(VerifierCandidate.Value))
        {
            return Task.FromResult<Guid?>(null);
        }
        return Task.FromResult(VerifierCandidate);
    }

    public Task<List<Asset>> ListVaultAssetsAsync(Guid vaultId, CancellationToken ct)
    {
        List<Asset> result = new List<Asset>();
        foreach (Asset asset in Assets)
        {
            if (asset.VaultId == vaultId && asset.ContentDeletedAt == null)
            {
                result.Add(asset);
            }
        }
        return Task.FromResult(result);
    }

    public Task<List<Beneficiary>> ListVaultBeneficiariesAsync(Guid vaultId, CancellationToken ct)
    {
        List<Beneficiary> result = new List<Beneficiary>();
        foreach (Beneficiary beneficiary in Beneficiaries)
        {
            if (beneficiary.VaultId == vaultId)
            {
                result.Add(beneficiary);
            }
        }
        return Task.FromResult(result);
    }

    public Task<List<Beneficiary>> ListBeneficiariesOfActivatedPlansAsync(CancellationToken ct)
    {
        // Fake không giữ kho và kế hoạch: test thuần dùng fake này không gọi danh sách kho của người nhận.
        throw new NotSupportedException();
    }

    public Task<List<Executor>> ListAcceptedExecutorsAsync(Guid userId, CancellationToken ct)
    {
        List<Executor> result = new List<Executor>();
        foreach (Executor executor in Executors)
        {
            if (executor.UserId == userId)
            {
                result.Add(executor);
            }
        }
        return Task.FromResult(result);
    }

    public void Add(HandoverRecord record)
    {
        Records.Add(record);
    }

    public List<HandoverTurn> Turns { get; } = new List<HandoverTurn>();

    public Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid recordId, CancellationToken ct)
    {
        List<HandoverTurn> result = new List<HandoverTurn>();
        foreach (HandoverTurn turn in Turns)
        {
            if (turn.RecordId == recordId)
            {
                result.Add(turn);
            }
        }
        return Task.FromResult(result);
    }

    public void AddSnapshotItem(HandoverSnapshotItem item)
    {
    }

    public void RemoveSnapshotItem(HandoverSnapshotItem item)
    {
    }

    public void RemoveLine(HandoverRecordLine line)
    {
    }

    public void AddLine(HandoverRecordLine line)
    {
    }

    public void AddSignature(HandoverRecordSignature signature)
    {
    }

    public Task<Beneficiary?> FindBeneficiaryByRequestTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        foreach (Beneficiary beneficiary in Beneficiaries)
        {
            if (beneficiary.RequestTokenHash == tokenHash)
            {
                return Task.FromResult<Beneficiary?>(beneficiary);
            }
        }
        return Task.FromResult<Beneficiary?>(null);
    }

    public Task<List<HandoverClaim>> ListClaimsByUserAsync(Guid userId, CancellationToken ct)
    {
        List<HandoverClaim> result = new List<HandoverClaim>();
        foreach (HandoverRecord record in Records)
        {
            foreach (HandoverClaim claim in record.Claims)
            {
                if (claim.UserId == userId)
                {
                    result.Add(claim);
                }
            }
        }
        return Task.FromResult(result);
    }

    public void AddClaim(HandoverClaim claim)
    {
    }

    public void AddFile(HandoverRecordFile file)
    {
    }

    public void RemoveFile(HandoverRecordFile file)
    {
    }

    public void AddBeneficiary(Beneficiary beneficiary)
    {
        Beneficiaries.Add(beneficiary);
    }

    public Task<Beneficiary?> FindBeneficiaryForUpdateAsync(Guid beneficiaryId, CancellationToken ct)
    {
        foreach (Beneficiary beneficiary in Beneficiaries)
        {
            if (beneficiary.Id == beneficiaryId)
            {
                return Task.FromResult<Beneficiary?>(beneficiary);
            }
        }
        return Task.FromResult<Beneficiary?>(null);
    }
}

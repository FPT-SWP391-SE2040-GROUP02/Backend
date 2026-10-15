using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <remarks>
/// ROLE-03 vế người nhận chưa kiểm: người nhận chưa gắn tài khoản trước F3 nên chưa loại được.
/// Không loại người từng giữ hồ sơ trước đó (chỉ loại người đang giữ) để nhóm chỉ có 2 Verifier vẫn chuyền được (D5).
/// </remarks>
public sealed class DeathCaseVerifierPicker : IDeathCaseVerifierPicker
{
    private readonly IDeathCaseRepository deathCaseRepository;
    private readonly IExecutorReader executorReader;

    public DeathCaseVerifierPicker(IDeathCaseRepository deathCaseRepository, IExecutorReader executorReader)
    {
        this.deathCaseRepository = deathCaseRepository;
        this.executorReader = executorReader;
    }

    public async Task<Guid?> PickAsync(
        Vault vault, Guid submitterUserId, Guid? currentVerifierUserId, CancellationToken ct)
    {
        List<Guid> excluded = new List<Guid>();
        excluded.Add(vault.OwnerId);
        excluded.Add(submitterUserId);
        if (currentVerifierUserId != null)
        {
            excluded.Add(currentVerifierUserId.Value);
        }

        List<Executor> executors = await executorReader.GetActiveAsync(vault.Id, ct);
        foreach (Executor executor in executors)
        {
            if (executor.UserId != null)
            {
                excluded.Add(executor.UserId.Value);
            }
        }

        Guid? verifierId = await deathCaseRepository.FindVerifierCandidateAsync(excluded, ct);
        return verifierId;
    }
}

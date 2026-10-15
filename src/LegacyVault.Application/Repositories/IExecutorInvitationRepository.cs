using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Tracked invitation lookup; raw tokens never reach persistence.</summary>
public interface IExecutorInvitationRepository
{
    Task<Executor?> FindByTokenHashAsync(string hash, CancellationToken ct);
    void Add(Executor executor);
}

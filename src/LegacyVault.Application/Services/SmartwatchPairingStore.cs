using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LegacyVault.Application.Services;

public record SmartwatchPairingSession(Guid UserId, Guid VaultId, DateTimeOffset ExpiresAt);

public interface ISmartwatchPairingStore
{
    string CreateCode(Guid userId, Guid vaultId, TimeSpan lifetime);
    bool TryGetAndRemove(string code, out SmartwatchPairingSession session);
}

public sealed class SmartwatchPairingStore : ISmartwatchPairingStore
{
    private readonly ConcurrentDictionary<string, SmartwatchPairingSession> store = new();

    public string CreateCode(Guid userId, Guid vaultId, TimeSpan lifetime)
    {
        // Sinh ngẫu nhiên 6 chữ số
        string code = RandomNumberGenerator.GetInt32(100000, 999999).ToString("D6");
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        store[code] = new SmartwatchPairingSession(userId, vaultId, expiresAt);
        return code;
    }

    public bool TryGetAndRemove(string code, out SmartwatchPairingSession session)
    {
        if (store.TryRemove(code, out var existing))
        {
            if (existing.ExpiresAt >= DateTimeOffset.UtcNow)
            {
                session = existing;
                return true;
            }
        }

        session = default!;
        return false;
    }
}

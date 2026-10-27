using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LegacyVault.Application.Services;

public sealed class SmartwatchPairingSession
{
    public Guid UserId { get; }
    public Guid VaultId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsPaired { get; private set; }
    public string? DeviceName { get; private set; }
    public DateTimeOffset? PairedAt { get; private set; }

    public SmartwatchPairingSession(Guid userId, Guid vaultId, DateTimeOffset expiresAt)
    {
        UserId = userId;
        VaultId = vaultId;
        ExpiresAt = expiresAt;
    }

    public void MarkPaired(string? deviceName)
    {
        IsPaired = true;
        DeviceName = deviceName;
        PairedAt = DateTimeOffset.UtcNow;
    }
}

public interface ISmartwatchPairingStore
{
    string CreateCode(Guid userId, Guid vaultId, TimeSpan lifetime);
    bool TryGetAndRemove(string code, out SmartwatchPairingSession session);
    bool TryPair(string code, string? deviceName, out SmartwatchPairingSession session);
    bool TryGetStatus(string code, Guid userId, out SmartwatchPairingSession session);
}

public sealed class SmartwatchPairingStore : ISmartwatchPairingStore
{
    private readonly ConcurrentDictionary<string, SmartwatchPairingSession> store = new();

    public string CreateCode(Guid userId, Guid vaultId, TimeSpan lifetime)
    {
        CleanupExpired();
        // Sinh ngẫu nhiên 6 chữ số
        string code = RandomNumberGenerator.GetInt32(100000, 999999).ToString("D6");
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        store[code] = new SmartwatchPairingSession(userId, vaultId, expiresAt);
        return code;
    }

    public bool TryGetAndRemove(string code, out SmartwatchPairingSession session)
    {
        return TryPair(code, null, out session);
    }

    public bool TryPair(string code, string? deviceName, out SmartwatchPairingSession session)
    {
        if (store.TryGetValue(code, out var existing))
        {
            if (existing.ExpiresAt >= DateTimeOffset.UtcNow && !existing.IsPaired)
            {
                existing.MarkPaired(deviceName);
                session = existing;
                return true;
            }
        }

        session = default!;
        return false;
    }

    public bool TryGetStatus(string code, Guid userId, out SmartwatchPairingSession session)
    {
        if (store.TryGetValue(code, out var existing))
        {
            if (existing.UserId == userId && existing.ExpiresAt >= DateTimeOffset.UtcNow)
            {
                session = existing;
                return true;
            }
        }

        session = default!;
        return false;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in store)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                store.TryRemove(kvp.Key, out _);
            }
        }
    }
}

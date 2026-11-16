using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Notifications;

public sealed class EmailBounceStore(AppDbContext db) : IEmailBounceStore
{
    public async Task RecordAsync(string email, string failureCode, DateTimeOffset detectedAt, CancellationToken ct)
    {
        string hash = EmailBounceHash.Of(email);
        EmailBounce? existing = await db.EmailBounces.FirstOrDefaultAsync(b => b.EmailHash == hash, ct);
        if (existing == null)
        {
            db.EmailBounces.Add(new EmailBounce
            {
                Id = Guid.NewGuid(), EmailHash = hash, FailureCode = failureCode, DetectedAt = detectedAt,
            });
        }
        else
        {
            existing.FailureCode = failureCode;
            existing.DetectedAt = detectedAt;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<string>> FindBouncedAsync(IEnumerable<string> emails, CancellationToken ct)
    {
        Dictionary<string, string> byHash = new();
        foreach (string email in emails)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                byHash[EmailBounceHash.Of(email)] = email.Trim().ToLowerInvariant();
            }
        }
        if (byHash.Count == 0)
        {
            return new HashSet<string>();
        }
        List<string> hashes = byHash.Keys.ToList();
        List<string> hit = await db.EmailBounces.Where(b => hashes.Contains(b.EmailHash)).Select(b => b.EmailHash).ToListAsync(ct);
        return hit.Select(h => byHash[h]).ToHashSet();
    }
}

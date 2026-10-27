using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.GoogleAuth;

// Các class giả lập (fake) để test GoogleAuthService mà không cần SQL Server hay mạng tới Google.
// Mỗi fake chỉ giữ dữ liệu trong List, đủ để kiểm tra service đã đọc/ghi đúng.

/// <summary>Google giả: chỉ chấp nhận các token đã đăng ký trước bằng Accept().</summary>
public sealed class FakeGoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly Dictionary<string, GoogleIdentity> _validTokens = new();

    public void Accept(string idToken, GoogleIdentity identity)
    {
        _validTokens[idToken] = identity;
    }

    public Task<GoogleIdentity?> VerifyAsync(string idToken, string? expectedNonce, CancellationToken ct)
    {
        _validTokens.TryGetValue(idToken, out var identity);
        return Task.FromResult(identity);
    }
}

/// <summary>Đồng hồ cố định để test không phụ thuộc giờ thật.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    // Fake không chứng minh locking/rollback SQL; checks SQL riêng kiểm tra invariant này.
    public Task<T> ExecuteSerializableAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Token giả: hash = "hash-" + giá trị gốc, đủ để kiểm tra service có hash trước khi tìm hay không.</summary>
public sealed class FakeTokenService : ITokenService
{
    public AccessToken CreateAccessToken(User user)
    {
        return new AccessToken { Value = "access-" + user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
    }

    public AccessToken CreateSmartwatchToken(User user, Guid vaultId, string deviceId)
    {
        return new AccessToken
        {
            Value = "smartwatch-" + user.Id + "-" + vaultId + "-" + deviceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
    }

    public OpaqueToken CreateOpaqueToken()
    {
        var value = Guid.NewGuid().ToString("N");
        return new OpaqueToken { Value = value, Hash = HashOpaqueToken(value) };
    }

    public string HashOpaqueToken(string value) => "hash-" + value;
}

/// <summary>"Băm" giả: chỉ thêm tiền tố, để test đặt mật khẩu dễ dàng.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hashed-" + password;

    public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
}

public sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        return Task.FromResult(Users.FirstOrDefault(u => u.Email == normalizedEmail));
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    }

    public Task<bool> TryAddAsync(User user, CancellationToken ct)
    {
        if (Users.Any(u => u.Email == user.Email))
        {
            return Task.FromResult(false);
        }
        Users.Add(user);
        return Task.FromResult(true);
    }
}

/// <summary>Giả lập cả 2 index unique của bảng ExternalLogins và việc EF lưu luôn User mới gắn kèm.</summary>
public sealed class FakeExternalLoginRepository(FakeUserRepository userRepository) : IExternalLoginRepository
{
    public List<ExternalLogin> Logins { get; } = new();

    public Task<ExternalLogin?> FindBySubjectAsync(LoginProvider provider, string subject, CancellationToken ct)
    {
        return Task.FromResult(Logins.FirstOrDefault(l => l.Provider == provider && l.ProviderSubject == subject));
    }

    public Task<ExternalLogin?> FindByUserAsync(Guid userId, LoginProvider provider, CancellationToken ct)
    {
        return Task.FromResult(Logins.FirstOrDefault(l => l.UserId == userId && l.Provider == provider));
    }

    public Task<List<ExternalLogin>> FindAllByUserAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Logins.Where(l => l.UserId == userId).ToList());
    }

    public Task<bool> TryAddAsync(ExternalLogin login, CancellationToken ct)
    {
        var duplicate = Logins.Any(l =>
            (l.Provider == login.Provider && l.ProviderSubject == login.ProviderSubject)
            || (l.UserId == login.UserId && l.Provider == login.Provider));
        if (duplicate)
        {
            return Task.FromResult(false);
        }

        // Giống EF: User mới gắn kèm liên kết được thêm cùng lúc.
        if (login.User != null && !userRepository.Users.Contains(login.User))
        {
            userRepository.Users.Add(login.User);
        }

        // Giống Include(User) khi đọc lại từ DB.
        login.User ??= userRepository.Users.First(u => u.Id == login.UserId);
        Logins.Add(login);
        return Task.FromResult(true);
    }

    public void Remove(ExternalLogin login)
    {
        Logins.Remove(login);
    }
}

public sealed class FakeLinkTicketRepository(FakeUserRepository userRepository) : IExternalLoginLinkTicketRepository
{
    public List<ExternalLoginLinkTicket> Tickets { get; } = new();

    public Task<ExternalLoginLinkTicket?> FindByHashAsync(string ticketHash, CancellationToken ct)
    {
        var ticket = Tickets.FirstOrDefault(t => t.TicketHash == ticketHash);
        if (ticket != null)
        {
            ticket.User = userRepository.Users.First(u => u.Id == ticket.UserId);
        }
        return Task.FromResult(ticket);
    }

    public void Add(ExternalLoginLinkTicket ticket)
    {
        Tickets.Add(ticket);
    }
}

public sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    public List<RefreshToken> Tokens { get; } = new();

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
    {
        return Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));
    }

    public Task<List<RefreshToken>> FindActiveByFamilyAsync(Guid familyId, CancellationToken ct)
    {
        return Task.FromResult(Tokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToList());
    }

    public Task<List<RefreshToken>> FindActiveByUserAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Tokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToList());
    }

    public void Add(RefreshToken token)
    {
        Tokens.Add(token);
    }
}

public sealed class FakeLoginThrottleRepository : ILoginThrottleRepository
{
    public List<LoginThrottle> Throttles { get; } = new();

    public Task<LoginThrottle?> FindAsync(string normalizedEmail, string ipRange, CancellationToken ct)
    {
        return Task.FromResult(Throttles.FirstOrDefault(t => t.Email == normalizedEmail && t.IpRange == ipRange));
    }

    public void Add(LoginThrottle throttle)
    {
        Throttles.Add(throttle);
    }

    public void Remove(LoginThrottle throttle)
    {
        Throttles.Remove(throttle);
    }
}

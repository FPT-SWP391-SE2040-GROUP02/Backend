using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Cảnh báo đăng nhập bất thường (SEC-10): lần sai thứ 5 khóa tài khoản và mở sự cố SUSPICIOUS_LOGIN cho Admin.
/// Fake không chứng minh SQL; SuspiciousLoginIncidentSqlTests kiểm tra phần lưu và giao Admin thật.
/// </summary>
public sealed class SuspiciousLoginIncidentTests
{
    private const string IpRange = "203.0.113.0/24";

    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeLoginThrottleRepository throttles = new FakeLoginThrottleRepository();
    private readonly RecordingIncidentService incidents = new RecordingIncidentService();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly User user = new User();

    public SuspiciousLoginIncidentTests()
    {
        user.Email = "user@example.test";
        user.FullName = "Fixture";
        user.Status = UserStatus.Active;
        user.Role = UserRole.User;
        user.PasswordHash = "hashed-right";
        users.Users.Add(user);
    }

    private AuthService CreateService()
    {
        return new AuthService(users, throttles, new FakeRefreshTokenRepository(), new FakeUnitOfWork(),
            new FakePasswordHasher(), new FakeTokenService(), new NoCurrentUser(), clock, incidentService: incidents);
    }

    private async Task<Exception> LoginWrongAsync(string email)
    {
        LoginRequest request = new LoginRequest();
        request.Email = email;
        request.Password = "wrong";
        return await Assert.ThrowsAnyAsync<Exception>(() => CreateService().LoginAsync(request, IpRange, default));
    }

    [Fact]
    public async Task FifthWrongPasswordLocksAccountAndRaisesSuspiciousLoginIncident()
    {
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            Assert.IsType<InvalidCredentialsException>(await LoginWrongAsync(user.Email));
        }
        Assert.Empty(incidents.Raised);

        Assert.IsType<AccountLockedException>(await LoginWrongAsync(user.Email));

        RaisedIncident raised = Assert.Single(incidents.Raised);
        Assert.Equal(IncidentType.SuspiciousLogin, raised.Type);
        Assert.Equal(IncidentRules.TargetUser, raised.TargetType);
        Assert.Equal(user.Id, raised.TargetId);
        Assert.Null(raised.VaultId);
        Assert.Null(raised.PreferredAssigneeUserId);
        Assert.Contains(IpRange, raised.Summary);
    }

    [Fact]
    public async Task LockoutOfUnknownEmailDoesNotRaiseIncident()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            await LoginWrongAsync("nobody@example.test");
        }

        Assert.Single(throttles.Throttles);
        Assert.Empty(incidents.Raised);
    }

    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? UserId
        {
            get { return null; }
        }

        public bool IsAuthenticated
        {
            get { return false; }
        }
    }

    private sealed class RaisedIncident
    {
        public IncidentType Type { get; set; }

        public Guid? VaultId { get; set; }

        public string TargetType { get; set; } = string.Empty;

        public Guid? TargetId { get; set; }

        public string Summary { get; set; } = string.Empty;

        public Guid? PreferredAssigneeUserId { get; set; }
    }

    /// <summary>Chỉ ghi lại lời gọi RaiseAsync; hai hàm còn lại không được AuthService dùng.</summary>
    private sealed class RecordingIncidentService : IIncidentService
    {
        public List<RaisedIncident> Raised { get; } = new List<RaisedIncident>();

        public Task<Incident> RaiseAsync(
            IncidentType type, Guid? vaultId, string targetType, Guid? targetId, string summary,
            Guid? preferredAssigneeUserId, CancellationToken ct)
        {
            RaisedIncident raised = new RaisedIncident();
            raised.Type = type;
            raised.VaultId = vaultId;
            raised.TargetType = targetType;
            raised.TargetId = targetId;
            raised.Summary = summary;
            raised.PreferredAssigneeUserId = preferredAssigneeUserId;
            Raised.Add(raised);
            return Task.FromResult(new Incident());
        }

        public Task ResolveByTargetAsync(IncidentType type, string targetType, Guid? targetId, string note, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task ReassignByTargetAsync(
            IncidentType type, string targetType, Guid? targetId, Guid newAssigneeUserId, string note, CancellationToken ct)
        {
            throw new NotSupportedException();
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
}

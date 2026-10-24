using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Quản lý phương thức đăng nhập của người đang đăng nhập (AUTH-03, xem ILoginMethodService).
/// Mọi thao tác ghi chạy trong transaction Serializable để hai request song song không cùng gỡ phương thức cuối.
/// </summary>
public sealed class LoginMethodService : ILoginMethodService
{
    private const string PasswordMethod = "PASSWORD";
    private const string GoogleMethod = "GOOGLE";

    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;
    private readonly IExternalLoginRepository _externalLoginRepository;
    private readonly IGoogleTokenVerifier _googleTokenVerifier;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public LoginMethodService(
        ICurrentUser currentUser,
        IUserRepository userRepository,
        IExternalLoginRepository externalLoginRepository,
        IGoogleTokenVerifier googleTokenVerifier,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _userRepository = userRepository;
        _externalLoginRepository = externalLoginRepository;
        _googleTokenVerifier = googleTokenVerifier;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<List<LoginMethodResponse>> GetMyLoginMethodsAsync(CancellationToken ct)
    {
        User user = await LoadCurrentUserAsync(ct);
        ExternalLogin? google = await _externalLoginRepository.FindByUserAsync(user.Id, LoginProvider.Google, ct);
        return BuildMethods(user, google);
    }

    public async Task<LoginMethodResponse> LinkGoogleAsync(LinkGoogleRequest request, CancellationToken ct)
    {
        // Xác thực Google ngoài transaction để không giữ khóa DB trong lúc gọi mạng.
        GoogleIdentity identity = await VerifyGoogleAsync(request.IdToken, request.Nonce, ct);

        return await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentUserAsync(ct);
            EnsurePasswordMatches(user, request.CurrentPassword);

            ExternalLogin? existing = await _externalLoginRepository.FindByUserAsync(user.Id, LoginProvider.Google, ct);
            if (existing != null)
            {
                throw new GoogleAccountAlreadyLinkedException();
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            ExternalLogin link = new ExternalLogin
            {
                UserId = user.Id,
                Provider = LoginProvider.Google,
                ProviderSubject = identity.Subject,
                ProviderEmail = identity.Email.Trim().ToLowerInvariant(),
                LinkedAt = now,
                LastUsedAt = null,
            };
            bool added = await _externalLoginRepository.TryAddAsync(link, ct);
            if (!added)
            {
                throw new GoogleAccountAlreadyLinkedException();
            }

            // Không đổi User.Email theo email Google (AUTH-02).
            return ToGoogleResponse(link, true);
        }, ct);
    }

    public async Task VerifyPasswordAsync(VerifyPasswordRequest request, CancellationToken ct)
    {
        User user = await LoadCurrentUserAsync(ct);
        EnsurePasswordMatches(user, request.CurrentPassword);
    }

    public Task UnlinkGoogleAsync(UnlinkGoogleRequest request, CancellationToken ct)
    {
        return _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentUserAsync(ct);
            ExternalLogin? link = await _externalLoginRepository.FindByUserAsync(user.Id, LoginProvider.Google, ct);
            if (link == null)
            {
                // Chưa liên kết: thành công luôn (idempotent).
                return true;
            }

            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                throw new LastLoginMethodException();
            }
            EnsurePasswordMatches(user, request.CurrentPassword);

            _externalLoginRepository.Remove(link);
            user.UpdatedAt = _timeProvider.GetUtcNow();
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task SetPasswordAsync(SetPasswordRequest request, CancellationToken ct)
    {
        GoogleIdentity identity = await VerifyGoogleAsync(request.GoogleIdToken, request.Nonce, ct);

        await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentUserAsync(ct);
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                throw new LoginMethodAlreadyExistsException();
            }

            await EnsureSameGoogleAccountAsync(user, identity, ct);

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            user.UpdatedAt = _timeProvider.GetUtcNow();
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        return _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentUserAsync(ct);
            EnsurePasswordMatches(user, request.CurrentPassword);
            if (request.NewPassword == request.CurrentPassword)
            {
                throw ValidationErrors.For("newPassword", "Mật khẩu mới phải khác mật khẩu hiện tại.");
            }

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            user.UpdatedAt = _timeProvider.GetUtcNow();
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task RemovePasswordAsync(RemovePasswordRequest request, CancellationToken ct)
    {
        GoogleIdentity identity = await VerifyGoogleAsync(request.GoogleIdToken, request.Nonce, ct);

        await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentUserAsync(ct);
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                // Không còn mật khẩu để gỡ: thành công luôn (idempotent).
                return true;
            }

            ExternalLogin? link = await _externalLoginRepository.FindByUserAsync(user.Id, LoginProvider.Google, ct);
            if (link == null)
            {
                throw new LastLoginMethodException();
            }

            EnsurePasswordMatches(user, request.CurrentPassword);
            EnsureSameGoogleAccount(link, identity);

            user.PasswordHash = null;
            user.UpdatedAt = _timeProvider.GetUtcNow();
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private async Task<User> LoadCurrentUserAsync(CancellationToken ct)
    {
        Guid? userId = _currentUser.UserId;
        if (userId == null)
        {
            throw new SessionExpiredException();
        }

        User? user = await _userRepository.FindByIdAsync(userId.Value, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new SessionExpiredException();
        }
        return user;
    }

    private async Task<GoogleIdentity> VerifyGoogleAsync(string idToken, string? nonce, CancellationToken ct)
    {
        GoogleIdentity? identity = await _googleTokenVerifier.VerifyAsync(idToken, nonce, ct);
        if (identity == null || string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Email))
        {
            throw new InvalidGoogleTokenException();
        }
        return identity;
    }

    private void EnsurePasswordMatches(User user, string password)
    {
        bool matches = !string.IsNullOrEmpty(user.PasswordHash)
            && _passwordHasher.Verify(password, user.PasswordHash);
        if (!matches)
        {
            throw new ReauthenticationFailedException();
        }
    }

    private async Task EnsureSameGoogleAccountAsync(User user, GoogleIdentity identity, CancellationToken ct)
    {
        ExternalLogin? link = await _externalLoginRepository.FindByUserAsync(user.Id, LoginProvider.Google, ct);
        if (link == null)
        {
            throw new ReauthenticationFailedException();
        }
        EnsureSameGoogleAccount(link, identity);
    }

    private static void EnsureSameGoogleAccount(ExternalLogin link, GoogleIdentity identity)
    {
        // So bằng sub, không bằng email (email Google có thể đổi, AUTH-01).
        if (!string.Equals(link.ProviderSubject, identity.Subject, StringComparison.Ordinal))
        {
            throw new ReauthenticationFailedException();
        }
    }

    private static List<LoginMethodResponse> BuildMethods(User user, ExternalLogin? google)
    {
        bool hasPassword = !string.IsNullOrEmpty(user.PasswordHash);
        bool hasGoogle = google != null;
        bool canRemove = hasPassword && hasGoogle;

        List<LoginMethodResponse> methods = new List<LoginMethodResponse>();
        if (hasPassword)
        {
            methods.Add(new LoginMethodResponse
            {
                Method = PasswordMethod,
                Email = user.Email,
                LinkedAt = user.CreatedAt,
                LastUsedAt = null,
                CanRemove = canRemove,
            });
        }
        if (google != null)
        {
            methods.Add(ToGoogleResponse(google, canRemove));
        }
        return methods;
    }

    private static LoginMethodResponse ToGoogleResponse(ExternalLogin link, bool canRemove)
    {
        return new LoginMethodResponse
        {
            Method = GoogleMethod,
            Email = link.ProviderEmail,
            LinkedAt = link.LinkedAt,
            LastUsedAt = link.LastUsedAt,
            CanRemove = canRemove,
        };
    }
}

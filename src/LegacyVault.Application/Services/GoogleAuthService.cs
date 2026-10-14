using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đăng nhập bằng Google theo luồng ID token (xem IGoogleAuthService).
/// </summary>
/// <remarks>
/// Ba trường hợp khi FE gửi ID token lên:
/// 1. Google sub đã liên kết với một user → mở phiên cho user đó.
/// 2. Email Google trùng một tài khoản app chưa liên kết → KHÔNG tự gộp, trả vé liên kết;
///    người dùng phải nhập mật khẩu tài khoản app (ConfirmGoogleLinkAsync) để chứng minh là chủ (AUTH-02).
/// 3. Chưa có gì → tạo tài khoản mới (không mật khẩu) + liên kết, rồi mở phiên.
/// </remarks>
public sealed class GoogleAuthService : IGoogleAuthService
{
    private static readonly TimeSpan LinkTicketLifetime = TimeSpan.FromMinutes(10);

    private readonly IGoogleTokenVerifier _googleTokenVerifier;
    private readonly IUserRepository _userRepository;
    private readonly IExternalLoginRepository _externalLoginRepository;
    private readonly IExternalLoginLinkTicketRepository _linkTicketRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LoginSessionIssuer _sessionIssuer;
    private readonly LoginThrottleGuard _throttleGuard;
    private readonly TimeProvider _timeProvider;

    public GoogleAuthService(
        IGoogleTokenVerifier googleTokenVerifier,
        IUserRepository userRepository,
        IExternalLoginRepository externalLoginRepository,
        IExternalLoginLinkTicketRepository linkTicketRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        LoginSessionIssuer sessionIssuer,
        LoginThrottleGuard throttleGuard,
        TimeProvider timeProvider)
    {
        _googleTokenVerifier = googleTokenVerifier;
        _userRepository = userRepository;
        _externalLoginRepository = externalLoginRepository;
        _linkTicketRepository = linkTicketRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _sessionIssuer = sessionIssuer;
        _throttleGuard = throttleGuard;
        _timeProvider = timeProvider;
    }

    public async Task<GoogleLoginResult> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken ct)
    {
        // Chỉ tin thông tin lấy từ token ĐÃ được kiểm tra chữ ký, Client ID, hạn dùng, nonce.
        var identity = await _googleTokenVerifier.VerifyAsync(request.IdToken, request.Nonce, ct);
        if (identity == null || string.IsNullOrWhiteSpace(identity.Subject) || string.IsNullOrWhiteSpace(identity.Email))
        {
            throw new InvalidGoogleTokenException();
        }

        var returnUrl = ReturnUrlValidator.KeepIfLocal(request.ReturnUrl);

        var result = await TryLoginAsync(identity, request.RememberMe, returnUrl, ct);
        if (result != null)
        {
            return result;
        }

        // Lần đầu không lưu được vì một request khác vừa tạo cùng user/liên kết (thường do bấm nút 2 lần).
        // Thử lại đúng một lần: lúc này dữ liệu request kia tạo đã có trong DB nên sẽ rơi vào trường hợp 1 hoặc 2.
        result = await TryLoginAsync(identity, request.RememberMe, returnUrl, ct);
        if (result != null)
        {
            return result;
        }

        throw new InvalidOperationException("Không lưu được tài khoản Google sau khi thử lại.");
    }

    public async Task<AuthResult> ConfirmGoogleLinkAsync(
        ConfirmGoogleLinkRequest request, string ipRange, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();

        // 1. Vé phải tồn tại, chưa dùng, chưa hết hạn. DB chỉ lưu hash nên phải hash vé FE gửi lên để tìm.
        var ticketHash = _tokenService.HashOpaqueToken(request.LinkTicket);
        var ticket = await _linkTicketRepository.FindByHashAsync(ticketHash, ct);
        if (ticket == null || ticket.UsedAt != null || ticket.ExpiresAt <= now)
        {
            throw new InvalidLinkTicketException();
        }

        var user = ticket.User;

        // 2. Kiểm tra mật khẩu tài khoản app, có giới hạn số lần sai như đăng nhập thường (SEC-10).
        await _throttleGuard.ThrowIfLockedAsync(user.Email, ipRange, ct);

        var passwordIsCorrect = !string.IsNullOrEmpty(user.PasswordHash)
            && _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (!passwordIsCorrect)
        {
            throw await _throttleGuard.RecordFailureAsync(user.Email, ipRange, ct);
        }

        // 3. Kiểm tra trạng thái SAU khi mật khẩu đúng (người lạ không dò được trạng thái tài khoản).
        if (user.Status != UserStatus.Active)
        {
            throw new AccountNotActiveException();
        }

        await _throttleGuard.ClearAsync(user.Email, ipRange, ct);

        // 4. Vé chỉ dùng một lần; tạo liên kết Google ↔ user.
        ticket.UsedAt = now;
        user.LastLoginAt = now;

        var link = new ExternalLogin
        {
            UserId = user.Id,
            Provider = LoginProvider.Google,
            ProviderSubject = ticket.ProviderSubject,
            ProviderEmail = ticket.ProviderEmail,
            LinkedAt = now,
            LastUsedAt = now,
        };

        // DB báo trùng: Google này đã gắn với user khác, hoặc user này đã gắn một Google khác.
        var linked = await _externalLoginRepository.TryAddAsync(link, ct);
        if (!linked)
        {
            throw new GoogleAccountAlreadyLinkedException();
        }

        // 5. Mở phiên như đăng nhập thường.
        return await _sessionIssuer.OpenSessionAsync(user, request.RememberMe, ct);
    }

    /// <summary>
    /// Xử lý 3 trường hợp đăng nhập Google. Trả null khi không lưu được do request khác chen vào cùng lúc.
    /// </summary>
    private async Task<GoogleLoginResult?> TryLoginAsync(
        GoogleIdentity identity, bool rememberMe, string? returnUrl, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();

        // Trường hợp 1: Google sub đã liên kết. Tìm bằng sub, KHÔNG bằng email (email Google có thể đổi, AUTH-01).
        var existingLink = await _externalLoginRepository.FindBySubjectAsync(LoginProvider.Google, identity.Subject, ct);
        if (existingLink != null)
        {
            var linkedUser = existingLink.User;
            if (linkedUser.Status != UserStatus.Active)
            {
                throw new AccountNotActiveException();
            }

            existingLink.LastUsedAt = now;
            linkedUser.LastLoginAt = now;

            var auth = await _sessionIssuer.OpenSessionAsync(linkedUser, rememberMe, ct);
            return SignedIn(auth, returnUrl, accountCreated: false);
        }

        // Trường hợp 2: email trùng tài khoản app có sẵn → bắt nhập mật khẩu, không tự gộp (AUTH-02).
        var email = identity.Email.Trim().ToLowerInvariant();
        var userWithSameEmail = await _userRepository.FindByEmailAsync(email, ct);
        if (userWithSameEmail != null)
        {
            return await CreateLinkTicketAsync(userWithSameEmail, identity, email, returnUrl, ct);
        }

        // Trường hợp 3: người mới → tạo tài khoản không mật khẩu (AUTH-03) và liên kết Google luôn.
        var newUser = new User
        {
            Email = email,
            // Chỉ coi email đã xác minh khi chính Google xác nhận (AUTH-04).
            EmailVerifiedAt = identity.EmailVerified ? now : null,
            PasswordHash = null,
            // Tên Google chỉ để điền sẵn, không phải xác minh danh tính (AUTH-05).
            FullName = identity.FullName ?? string.Empty,
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = now,
            LastLoginAt = now,
        };

        var newLink = new ExternalLogin
        {
            UserId = newUser.Id,
            // Gắn User vào liên kết để user và liên kết được lưu trong CÙNG một lần SaveChanges:
            // không bao giờ có user Google "mồ côi" (không liên kết, không mật khẩu → không đăng nhập được).
            User = newUser,
            Provider = LoginProvider.Google,
            ProviderSubject = identity.Subject,
            ProviderEmail = email,
            LinkedAt = now,
            LastUsedAt = now,
        };

        var saved = await _externalLoginRepository.TryAddAsync(newLink, ct);
        if (!saved)
        {
            return null;
        }

        var newAuth = await _sessionIssuer.OpenSessionAsync(newUser, rememberMe, ct);
        return SignedIn(newAuth, returnUrl, accountCreated: true);
    }

    /// <summary>Tạo vé liên kết ngắn hạn (10 phút, dùng một lần); DB chỉ lưu hash của vé.</summary>
    private async Task<GoogleLoginResult> CreateLinkTicketAsync(
        User existingUser, GoogleIdentity identity, string email, string? returnUrl, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var ticket = _tokenService.CreateOpaqueToken();

        _linkTicketRepository.Add(new ExternalLoginLinkTicket
        {
            UserId = existingUser.Id,
            Provider = LoginProvider.Google,
            ProviderSubject = identity.Subject,
            ProviderEmail = email,
            TicketHash = ticket.Hash,
            CreatedAt = now,
            ExpiresAt = now.Add(LinkTicketLifetime),
        });
        await _unitOfWork.SaveChangesAsync(ct);

        return new GoogleLoginResult
        {
            SignedIn = false,
            LinkTicket = ticket.Value,
            MaskedEmail = EmailMasker.Mask(existingUser.Email),
            ReturnUrl = returnUrl,
        };
    }

    private static GoogleLoginResult SignedIn(AuthResult auth, string? returnUrl, bool accountCreated)
    {
        return new GoogleLoginResult
        {
            SignedIn = true,
            Auth = auth,
            ReturnUrl = returnUrl,
            AccountCreated = accountCreated,
        };
    }
}

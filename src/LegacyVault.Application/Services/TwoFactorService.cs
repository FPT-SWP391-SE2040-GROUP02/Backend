using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Xác thực hai bước TOTP cho Admin (xem ITwoFactorService).
/// </summary>
/// <remarks>
/// - Bí mật TOTP lưu đã bọc bằng KEK; mã khôi phục và token thử thách chỉ lưu hash.
/// - Mỗi mã TOTP chỉ dùng một lần (LastUsedStep); thử thách sống 5 phút, sai 5 lần thì bị hủy.
/// - Người dùng lấy từ ICurrentUser, không nhận userId từ FE.
/// </remarks>
public sealed class TwoFactorService : ITwoFactorService
{
    private const string Issuer = "LegacyVault";
    private const int RecoveryCodeCount = 8;
    private const int RecoveryCodeLength = 12;
    private const int MaxChallengeFailures = 5;
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorRepository _twoFactorRepository;
    private readonly ITotpSecretProtector _secretProtector;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LoginSessionIssuer _sessionIssuer;
    private readonly TimeProvider _timeProvider;

    public TwoFactorService(
        ICurrentUser currentUser,
        IUserRepository userRepository,
        ITwoFactorRepository twoFactorRepository,
        ITotpSecretProtector secretProtector,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        LoginSessionIssuer sessionIssuer,
        TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _userRepository = userRepository;
        _twoFactorRepository = twoFactorRepository;
        _secretProtector = secretProtector;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _sessionIssuer = sessionIssuer;
        _timeProvider = timeProvider;
    }

    public async Task<TwoFactorStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        User user = await LoadCurrentAdminAsync(ct);
        UserTotp? totp = await _twoFactorRepository.FindTotpAsync(user.Id, ct);

        TwoFactorStatusResponse status = new TwoFactorStatusResponse();
        if (totp != null && totp.EnabledAt != null)
        {
            status.Enabled = true;
            List<TwoFactorRecoveryCode> unused = await _twoFactorRepository.FindUnusedRecoveryCodesAsync(user.Id, ct);
            status.RecoveryCodesRemaining = unused.Count;
        }
        else if (totp != null)
        {
            status.SetupPending = true;
        }
        return status;
    }

    public Task<TwoFactorSetupResponse> SetupAsync(CancellationToken ct)
    {
        return _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentAdminAsync(ct);
            UserTotp? totp = await _twoFactorRepository.FindTotpAsync(user.Id, ct);
            if (totp != null && totp.EnabledAt != null)
            {
                throw TwoFactorException.AlreadyEnabled();
            }

            byte[] secret = Totp.NewSecret();
            if (totp == null)
            {
                totp = new UserTotp { UserId = user.Id, CreatedAt = _timeProvider.GetUtcNow() };
                _twoFactorRepository.AddTotp(totp);
            }
            // Setup lại khi chưa xác nhận: thay khóa cũ bằng khóa mới.
            _secretProtector.Protect(totp, secret);
            totp.LastUsedStep = 0;
            await _unitOfWork.SaveChangesAsync(ct);

            TwoFactorSetupResponse response = new TwoFactorSetupResponse();
            response.Secret = Totp.ToBase32(secret);
            response.OtpAuthUri = BuildOtpAuthUri(user.Email, response.Secret);
            CryptographicOperations.ZeroMemory(secret);
            return response;
        }, ct);
    }

    public Task<TwoFactorEnableResponse> EnableAsync(TwoFactorCodeRequest request, CancellationToken ct)
    {
        return _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentAdminAsync(ct);
            UserTotp? totp = await _twoFactorRepository.FindTotpAsync(user.Id, ct);
            if (totp == null)
            {
                throw TwoFactorException.SetupRequired();
            }
            if (totp.EnabledAt != null)
            {
                throw TwoFactorException.AlreadyEnabled();
            }

            long? step = VerifyTotp(totp, request.Code);
            if (step == null)
            {
                throw TwoFactorException.InvalidCode();
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            totp.EnabledAt = now;
            totp.LastUsedStep = step.Value;
            user.UpdatedAt = now;

            TwoFactorEnableResponse response = new TwoFactorEnableResponse();
            await ReplaceRecoveryCodesAsync(user.Id, response.RecoveryCodes, now, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return response;
        }, ct);
    }

    public async Task DisableAsync(TwoFactorCodeRequest request, CancellationToken ct)
    {
        await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            User user = await LoadCurrentAdminAsync(ct);
            UserTotp? totp = await _twoFactorRepository.FindTotpAsync(user.Id, ct);
            if (totp == null || totp.EnabledAt == null)
            {
                throw TwoFactorException.NotEnabled();
            }

            bool accepted = await AcceptCodeAsync(user.Id, totp, request.Code, ct);
            if (!accepted)
            {
                throw TwoFactorException.InvalidCode();
            }

            List<TwoFactorRecoveryCode> all = await _twoFactorRepository.FindAllRecoveryCodesAsync(user.Id, ct);
            foreach (TwoFactorRecoveryCode code in all)
            {
                _twoFactorRepository.RemoveRecoveryCode(code);
            }
            _twoFactorRepository.RemoveTotp(totp);
            user.UpdatedAt = _timeProvider.GetUtcNow();
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<TwoFactorChallengeResponse?> ChallengeIfRequiredAsync(User user, bool rememberMe, CancellationToken ct)
    {
        if (user.Role != UserRole.Admin)
        {
            return null;
        }
        UserTotp? totp = await _twoFactorRepository.FindTotpAsync(user.Id, ct);
        if (totp == null || totp.EnabledAt == null)
        {
            return null;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        OpaqueToken token = _tokenService.CreateOpaqueToken();
        _twoFactorRepository.AddChallenge(new TwoFactorChallenge
        {
            UserId = user.Id,
            TokenHash = token.Hash,
            RememberMe = rememberMe,
            CreatedAt = now,
            ExpiresAt = now.Add(ChallengeLifetime),
        });
        await _unitOfWork.SaveChangesAsync(ct);

        TwoFactorChallengeResponse response = new TwoFactorChallengeResponse();
        response.ChallengeToken = token.Value;
        response.ExpiresIn = (int)ChallengeLifetime.TotalSeconds;
        return response;
    }

    public async Task<AuthResult> VerifyLoginAsync(TwoFactorVerifyRequest request, CancellationToken ct)
    {
        string tokenHash = _tokenService.HashOpaqueToken(request.ChallengeToken);

        // Sai mã phải được ghi nhận (đếm lần sai) rồi mới báo lỗi, nên lỗi được trả ra ngoài transaction.
        (AuthResult? Result, ApiException? Failure) outcome = await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            try
            {
                AuthResult result = await VerifyCoreAsync(tokenHash, request.Code, ct);
                return (Result: (AuthResult?)result, Failure: (ApiException?)null);
            }
            catch (TwoFactorException failure)
            {
                return (Result: (AuthResult?)null, Failure: (ApiException?)failure);
            }
        }, ct);

        if (outcome.Failure != null)
        {
            throw outcome.Failure;
        }
        return outcome.Result!;
    }

    private async Task<AuthResult> VerifyCoreAsync(string tokenHash, string code, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TwoFactorChallenge? challenge = await _twoFactorRepository.FindChallengeByHashAsync(tokenHash, ct);
        if (challenge == null || challenge.UsedAt != null || challenge.ExpiresAt <= now)
        {
            throw TwoFactorException.InvalidChallenge();
        }

        User user = challenge.User;
        UserTotp? totp = await _twoFactorRepository.FindTotpAsync(user.Id, ct);
        if (user.Status != UserStatus.Active || user.Role != UserRole.Admin || totp == null || totp.EnabledAt == null)
        {
            throw TwoFactorException.InvalidChallenge();
        }

        bool accepted = await AcceptCodeAsync(user.Id, totp, code, ct);
        if (!accepted)
        {
            challenge.FailedAttempts = challenge.FailedAttempts + 1;
            if (challenge.FailedAttempts >= MaxChallengeFailures)
            {
                challenge.UsedAt = now;
            }
            await _unitOfWork.SaveChangesAsync(ct);
            throw TwoFactorException.InvalidCode();
        }

        challenge.UsedAt = now;
        user.LastLoginAt = now;
        return await _sessionIssuer.OpenSessionAsync(user, challenge.RememberMe, ct);
    }

    /// <summary>Nhận mã TOTP 6 số hoặc mã khôi phục. Mã đúng được đánh dấu đã dùng (chưa SaveChanges).</summary>
    private async Task<bool> AcceptCodeAsync(Guid userId, UserTotp totp, string rawCode, CancellationToken ct)
    {
        string code = rawCode.Trim();
        bool looksLikeTotp = code.Length == Totp.Digits && code.All(char.IsAsciiDigit);
        if (looksLikeTotp)
        {
            long? step = VerifyTotp(totp, code);
            if (step == null)
            {
                return false;
            }
            totp.LastUsedStep = step.Value;
            return true;
        }

        string normalized = NormalizeRecoveryCode(code);
        if (normalized.Length != RecoveryCodeLength)
        {
            return false;
        }
        string hash = RecoveryHash(userId, normalized);
        List<TwoFactorRecoveryCode> unused = await _twoFactorRepository.FindUnusedRecoveryCodesAsync(userId, ct);
        foreach (TwoFactorRecoveryCode candidate in unused)
        {
            if (candidate.CodeHash == hash)
            {
                candidate.UsedAt = _timeProvider.GetUtcNow();
                return true;
            }
        }
        return false;
    }

    private long? VerifyTotp(UserTotp totp, string code)
    {
        byte[] secret = _secretProtector.Unprotect(totp);
        try
        {
            return Totp.Verify(secret, code.Trim(), _timeProvider.GetUtcNow(), totp.LastUsedStep);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private async Task ReplaceRecoveryCodesAsync(Guid userId, List<string> output, DateTimeOffset now, CancellationToken ct)
    {
        List<TwoFactorRecoveryCode> old = await _twoFactorRepository.FindAllRecoveryCodesAsync(userId, ct);
        foreach (TwoFactorRecoveryCode code in old)
        {
            _twoFactorRepository.RemoveRecoveryCode(code);
        }

        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            string plain = NewRecoveryCode();
            output.Add(plain.Substring(0, 6) + "-" + plain.Substring(6));
            _twoFactorRepository.AddRecoveryCode(new TwoFactorRecoveryCode
            {
                UserId = userId,
                CodeHash = RecoveryHash(userId, plain),
                CreatedAt = now,
            });
        }
    }

    /// <summary>Hash mã khôi phục kèm userId để hai tài khoản trùng mã vẫn ra hash khác nhau.</summary>
    private string RecoveryHash(Guid userId, string normalizedCode)
    {
        return _tokenService.HashOpaqueToken(userId.ToString("N") + ":" + normalizedCode);
    }

    private static string NewRecoveryCode()
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < RecoveryCodeLength; i++)
        {
            int index = RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length);
            builder.Append(RecoveryAlphabet[index]);
        }
        return builder.ToString();
    }

    private static string NormalizeRecoveryCode(string code)
    {
        return code.Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
    }

    private async Task<User> LoadCurrentAdminAsync(CancellationToken ct)
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
        if (user.Role != UserRole.Admin)
        {
            throw TwoFactorException.AdminOnly();
        }
        return user;
    }

    private static string BuildOtpAuthUri(string email, string base32Secret)
    {
        string label = Uri.EscapeDataString(Issuer) + ":" + Uri.EscapeDataString(email);
        string query = "secret=" + base32Secret
            + "&issuer=" + Uri.EscapeDataString(Issuer)
            + "&algorithm=SHA1&digits=" + Totp.Digits.ToString(CultureInfo.InvariantCulture)
            + "&period=" + Totp.StepSeconds.ToString(CultureInfo.InvariantCulture);
        return "otpauth://totp/" + label + "?" + query;
    }
}

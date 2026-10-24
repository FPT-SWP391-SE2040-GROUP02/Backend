using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class RegistrationService : IRegistrationService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailVerificationTokenRepository _tokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly IAppUrlProvider _appUrlProvider;
    private readonly TimeProvider _timeProvider;

    public RegistrationService(
        IUserRepository userRepository,
        IEmailVerificationTokenRepository tokenRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IEmailSender emailSender,
        IAppUrlProvider appUrlProvider,
        TimeProvider timeProvider)
    {
        _userRepository = userRepository;
        _tokenRepository = tokenRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _emailSender = emailSender;
        _appUrlProvider = appUrlProvider;
        _timeProvider = timeProvider;
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var fullName = request.FullName.Trim();
        var now = _timeProvider.GetUtcNow();

        // 1. Kiểm tra tài khoản đã tồn tại chưa
        var existing = await _userRepository.FindByEmailAsync(email, ct);
        if (existing != null)
        {
            throw new AppException(
                AppErrorType.Conflict,
                ErrorCode.EmailAlreadyExists,
                "Địa chỉ email này đã được sử dụng. Vui lòng đăng nhập hoặc dùng tính năng Quên mật khẩu.");
        }

        // 2. Tạo tài khoản User mới: Role = User (ROLE-01), Status = Active, EmailVerifiedAt = null
        var user = new User
        {
            Email = email,
            FullName = fullName,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = UserRole.User,
            Status = UserStatus.Active,
            EmailVerifiedAt = null,
            CreatedAt = now,
        };

        if (!await _userRepository.TryAddAsync(user, ct))
        {
            // Race condition: request khác vừa thêm cùng email
            return;
        }

        // 3. Sinh token xác minh và lưu hash
        var token = _tokenService.CreateOpaqueToken();
        var verificationToken = new EmailVerificationToken
        {
            UserId = user.Id,
            TokenHash = token.Hash,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            UsedAt = null,
        };

        _tokenRepository.Add(verificationToken);
        await _unitOfWork.SaveChangesAsync(ct);

        // 4. Gửi email xác minh
        var verificationUrl = _appUrlProvider.GetEmailVerificationUrl(token.Value);
        await _emailSender.SendEmailVerificationAsync(user.Email, user.FullName, verificationUrl, ct);
    }

    public async Task<UserResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var hash = _tokenService.HashOpaqueToken(request.Token);

        var tokenEntity = await _tokenRepository.FindByHashAsync(hash, ct);
        if (tokenEntity == null || tokenEntity.ExpiresAt <= now)
        {
            throw new InvalidVerificationTokenException();
        }

        var user = tokenEntity.User;

        // Nếu token đã được sử dụng nhưng tài khoản đã được xác minh email thành công:
        // Trả về UserResponse hợp lệ thay vì báo lỗi link hỏng (idempotent verification)
        if (tokenEntity.UsedAt != null)
        {
            if (user.EmailVerifiedAt != null)
            {
                return MapUser(user);
            }
            throw new InvalidVerificationTokenException();
        }

        tokenEntity.UsedAt = now;

        if (user.EmailVerifiedAt == null)
        {
            user.EmailVerifiedAt = now;
            user.UpdatedAt = now;
        }

        // Vô hiệu hóa dọn dẹp các token chưa dùng còn lại của user này
        var unused = await _tokenRepository.FindUnusedByUserAsync(user.Id, ct);
        foreach (var t in unused)
        {
            t.UsedAt = now;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return MapUser(user);
    }

    public async Task ResendVerificationAsync(ResendVerificationRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var now = _timeProvider.GetUtcNow();

        var user = await _userRepository.FindByEmailAsync(email, ct);
        if (user == null || user.EmailVerifiedAt != null || user.Status != UserStatus.Active)
        {
            // Luôn trả thành công để không lộ email đã đăng ký
            return;
        }

        // Không vội xóa các token chưa dùng, để người dùng bấm link nào trong hộp thư cũng hợp lệ
        var token = _tokenService.CreateOpaqueToken();
        var verificationToken = new EmailVerificationToken
        {
            UserId = user.Id,
            TokenHash = token.Hash,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            UsedAt = null,
        };

        _tokenRepository.Add(verificationToken);
        await _unitOfWork.SaveChangesAsync(ct);

        var verifyUrl = _appUrlProvider.GetEmailVerificationUrl(token.Value);
        await _emailSender.SendEmailVerificationAsync(user.Email, user.FullName, verifyUrl, ct);
    }

    private static UserResponse MapUser(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            Role = user.Role.ToString().ToUpperInvariant(),
            Status = user.Status.ToString().ToUpperInvariant(),
            EmailVerified = user.EmailVerifiedAt != null,
            HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
            HasAvatar = user.AvatarStorageKey != null,
            AvatarUpdatedAt = user.AvatarUpdatedAt,
            CreatedAt = user.CreatedAt,
        };
    }
}

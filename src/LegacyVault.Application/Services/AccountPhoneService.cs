using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Số điện thoại của người đang đăng nhập (xem IAccountPhoneService).
/// </summary>
/// <remarks>
/// Tài khoản đăng ký bằng email nhập số ngay lúc đăng ký; tài khoản tạo bằng Google chưa có số nên Frontend
/// đưa người dùng qua màn nhập số trước khi vào trang chính. Số này được chia sẻ cho người nhận khi kế hoạch được kích hoạt.
/// </remarks>
public sealed class AccountPhoneService(
    ICurrentUser currentUser,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAccountPhoneService
{
    public async Task<UserResponse> SetMyPhoneAsync(SetPhoneRequest request, CancellationToken ct)
    {
        string phone = PhoneRules.NormalizeContactPhone(request.Phone, "phone");

        Guid? userId = currentUser.UserId;
        if (userId == null)
        {
            throw new SessionExpiredException();
        }
        User? user = await userRepository.FindByIdAsync(userId.Value, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new SessionExpiredException();
        }

        if (user.Phone != phone)
        {
            // Mỗi số chỉ thuộc một tài khoản, mọi vai trò.
            if (await userRepository.PhoneExistsAsync(phone, user.Id, ct))
            {
                throw PhoneRules.AlreadyUsed("phone");
            }
            user.Phone = phone;
            user.UpdatedAt = timeProvider.GetUtcNow();
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (userRepository.IsUniqueViolation(ex))
            {
                // Request khác vừa lấy đúng số này giữa lúc kiểm tra và lúc lưu.
                throw PhoneRules.AlreadyUsed("phone");
            }
        }
        return LoginSessionIssuer.ToUserResponse(user);
    }
}

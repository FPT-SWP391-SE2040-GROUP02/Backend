using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Triển khai dịch vụ tra cứu danh bạ Người thực thi hệ thống.
/// </summary>
public sealed class SystemExecutorDirectoryService : ISystemExecutorDirectoryService
{
    private readonly IUserRepository userRepository;
    private readonly IVaultRepository vaultRepository;
    private readonly ICurrentUser currentUser;

    public SystemExecutorDirectoryService(IUserRepository userRepository, IVaultRepository vaultRepository, ICurrentUser currentUser)
    {
        this.userRepository = userRepository;
        this.vaultRepository = vaultRepository;
        this.currentUser = currentUser;
    }

    public async Task<List<SystemExecutorResponse>> ListAvailableExecutorsAsync(CancellationToken ct)
    {
        // Danh bạ có email: chỉ Chủ tài sản (người có kho) được xem, vai trò tài khoản không đủ để phân biệt.
        if (await vaultRepository.FindByOwnerIdAsync(currentUser.RequireUserId(), ct) == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.VaultNotFound, "Không tìm thấy kho.");
        }

        List<User> users = await userRepository.ListByRoleAsync(UserRole.Executor, UserStatus.Active, ct);
        List<SystemExecutorResponse> result = new(users.Count);

        foreach (User user in users)
        {
            // Khách cần gọi được cho Người thực thi nên danh bạ trả đủ số điện thoại.
            string? phone = ReadPhone(user.Phone);
            result.Add(new SystemExecutorResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = phone,
                AvatarUrl = null,
                ExecutorRoleTitle = user.ExecutorRoleTitle ?? string.Empty,
                AssignedVaultsCount = 0,
                IsAvailable = true
            });
        }

        return result;
    }

    private static string? ReadPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }
        return phone.Trim();
    }
}

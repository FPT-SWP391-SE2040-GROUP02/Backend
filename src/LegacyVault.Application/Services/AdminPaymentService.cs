using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Màn đối chiếu thanh toán của Admin (SRS ADMIN-09, PAY-07). Chỉ đọc.</summary>
public interface IAdminPaymentService
{
    /// <summary>
    /// Danh sách giao dịch tiền vào, mới nhất trước. status lọc theo RECEIVED, MATCHED, RECONCILIATION_REQUIRED, RESOLVED.
    /// Không phải Admin đang hoạt động → 404; status lạ → 400.
    /// </summary>
    Task<PaginatedList<AdminPaymentTransactionResponse>> ListAsync(
        string? status, int pageIndex, int pageSize, CancellationToken ct);
}

/// <remarks>
/// - PAY-07: Admin chỉ điều phối, không có thao tác nào ghi trạng thái PAID hay sửa giao dịch ở đây.
/// - Kiểm Admin giống AdminService: không phải Admin đang hoạt động thì 404 như khu vực không tồn tại (SEC-04).
/// - Không trả nội dung chuyển khoản, số tài khoản hay key (ADMIN-09, AC-75).
/// </remarks>
public sealed class AdminPaymentService : IAdminPaymentService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IPaymentTransactionRepository transactionRepository;

    public AdminPaymentService(
        ICurrentUser currentUser,
        IUserRepository users,
        IPaymentTransactionRepository transactionRepository)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.transactionRepository = transactionRepository;
    }

    public async Task<PaginatedList<AdminPaymentTransactionResponse>> ListAsync(
        string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        PaymentTransactionStatus? statusFilter = ParseStatus(status);

        (List<PaymentTransaction> items, int total) = await transactionRepository.ListAsync(
            statusFilter, pageIndex, pageSize, ct);

        PaginatedList<AdminPaymentTransactionResponse> page = new PaginatedList<AdminPaymentTransactionResponse>();
        foreach (PaymentTransaction transaction in items)
        {
            page.Items.Add(transaction.ToAdminResponse());
        }
        page.TotalCount = total;
        page.PageIndex = pageIndex;
        page.PageSize = pageSize;
        page.TotalPages = (total + pageSize - 1) / pageSize;
        page.HasPreviousPage = pageIndex > 1;
        page.HasNextPage = pageIndex < page.TotalPages;
        return page;
    }

    private async Task RequireAdminAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Role != UserRole.Admin || user.Status != UserStatus.Active)
        {
            // Không lộ sự tồn tại của khu vực Admin (SEC-04), cùng mã lỗi với AdminService.
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
    }

    private static PaymentTransactionStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }
        string wanted = status.Trim();
        foreach (PaymentTransactionStatus candidate in Enum.GetValues<PaymentTransactionStatus>())
        {
            if (string.Equals(candidate.ToApiString(), wanted, StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        throw ValidationErrors.For("status", "Giá trị lọc không hợp lệ.");
    }
}

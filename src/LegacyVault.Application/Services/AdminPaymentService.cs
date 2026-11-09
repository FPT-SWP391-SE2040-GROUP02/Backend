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
        string? status, string? search, int pageIndex, int pageSize, CancellationToken ct,
        string? environment = null, string? from = null, string? to = null);
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
        string? status, string? search, int pageIndex, int pageSize, CancellationToken ct,
        string? environment = null, string? from = null, string? to = null)
    {
        await RequireAdminAsync(ct);
        PaymentTransactionFilter filter = new PaymentTransactionFilter();
        // "SUCCESS" gộp Đã khớp đơn và Đã xử lý thành một nhóm "Thành công".
        if (string.Equals(status?.Trim(), "SUCCESS", StringComparison.Ordinal))
        {
            filter.SuccessOnly = true;
            status = null;
        }
        PaymentTransactionStatus? statusFilter = ParseStatus(status);
        filter.Environment = ParseEnvironment(environment);
        filter.From = ParseDayStart(from, "from");
        DateTimeOffset? lastDay = ParseDayStart(to, "to");
        filter.ToExclusive = lastDay?.AddDays(1);
        if (filter.From != null && lastDay != null && filter.From > lastDay)
        {
            throw ValidationErrors.For("from", "Ngày bắt đầu phải trước hoặc cùng ngày kết thúc.");
        }

        (List<PaymentTransaction> items, int total) = await transactionRepository.ListAsync(
            statusFilter, search, pageIndex, pageSize, ct, filter);

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

    private static string? ParseEnvironment(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return null;
        }
        string wanted = environment.Trim();
        if (wanted == "Live" || wanted == "Test")
        {
            return wanted;
        }
        throw ValidationErrors.For("environment", "Giá trị lọc không hợp lệ.");
    }

    /// <summary>Ngày yyyy-MM-dd tính theo giờ Việt Nam (UTC+7); trả 00:00 của ngày đó.</summary>
    private static DateTimeOffset? ParseDayStart(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateOnly day))
        {
            throw ValidationErrors.For(field, "Ngày không hợp lệ, cần dạng yyyy-MM-dd.");
        }
        return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
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

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

    /// <summary>
    /// Doanh thu = tổng tiền giao dịch thành công (Matched và Resolved), gom theo groupBy (day, month, quarter; mặc định month)
    /// theo giờ Việt Nam. environment: Live (mặc định, tiền thật), Test hoặc ALL. from, to: yyyy-MM-dd, tính cả hai đầu.
    /// Không phải Admin đang hoạt động → 404; tham số lạ → 400.
    /// </summary>
    Task<AdminRevenueResponse> GetRevenueAsync(
        string? groupBy, string? environment, string? from, string? to, CancellationToken ct);
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

    public async Task<AdminRevenueResponse> GetRevenueAsync(
        string? groupBy, string? environment, string? from, string? to, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        string group = ParseGroupBy(groupBy);

        // Mặc định chỉ tính tiền thật; "ALL" mới gộp cả môi trường thử nghiệm.
        string revenueEnvironment = "Live";
        PaymentTransactionFilter filter = new PaymentTransactionFilter();
        if (!string.IsNullOrWhiteSpace(environment))
        {
            string wanted = environment.Trim();
            if (string.Equals(wanted, "ALL", StringComparison.Ordinal))
            {
                revenueEnvironment = "ALL";
            }
            else
            {
                revenueEnvironment = ParseEnvironment(wanted) ?? "Live";
            }
        }
        if (revenueEnvironment != "ALL")
        {
            filter.Environment = revenueEnvironment;
        }
        filter.From = ParseDayStart(from, "from");
        DateTimeOffset? lastDay = ParseDayStart(to, "to");
        filter.ToExclusive = lastDay?.AddDays(1);
        if (filter.From != null && lastDay != null && filter.From > lastDay)
        {
            throw ValidationErrors.For("from", "Ngày bắt đầu phải trước hoặc cùng ngày kết thúc.");
        }

        List<RevenueTransaction> rows = await transactionRepository.ListSuccessAmountsAsync(filter, ct);

        AdminRevenueResponse response = new AdminRevenueResponse();
        response.GroupBy = group;
        response.Environment = revenueEnvironment;
        // Danh sách đã sắp theo thời điểm nhận tăng dần nên kỳ nào đến trước cũng nằm trước.
        Dictionary<string, AdminRevenueBucketResponse> byKey = new Dictionary<string, AdminRevenueBucketResponse>();
        foreach (RevenueTransaction row in rows)
        {
            DateTimeOffset vietnamTime = row.ReceivedAt.ToOffset(TimeSpan.FromHours(7));
            string key = BucketKey(group, vietnamTime);
            if (!byKey.TryGetValue(key, out AdminRevenueBucketResponse? bucket))
            {
                bucket = new AdminRevenueBucketResponse();
                bucket.Key = key;
                bucket.Label = BucketLabel(group, vietnamTime);
                byKey[key] = bucket;
                response.Buckets.Add(bucket);
            }
            bucket.Amount += row.Amount;
            bucket.Count += 1;
            response.TotalAmount += row.Amount;
            response.TransactionCount += 1;
        }
        return response;
    }

    private static string ParseGroupBy(string? groupBy)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
        {
            return "month";
        }
        string wanted = groupBy.Trim();
        if (wanted == "day" || wanted == "month" || wanted == "quarter")
        {
            return wanted;
        }
        throw ValidationErrors.For("groupBy", "Giá trị gom nhóm không hợp lệ, cần day, month hoặc quarter.");
    }

    private static int QuarterOf(DateTimeOffset vietnamTime)
    {
        return (vietnamTime.Month - 1) / 3 + 1;
    }

    private static string BucketKey(string group, DateTimeOffset vietnamTime)
    {
        if (group == "day")
        {
            return vietnamTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (group == "quarter")
        {
            return vietnamTime.Year + "-Q" + QuarterOf(vietnamTime);
        }
        return vietnamTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string BucketLabel(string group, DateTimeOffset vietnamTime)
    {
        if (group == "day")
        {
            return vietnamTime.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (group == "quarter")
        {
            return "Quý " + QuarterOf(vietnamTime) + "/" + vietnamTime.Year;
        }
        return vietnamTime.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
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

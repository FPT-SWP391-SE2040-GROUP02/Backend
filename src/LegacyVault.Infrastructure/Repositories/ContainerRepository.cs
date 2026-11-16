// Nạp giao diện kho lưu trữ, các thực thể miền, kiểu liệt kê và công cụ truy vấn Entity Framework Core.
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Không gian tên chứa các lớp cài đặt thao tác lưu trữ và truy xuất cơ sở dữ liệu ở tầng hạ tầng.
namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Chỉ là truy cập dữ liệu theo IContainerRepository. Luật nghiệp vụ gói (ContainerService) do module gói cài đặt.
/// VaultService dùng CountActiveByVaultIdAsync cho VaultResponse.ActiveContainerCount.
/// </remarks>
// Lớp cài đặt các thao tác truy xuất dữ liệu gói bàn giao từ cơ sở dữ liệu qua ngữ cảnh Entity Framework.
public sealed class ContainerRepository(AppDbContext context) : IContainerRepository
{
    // Tìm kiếm một gói bàn giao theo mã kho và mã gói, nạp kèm tài sản và các chỉ định người nhận.
    public Task<Container?> FindByIdAsync(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        // Truy vấn bảng gói bàn giao kèm nạp danh sách tài sản và người nhận sắp xếp theo thời điểm tạo.
        return context.Containers
            // Nạp tập hợp danh sách các tài sản được gắn vào gói bàn giao.
            .Include(c => c.Assets)
            // Nạp tập hợp người nhận của gói và sắp xếp theo thời điểm gán tăng dần.
            .Include(c => c.Designations.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id))
            // Tìm bản ghi đầu tiên khớp với mã gói và mã kho sở hữu.
            .FirstOrDefaultAsync(c => c.Id == containerId && c.VaultId == vaultId, ct);
    }

    // SETUP-01: chỉ các gói chưa bị xóa xuất hiện trên màn kích hoạt.
    // Truy vấn tất cả các gói bàn giao đang hoạt động trong kho để phục vụ lập kế hoạch kích hoạt.
    public Task<List<Container>> FindOpenByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        // Truy vấn cơ sở dữ liệu không theo dõi thay đổi để tối ưu hiệu năng và nạp đầy đủ thông tin kèm theo.
        return context.Containers
            // Bật chế độ không theo dõi thay đổi thực thể giúp truy vấn nhanh hơn và tiết kiệm bộ nhớ.
            .AsNoTracking()
            // Lọc các gói thuộc kho chỉ định và loại bỏ các gói đã bị đánh dấu xóa.
            .Where(c => c.VaultId == vaultId && c.Status != ContainerStatus.Removed)
            // Nạp thông tin danh sách các tài sản được liên kết trong gói.
            .Include(c => c.Assets)
            // Nạp thông tin danh sách các chỉ định người thụ hưởng của gói.
            .Include(c => c.Designations)
            // Tách thành nhiều truy vấn con để tránh nhân bản dữ liệu khi nạp nhiều tập hợp liên kết.
            .AsSplitQuery()
            // Sắp xếp các gói theo thời điểm tạo tăng dần để giữ thứ tự trước sau nhất quán.
            .OrderBy(c => c.CreatedAt)
            // Nếu trùng thời điểm tạo thì dùng mã định danh để bảo đảm thứ tự duy nhất tuyệt đối.
            .ThenBy(c => c.Id)
            // Thực thi truy vấn bất đồng bộ và trả về danh sách kết quả.
            .ToListAsync(ct);
    }

    // Truy vấn danh sách gói bàn giao theo phân trang, tìm kiếm từ khóa và sắp xếp linh hoạt.
    public async Task<(List<Container> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, bool includeRemoved, int pageIndex, int pageSize, string? searchTerm,
        string sortBy, bool isAscending, CancellationToken ct)
    {
        // Khởi tạo câu truy vấn gốc chỉ đọc lọc theo kho để chuẩn bị ghép các điều kiện lọc tiếp theo.
        var query = context.Containers.AsNoTracking().Where(c => c.VaultId == vaultId);
        // Kiểm tra xem yêu cầu có muốn bao gồm cả các gói đã xóa hay không.
        if (!includeRemoved)
        {
            // Bỏ qua các gói đã bị đánh dấu xóa khỏi kết quả tìm kiếm.
            query = query.Where(c => c.Status != ContainerStatus.Removed);
        }
        // Kiểm tra nếu người dùng có nhập từ khóa tìm kiếm theo tên gói.
        if (!string.IsNullOrEmpty(searchTerm))
        {
            // Lọc các gói có tên chứa từ khóa tìm kiếm được cung cấp.
            query = query.Where(c => c.Name.Contains(searchTerm));
        }

        // Đếm tổng số bản ghi thỏa mãn điều kiện lọc để phục vụ tính toán số trang hiển thị.
        var totalCount = await query.CountAsync(ct);

        // ThenBy(Id): khi trùng tên/thời điểm tạo vẫn có thứ tự cố định, chuyển trang không lặp/sót.
        // Kiểm tra tiêu chí sắp xếp yêu cầu theo tên gói hay theo thời gian tạo.
        if (sortBy == "name")
        {
            // Kiểm tra chiều sắp xếp là tăng dần hay giảm dần.
            if (isAscending)
            {
                // Sắp xếp tên theo thứ tự bảng chữ cái tăng dần và dùng mã ID làm tiêu chí phụ.
                query = query.OrderBy(c => c.Name).ThenBy(c => c.Id);
            }
            else
            {
                // Sắp xếp tên theo thứ tự giảm dần và dùng mã ID làm tiêu chí phụ.
                query = query.OrderByDescending(c => c.Name).ThenByDescending(c => c.Id);
            }
        }
        else
        {
            // Mặc định: sắp theo thời điểm tạo (service đã chặn các giá trị sortBy khác).
            // Kiểm tra chiều sắp xếp thời gian tạo là tăng dần hay giảm dần.
            if (isAscending)
            {
                // Sắp xếp từ gói cũ nhất đến mới nhất theo mốc thời gian tạo.
                query = query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);
            }
            else
            {
                // Sắp xếp từ gói mới nhất đến cũ nhất theo mốc thời gian tạo.
                query = query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id);
            }
        }

        // Thực hiện phân trang, nạp các dữ liệu liên kết và lấy về danh sách các gói của trang hiện tại.
        var items = await query
            // Bỏ qua số lượng bản ghi của các trang trước đó.
            .Skip((pageIndex - 1) * pageSize)
            // Lấy đúng số lượng bản ghi cần hiển thị cho trang hiện tại.
            .Take(pageSize)
            // Nạp kèm danh sách tài sản thuộc từng gói trong kết quả.
            .Include(c => c.Assets)
            // Nạp kèm danh sách người nhận sắp xếp theo thứ tự thời gian gắn.
            .Include(c => c.Designations.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id))
            // Tách các tập hợp liên quan thành các câu truy vấn riêng biệt để tối ưu hiệu năng đọc.
            .AsSplitQuery()
            // Thực thi truy vấn danh sách bản ghi trang hiện tại bất đồng bộ.
            .ToListAsync(ct);
        // Trả về bộ dữ liệu gồm danh sách các gói của trang và tổng số lượng bản ghi.
        return (items, totalCount);
    }

    // Đếm tổng số lượng gói bàn giao đang còn hoạt động trong một kho cụ thể.
    public Task<int> CountActiveByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        // Truy vấn đếm số bản ghi thuộc kho mà trạng thái khác trạng thái đã xóa.
        return context.Containers.CountAsync(
            c => c.VaultId == vaultId && c.Status != ContainerStatus.Removed, ct);
    }

    // Đếm số lượng gói bàn giao đã sẵn sàng để chuyển giao trong kho.
    public Task<int> CountReadyByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        // Đếm các bản ghi thuộc kho có trạng thái là sẵn sàng.
        return context.Containers.CountAsync(
            c => c.VaultId == vaultId && c.Status == ContainerStatus.Ready, ct);
    }

    // Kiểm tra xem một tài sản đã được liên kết vào gói bàn giao nào hay chưa.
    public Task<ContainerAsset?> FindLinkByAssetIdAsync(Guid assetId, CancellationToken ct)
    {
        // Tìm bản ghi liên kết đầu tiên khớp với mã tài sản được cung cấp.
        return context.ContainerAssets.FirstOrDefaultAsync(l => l.AssetId == assetId, ct);
    }

    // Đánh dấu thêm một thực thể gói bàn giao mới vào bộ theo dõi ngữ cảnh dữ liệu.
    public void Add(Container container)
    {
        // Đưa thực thể gói mới vào tập hợp để chuẩn bị ghi xuống cơ sở dữ liệu.
        context.Containers.Add(container);
    }

    // Cố gắng liên kết tài sản vào gói bàn giao và xử lý an toàn nếu xảy ra trùng lặp ràng buộc duy nhất.
    public async Task<bool> TryAddAssetLinkAsync(ContainerAsset link, CancellationToken ct)
    {
        // Thêm liên kết tài sản vào ngữ cảnh theo dõi thay đổi.
        context.ContainerAssets.Add(link);
        // Bắt đầu khối xử lý lưu dữ liệu có phòng ngừa ngoại lệ trùng khóa.
        try
        {
            // Lưu thay đổi xuống cơ sở dữ liệu và chờ hoàn thành.
            await context.SaveChangesAsync(ct);
            // Trả về true báo hiệu liên kết tài sản thành công.
            return true;
        }
        // Bắt lỗi vi phạm chỉ mục duy nhất nếu tài sản này đã thuộc gói khác hoặc đã được gán trước đó.
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            // Tách thực thể lỗi ra khỏi bộ theo dõi để tránh làm hỏng các thao tác lưu dữ liệu sau đó.
            DbExceptions.DetachAll(context, link);
            // Trả về false báo hiệu liên kết thất bại do bị trùng lặp.
            return false;
        }
    }

    // Đánh dấu xóa một liên kết tài sản khỏi gói bàn giao trong cơ sở dữ liệu.
    public void RemoveAssetLink(ContainerAsset link)
    {
        // Chuyển trạng thái của bản ghi liên kết tài sản sang trạng thái bị xóa.
        context.ContainerAssets.Remove(link);
    }

    // Cố gắng gán người nhận cho gói bàn giao và xử lý an toàn nếu vi phạm trùng lặp chỉ định.
    public async Task<bool> TryAddDesignationAsync(Designation designation, CancellationToken ct)
    {
        // Đưa thực thể chỉ định người nhận vào ngữ cảnh theo dõi dữ liệu.
        context.Designations.Add(designation);
        // Bắt đầu khối lưu dữ liệu có kiểm soát lỗi trùng lặp.
        try
        {
            // Ghi nhận bản ghi chỉ định xuống cơ sở dữ liệu.
            await context.SaveChangesAsync(ct);
            // Trả về true xác nhận gán người nhận vào gói thành công.
            return true;
        }
        // Bắt lỗi vi phạm ràng buộc duy nhất khi người nhận đã được chỉ định vào gói này từ trước.
        catch (DbUpdateException ex) when (DbExceptions.IsUniqueViolation(ex))
        {
            // Tách thực thể lỗi ra khỏi bộ theo dõi để giữ sạch ngữ cảnh dữ liệu.
            DbExceptions.DetachAll(context, designation);
            // Trả về false báo hiệu không thể thêm người nhận do đã tồn tại chỉ định này.
            return false;
        }
    }

    // Đánh dấu xóa bản ghi chỉ định người nhận khỏi gói bàn giao.
    public void RemoveDesignation(Designation designation)
    {
        // Chuyển trạng thái của bản ghi chỉ định sang trạng thái bị xóa trong ngữ cảnh dữ liệu.
        context.Designations.Remove(designation);
    }

    // Tính tổng số suất người nhận thực tế đã được sử dụng trên toàn bộ các gói trong kho.
    public Task<int> CountUsedBeneficiarySlotsAsync(Guid vaultId, CancellationToken ct)
    {
        // Truy vấn bảng chỉ định lọc theo kho, lấy danh sách mã người nhận, loại bỏ trùng lặp và đếm số lượng.
        return context.Designations
            // Lọc các chỉ định thuộc các gói nằm trong kho lưu trữ tương ứng.
            .Where(d => d.Container.VaultId == vaultId)
            // Chọn ra trường mã định danh người thụ hưởng để tính toán.
            .Select(d => d.BeneficiaryId)
            // Loại bỏ các mã người nhận trùng lặp nếu một người được chỉ định vào nhiều gói khác nhau.
            .Distinct()
            // Đếm tổng số lượng người nhận duy nhất đang được gán trong kho.
            .CountAsync(ct);
    }

    // Kiểm tra xem một người nhận có đang được chỉ định vào bất kỳ gói nào trong kho hay không.
    public Task<bool> IsBeneficiaryDesignatedAsync(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        // Kiểm tra sự tồn tại của bản ghi chỉ định khớp với mã người nhận và mã kho sở hữu.
        return context.Designations
            // Trả về true nếu tìm thấy ít nhất một chỉ định thỏa mãn điều kiện.
            .AnyAsync(d => d.BeneficiaryId == beneficiaryId && d.Container.VaultId == vaultId, ct);
    }

    // Gom nhóm danh sách các mã gói bàn giao theo từng mã người nhận để hỗ trợ kiểm tra liên kết.
    public async Task<Dictionary<Guid, List<Guid>>> FindContainerIdsByBeneficiaryIdsAsync(
        List<Guid> beneficiaryIds, CancellationToken ct)
    {
        // Khởi tạo từ điển lưu kết quả ánh xạ từ mã người nhận sang danh sách các mã gói tương ứng.
        Dictionary<Guid, List<Guid>> result = new Dictionary<Guid, List<Guid>>();
        // Kiểm tra nếu danh sách đầu vào rỗng thì trả về kết quả rỗng ngay để tránh truy vấn thừa.
        if (beneficiaryIds.Count == 0)
        {
            // Trả về từ điển rỗng khi không có mã người nhận nào được truyền vào.
            return result;
        }

        // Truy vấn các cặp mã người nhận và mã gói từ cơ sở dữ liệu theo danh sách mã cung cấp.
        var rows = await context.Designations
            // Sử dụng truy vấn chỉ đọc không theo dõi để tăng tốc độ truy xuất dữ liệu.
            .AsNoTracking()
            // Lọc các bản ghi chỉ định có mã người nhận nằm trong danh sách đầu vào.
            .Where(d => beneficiaryIds.Contains(d.BeneficiaryId))
            // Sắp xếp các chỉ định theo thời điểm tạo tăng dần.
            .OrderBy(d => d.CreatedAt)
            // Sắp xếp phụ theo mã định danh để đảm bảo thứ tự nhất quán.
            .ThenBy(d => d.Id)
            // Chỉ chọn hai trường cần thiết là mã người nhận và mã gói để giảm tải băng thông.
            .Select(d => new { d.BeneficiaryId, d.ContainerId })
            // Thực thi truy vấn và trả về danh sách đối tượng ẩn danh.
            .ToListAsync(ct);

        // Duyệt qua từng dòng kết quả để phân loại các mã gói theo từng người nhận.
        foreach (var row in rows)
        {
            // Khai báo biến chứa danh sách các mã gói của một người nhận.
            List<Guid>? containerIds;
            // Kiểm tra xem người nhận này đã có danh sách gói trong từ điển hay chưa.
            if (!result.TryGetValue(row.BeneficiaryId, out containerIds))
            {
                // Khởi tạo danh sách mới nếu người nhận lần đầu xuất hiện trong kết quả.
                containerIds = new List<Guid>();
                // Thêm danh sách vừa tạo vào từ điển ứng với mã người nhận.
                result[row.BeneficiaryId] = containerIds;
            }
            // Bổ sung mã gói hiện tại vào danh sách gói của người nhận đó.
            containerIds.Add(row.ContainerId);
        }
        // Trả về từ điển đã gom nhóm đầy đủ dữ liệu.
        return result;
    }

    /// <remarks>
    /// Xóa MỌI thay đổi chưa lưu của cả request, không chỉ của Container. Chỉ gọi khi use case
    /// sẽ trả kết quả mà không SaveChanges nữa (ví dụ sau khi TryAddAssetLinkAsync trả false).
    /// </remarks>
    // Hủy bỏ toàn bộ các thay đổi chưa được lưu trong ngữ cảnh để khôi phục trạng thái sạch cho phiên làm việc.
    public void DiscardPendingChanges()
    {
        // Xóa sạch mọi thực thể đang được theo dõi thay đổi trong bộ theo dõi của Entity Framework.
        context.ChangeTracker.Clear();
    }
}

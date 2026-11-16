// Nạp các không gian tên phục vụ bảo mật, DTO yêu cầu/phản hồi, dịch vụ nghiệp vụ và ASP.NET Core API.
using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// Không gian tên chứa các bộ điều khiển tiếp nhận và xử lý yêu cầu HTTP trong tầng API.
namespace LegacyVault.Api.Controllers;

/// <summary>Gói bàn giao (Container) trong kho (docs/api-contract.md mục 6.6).</summary>
// Đánh dấu lớp là API Controller để tự động kích hoạt kiểm tra tính hợp lệ của mô hình dữ liệu và định dạng lỗi.
[ApiController]
// Yêu cầu người dùng phải xác thực đăng nhập trước khi có quyền gọi các endpoint trong bộ điều khiển này.
[Authorize]
// Định tuyến đường dẫn gốc cho các tài nguyên gói bàn giao thuộc một kho lưu trữ cụ thể.
[Route("api/v1/vaults/{vaultId:guid}/containers")]
// Quy định kiểu dữ liệu trả về cho máy khách luôn ở định dạng chuẩn JSON.
[Produces("application/json")]
// Khởi tạo bộ điều khiển quản lý gói bàn giao và nạp dịch vụ nghiệp vụ qua cơ chế tiêm phụ thuộc.
public sealed class ContainersController(IContainerService containerService) : ControllerBase
{
    /// <summary>Danh sách gói. sortBy: createdAt (mặc định) | name. includeRemoved mặc định false.</summary>
    // Tiếp nhận phương thức HTTP GET để lấy danh sách các gói bàn giao trong kho.
    [HttpGet]
    // Khai báo kiểu phản hồi thành công mã 200 kèm danh sách gói bàn giao đã được phân trang.
    [ProducesResponseType<PaginatedList<ContainerResponse>>(StatusCodes.Status200OK)]
    // Khai báo phản hồi lỗi mã 400 khi tham số phân trang hoặc bộ lọc gửi lên không hợp lệ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    // Khai báo phản hồi lỗi mã 404 khi không tìm thấy kho lưu trữ tương ứng với mã yêu cầu.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Phương thức xử lý lấy danh sách gói bàn giao có hỗ trợ phân trang và tùy chọn bao gồm gói đã xóa.
    public Task<PaginatedList<ContainerResponse>> GetContainers(
        Guid vaultId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] bool includeRemoved = false)
    {
        // Chuyển tiếp yêu cầu sang tầng dịch vụ để truy vấn cơ sở dữ liệu và trả về kết quả phân trang.
        return containerService.GetContainersAsync(vaultId, includeRemoved, pagination, ct);
    }

    /// <summary>Tạo gói rỗng ở DRAFT (hạng Free cũng được tạo, SETUP-17).</summary>
    // Tiếp nhận phương thức HTTP POST để tạo mới một gói bàn giao trong kho.
    [HttpPost]
    // Giới hạn định dạng dữ liệu thân yêu cầu gửi lên phải là JSON.
    [Consumes("application/json")]
    // Khai báo phản hồi mã 201 Created khi tạo gói thành công và trả về dữ liệu gói vừa tạo.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status201Created)]
    // Khai báo phản hồi mã 400 khi thông tin đầu vào tạo gói không đạt yêu cầu hợp lệ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    // Khai báo phản hồi mã 404 khi không tìm thấy kho lưu trữ chứa gói cần tạo.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi xảy ra xung đột nghiệp vụ như vượt quá giới hạn số lượng gói.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý yêu cầu tạo gói bàn giao mới ở trạng thái bản nháp ban đầu.
    public async Task<ActionResult<ContainerResponse>> CreateContainer(
        Guid vaultId, CreateContainerRequest request, CancellationToken ct)
    {
        // Gọi dịch vụ nghiệp vụ để tạo gói mới trong kho và đợi kết quả hoàn tất.
        ContainerResponse container = await containerService.CreateContainerAsync(vaultId, request, ct);
        // Trả về mã phản hồi 201 kèm tiêu đề Location điều hướng tới API xem chi tiết gói vừa tạo.
        return CreatedAtAction(nameof(GetContainer), new { vaultId, containerId = container.Id }, container);
    }

    // Tiếp nhận phương thức HTTP GET kèm mã định danh gói trên đường dẫn để lấy thông tin chi tiết.
    [HttpGet("{containerId:guid}")]
    // Khai báo phản hồi mã 200 thành công trả về thông tin chi tiết của gói bàn giao.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 404 khi không tìm thấy kho hoặc gói bàn giao với mã tương ứng.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Phương thức truy xuất thông tin chi tiết của một gói bàn giao cụ thể theo mã gói và mã kho.
    public Task<ContainerResponse> GetContainer(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        // Chuyển tiếp yêu cầu sang tầng dịch vụ để tìm gói và nạp các thành phần liên quan.
        return containerService.GetContainerAsync(vaultId, containerId, ct);
    }

    /// <summary>Đổi tên/mô tả gói; etag bắt buộc trong body.</summary>
    // Tiếp nhận phương thức HTTP PUT kèm mã gói để cập nhật tên hoặc mô tả của gói bàn giao.
    [HttpPut("{containerId:guid}")]
    // Giới hạn định dạng dữ liệu thân yêu cầu gửi lên phải là JSON.
    [Consumes("application/json")]
    // Khai báo phản hồi mã 200 thành công trả về thông tin gói sau khi cập nhật.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 400 khi dữ liệu yêu cầu cập nhật không hợp lệ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    // Khai báo phản hồi mã 404 khi không tìm thấy kho hoặc gói cần sửa.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi xảy ra xung đột phiên bản dữ liệu do sửa đổi đồng thời.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý cập nhật thông tin tên và mô tả của gói bàn giao có kiểm tra xung đột dữ liệu.
    public Task<ContainerResponse> UpdateContainer(
        Guid vaultId, Guid containerId, UpdateContainerRequest request, CancellationToken ct)
    {
        // Gọi dịch vụ nghiệp vụ để cập nhật thông tin gói và kiểm tra mã xác thực phiên bản.
        return containerService.UpdateContainerAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Xóa gói (không xóa hẳn dòng): gỡ hết tài sản, chuyển REMOVED.</summary>
    // Bắt buộc người dùng phải xác thực mã PIN của kho trước khi thực hiện thao tác xóa gói nhạy cảm.
    [RequireVaultPin]
    // Tiếp nhận phương thức HTTP DELETE kèm mã gói để thực hiện thao tác xóa mềm gói bàn giao.
    [HttpDelete("{containerId:guid}")]
    // Khai báo phản hồi mã 200 thành công trả về trạng thái gói sau khi đã được đánh dấu xóa.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 404 khi không tìm thấy kho hoặc gói cần xóa.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi trạng thái hiện tại của gói không cho phép xóa hoặc có xung đột.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý xóa mềm gói bàn giao bằng cách gỡ toàn bộ liên kết tài sản và đổi trạng thái.
    public Task<ContainerResponse> RemoveContainer(
        Guid vaultId, Guid containerId, [FromQuery] ConfirmPlanImpactRequest request, CancellationToken ct)
    {
        // Chuyển tiếp yêu cầu sang dịch vụ nghiệp vụ để hủy kích hoạt gói và tính toán lại kế hoạch.
        return containerService.RemoveContainerAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Đưa một tài sản của kho vào gói (hạng Free bị chặn, SETUP-17).</summary>
    // Tiếp nhận phương thức HTTP POST để gắn thêm một tài sản có sẵn trong kho vào gói bàn giao.
    [HttpPost("{containerId:guid}/assets")]
    // Giới hạn định dạng dữ liệu thân yêu cầu gửi lên phải là JSON.
    [Consumes("application/json")]
    // Khai báo phản hồi mã 200 thành công trả về thông tin chi tiết gói sau khi thêm tài sản.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 400 khi mã tài sản hoặc tham số gửi lên không đúng định dạng.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    // Khai báo phản hồi mã 403 khi gói dịch vụ hiện tại không đủ quyền để đưa tài sản vào gói.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    // Khai báo phản hồi mã 404 khi không tìm thấy kho, gói hoặc tài sản tương ứng.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi tài sản đã tồn tại trong gói hoặc trạng thái gói không hợp lệ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý việc liên kết một tài sản trong kho vào gói bàn giao được chỉ định.
    public Task<ContainerResponse> AddContainerAsset(
        Guid vaultId, Guid containerId, AddContainerAssetRequest request, CancellationToken ct)
    {
        // Chuyển tiếp yêu cầu sang dịch vụ để kiểm tra quyền gói dịch vụ và tạo liên kết tài sản.
        return containerService.AddAssetAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Gỡ tài sản khỏi gói (không xóa tài sản, không giải phóng dung lượng — QUOTA-02).</summary>
    // Bắt buộc người dùng phải xác thực mã PIN của kho trước khi thực hiện gỡ tài sản khỏi gói.
    [RequireVaultPin]
    // Tiếp nhận phương thức HTTP DELETE kèm mã tài sản để gỡ bỏ tài sản ra khỏi gói bàn giao.
    [HttpDelete("{containerId:guid}/assets/{assetId:guid}")]
    // Khai báo phản hồi mã 200 thành công trả về dữ liệu gói sau khi đã gỡ tài sản.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 404 khi không tìm thấy gói hoặc liên kết tài sản cần gỡ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi việc gỡ tài sản vi phạm ràng buộc trạng thái kế hoạch bàn giao.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý gỡ bỏ liên kết giữa tài sản và gói bàn giao mà không xóa tài sản gốc.
    public Task<ContainerResponse> RemoveContainerAsset(
        Guid vaultId, Guid containerId, Guid assetId, [FromQuery] ConfirmPlanImpactRequest request,
        CancellationToken ct)
    {
        // Chuyển tiếp yêu cầu sang dịch vụ nghiệp vụ để gỡ liên kết và đánh giá lại tính sẵn sàng của gói.
        return containerService.RemoveAssetAsync(vaultId, containerId, assetId, request, ct);
    }

    /// <summary>Chỉ định một hồ sơ người nhận của kho cho gói (hạng Free bị chặn, SETUP-17; kiểm suất, QUOTA-05).</summary>
    // Bắt buộc xác thực mã PIN của kho để bảo vệ thao tác phân bổ người nhận quyền thừa kế.
    [RequireVaultPin]
    // Tiếp nhận phương thức HTTP POST để chỉ định một người thụ hưởng tiếp nhận gói bàn giao.
    [HttpPost("{containerId:guid}/beneficiaries")]
    // Giới hạn định dạng dữ liệu thân yêu cầu gửi lên phải là JSON.
    [Consumes("application/json")]
    // Khai báo phản hồi mã 200 thành công trả về thông tin chi tiết gói kèm người thụ hưởng vừa thêm.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 400 khi dữ liệu chỉ định người thụ hưởng gửi lên không hợp lệ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    // Khai báo phản hồi mã 403 khi gói dịch vụ hiện tại không cho phép chỉ định người nhận.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    // Khai báo phản hồi mã 404 khi không tìm thấy kho, gói hoặc hồ sơ người nhận tương ứng.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi người này đã được chỉ định vào gói hoặc vượt hạn mức suất nhận.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý gán quyền thừa kế gói bàn giao cho một người thụ hưởng trong kho.
    public Task<ContainerResponse> AddContainerBeneficiary(
        Guid vaultId, Guid containerId, AddContainerBeneficiaryRequest request, CancellationToken ct)
    {
        // Chuyển tiếp yêu cầu sang dịch vụ để kiểm tra số suất người nhận và tạo bản ghi chỉ định.
        return containerService.AddBeneficiaryAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Gỡ người nhận khỏi gói (không xóa hồ sơ; người còn ở gói khác vẫn chiếm suất).</summary>
    // Bắt buộc xác thực mã PIN của kho trước khi xóa người thụ hưởng khỏi gói bàn giao.
    [RequireVaultPin]
    // Tiếp nhận phương thức HTTP DELETE kèm mã người nhận để gỡ quyền thừa kế gói bàn giao này.
    [HttpDelete("{containerId:guid}/beneficiaries/{beneficiaryId:guid}")]
    // Khai báo phản hồi mã 200 thành công trả về thông tin gói sau khi đã gỡ người thụ hưởng.
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    // Khai báo phản hồi mã 404 khi không tìm thấy gói hoặc chỉ định của người nhận cần gỡ.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    // Khai báo phản hồi mã 409 khi việc gỡ người nhận xung đột với trạng thái kế hoạch bàn giao.
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    // Phương thức xử lý hủy chỉ định người thụ hưởng khỏi gói mà không xóa hồ sơ gốc của họ.
    public Task<ContainerResponse> RemoveContainerBeneficiary(
        Guid vaultId, Guid containerId, Guid beneficiaryId, [FromQuery] ConfirmPlanImpactRequest request,
        CancellationToken ct)
    {
        // Chuyển tiếp yêu cầu sang dịch vụ để hủy chỉ định và cập nhật lại trạng thái sẵn sàng của gói.
        return containerService.RemoveBeneficiaryAsync(vaultId, containerId, beneficiaryId, request, ct);
    }
}

// Nạp các kiểu liệt kê định nghĩa trạng thái của các thực thể trong miền nghiệp vụ.
using LegacyVault.Domain.Enums;

// Không gian tên chứa các thực thể nghiệp vụ cốt lõi của hệ thống.
namespace LegacyVault.Domain.Entities;

/// <summary>
/// Container (gói bàn giao) trong một kho (bảng Containers), SRS §5.2 bước 2 và 6.
/// </summary>
/// <remarks>
/// - SETUP-03: liên kết gắn với TÀI SẢN, không với phiên bản; nội dung gói là phiên bản hiện hành của tài sản.
/// - SETUP-04: Container không có phiên bản riêng; mọi thay đổi tăng HandoverPlan.ConfigVersion.
/// - SETUP-17: hạng Free chỉ được tạo/đổi tên/xóa Container rỗng.
/// - NFR-14: tối đa 50 Container chưa bị xóa mỗi kho.
/// - SETUP-16: READY khi có ≥1 tài sản VÀ ≥1 chỉ định người nhận (Designation); backend tự tính lại sau mỗi thay đổi.
/// </remarks>
// Lớp thực thể đại diện cho gói bàn giao tài sản và người thụ hưởng trong kho lưu trữ.
public class Container
{
    // Khóa chính định danh gói bàn giao, tự động sinh khóa theo chuẩn UUID phiên bản 7 có thứ tự thời gian.
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Mã khóa ngoại liên kết tới kho lưu trữ sở hữu gói bàn giao này.
    public Guid VaultId { get; set; }

    // Thuộc tính điều hướng tham chiếu đến đối tượng kho lưu trữ sở hữu gói bàn giao.
    public Vault Vault { get; set; } = null!;

    // Tên gợi nhớ của gói bàn giao do người dùng đặt để dễ nhận biết.
    public string Name { get; set; } = string.Empty;

    /// <summary>Không mã hóa: không ghi mật khẩu, seed phrase vào đây (ASSET-02).</summary>
    // Mô tả chi tiết mục đích hoặc nội dung của gói, lưu dưới dạng văn bản thuần không mã hóa.
    public string? Description { get; set; }

    // Trạng thái hiện tại của gói bàn giao như bản nháp, đã sẵn sàng hoặc đã đánh dấu xóa.
    public ContainerStatus Status { get; set; }

    // Mốc thời gian tạo gói bàn giao được ghi nhận theo chuẩn UTC có múi giờ.
    public DateTimeOffset CreatedAt { get; set; }

    // Mốc thời gian cập nhật thông tin gói lần cuối, dùng làm khóa kiểm tra phiên bản chống ghi đè đồng thời.
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Thời điểm xóa gói; null khi gói chưa bị xóa.</summary>
    // Thời điểm người dùng thực hiện xóa mềm gói bàn giao, mang giá trị null nếu gói vẫn đang hoạt động.
    public DateTimeOffset? RemovedAt { get; set; }

    // Danh sách các liên kết tài sản trong kho được đưa vào gói bàn giao này.
    public List<ContainerAsset> Assets { get; set; } = new List<ContainerAsset>();

    /// <summary>Các chỉ định người nhận của gói, sắp theo thời điểm gắn.</summary>
    // Danh sách các phân bổ người nhận được thừa hưởng gói bàn giao này theo thứ tự thời gian gắn.
    public List<Designation> Designations { get; set; } = new List<Designation>();
}

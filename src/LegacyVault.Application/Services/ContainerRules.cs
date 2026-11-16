// Nạp các thực thể miền và kiểu liệt kê phục vụ việc kiểm tra và thực thi luật nghiệp vụ gói bàn giao.
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

// Không gian tên chứa các dịch vụ và quy tắc nghiệp vụ thuộc tầng ứng dụng.
namespace LegacyVault.Application.Services;

/// <summary>Luật nghiệp vụ của gói (Container) dùng chung cho các service.</summary>
/// <remarks>
/// Cần container đã nạp Assets và Designations.
/// SETUP-08: hồ sơ người nhận đã lưu luôn đủ số CCCD, họ tên, ngày sinh nên mọi chỉ định đều hợp lệ;
/// Không kiểm tuổi người nhận (đã bỏ SETUP-19): người nhận chưa đủ 18 tuổi vẫn tính như mọi người khác.
/// </remarks>
// Lớp tĩnh cung cấp các hằng số và quy tắc nghiệp vụ tập trung áp dụng cho các gói bàn giao.
public static class ContainerRules
{
    /// <summary>NFR-14: tối đa 50 gói chưa bị xóa mỗi kho.</summary>
    // Giới hạn số lượng gói bàn giao tối đa đang hoạt động cho phép trong một kho lưu trữ.
    public const int MaxActiveContainers = 50;

    /// <summary>
    /// Số gói tối đa của kho theo gói dịch vụ đang dùng do Admin đặt (Plans.Free|Xs|XsMax|MÃ.MaxPackages); chưa đặt thì
    /// dùng mức gốc <see cref="MaxActiveContainers"/>.
    /// </summary>
    // Xác định số gói bàn giao tối đa được phép tạo dựa trên gói dịch vụ và quyền lợi thực tế của kho.
    public static int MaxActiveFor(VaultEntitlement entitlement)
    {
        // Xác định mã gói cước của kho, ưu tiên kiểm tra xem kho có đang ở gói miễn phí hay gói nâng cao.
        string code = entitlement.Status == EntitlementStatus.Free
            ? PlanCodes.Free
            : PlanCodes.Resolve(entitlement.Tier, entitlement.PlanCode);
        // Chuyển đổi mã gói cước thành chuỗi phân đoạn tương ứng để tra cứu khóa cấu hình hệ thống.
        string segment = code == PlanCodes.Free ? "Free" : code == PlanCodes.Xs ? "Xs" : code == PlanCodes.XsMax ? "XsMax" : code;
        // Đọc cấu hình số gói tối đa từ cài đặt hệ thống khi chạy, nếu chưa cấu hình thì dùng giá trị mặc định.
        return RuntimeSettings.Int("Plans." + segment + ".MaxPackages", MaxActiveContainers);
    }

    // Mã định danh lỗi thông báo gói bàn giao chưa được gán bất kỳ tài sản nào.
    public const string NoAsset = "NO_ASSET";
    // Mã định danh lỗi thông báo gói bàn giao chưa được chỉ định người nhận nào.
    public const string NoDesignation = "NO_DESIGNATION";

    /// <summary>
    /// SETUP-16: backend tự tính DRAFT/READY. READY cần ≥1 tài sản VÀ ≥1 chỉ định người nhận hợp lệ.
    /// Gói REMOVED do Owner chọn nên giữ nguyên.
    /// </summary>
    // Tự động tính toán và cập nhật lại trạng thái của gói sang sẵn sàng hoặc bản nháp sau mỗi thay đổi.
    public static void RecalculateStatus(Container container)
    {
        // Nếu gói đã bị người dùng xóa thì giữ nguyên trạng thái đã xóa và không tính toán lại.
        if (container.Status == ContainerStatus.Removed)
        {
            // Kết thúc sớm hàm xử lý vì gói đã bị xóa.
            return;
        }

        // Kiểm tra điều kiện gói phải có ít nhất một tài sản và ít nhất một người nhận được chỉ định.
        if (container.Assets.Count > 0 && container.Designations.Count > 0)
        {
            // Chuyển trạng thái gói sang sẵn sàng bàn giao khi đã thỏa mãn đầy đủ điều kiện.
            container.Status = ContainerStatus.Ready;
        }
        // Trường hợp gói còn thiếu tài sản hoặc chưa có người nhận.
        else
        {
            // Đưa trạng thái gói trở về bản nháp để người dùng tiếp tục hoàn thiện.
            container.Status = ContainerStatus.Draft;
        }
    }

    /// <summary>Lý do gói còn DRAFT để FE hướng dẫn. Gói READY/REMOVED → danh sách rỗng.</summary>
    // Kiểm tra và trả về danh sách các điều kiện còn thiếu khiến gói bàn giao chưa thể sẵn sàng.
    public static List<string> GetMissingRequirements(Container container)
    {
        // Khởi tạo danh sách chứa các mã điều kiện còn thiếu để trả về cho giao diện người dùng.
        List<string> missing = new List<string>();
        // Nếu gói đã ở trạng thái sẵn sàng hoặc đã bị xóa thì không cần kiểm tra điều kiện còn thiếu.
        if (container.Status == ContainerStatus.Ready || container.Status == ContainerStatus.Removed)
        {
            // Trả về danh sách rỗng vì gói đã hoàn tất hoặc không còn hoạt động.
            return missing;
        }

        // Kiểm tra nếu gói chưa có tài sản nào được liên kết vào.
        if (container.Assets.Count == 0)
        {
            // Bổ sung mã thiếu tài sản vào danh sách cảnh báo.
            missing.Add(NoAsset);
        }
        // Kiểm tra nếu gói chưa có người thụ hưởng nào được chỉ định.
        if (container.Designations.Count == 0)
        {
            // Bổ sung mã thiếu người nhận vào danh sách cảnh báo.
            missing.Add(NoDesignation);
        }
        // Trả về danh sách các điều kiện còn thiếu để hiển thị hướng dẫn người dùng.
        return missing;
    }
}

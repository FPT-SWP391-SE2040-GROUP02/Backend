using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOwnerAliveLeftovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Luồng "Chủ kho báo còn sống" đã gỡ; giờ bỏ nốt giá trị enum của nó. EF đọc Status và Action dạng chuỗi,
            // gặp giá trị không còn trong enum sẽ lỗi, nên dọn hàng cũ trước (không đổi cấu trúc bảng).
            // Kế hoạch ở trạng thái Blocked không còn mã nào đặt; nếu còn hàng cũ thì trả về Active (Chủ kho còn sống).
            migrationBuilder.Sql("UPDATE [HandoverPlans] SET [Status] = 'Active' WHERE [Status] = 'Blocked';");
            // Dòng nhật ký báo còn sống và kết luận báo còn sống.
            migrationBuilder.Sql("DELETE FROM [AuditLogs] WHERE [Action] IN ('ObjectionReported', 'ObjectionConcluded');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dữ liệu đã dọn ở Up (dòng nhật ký, trạng thái Blocked) không khôi phục được.
        }
    }
}

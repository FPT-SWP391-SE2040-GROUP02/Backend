using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Đổi trạng thái gói "Archived" thành "Removed" (xóa gói, không khôi phục được) và cột ArchivedAt thành RemovedAt.
    /// Trạng thái lưu dạng chữ nên phải đổi cả dữ liệu cũ. Nhật ký (AuditLogs) KHÔNG sửa: dòng cũ giữ ContainerArchived.
    /// </summary>
    public partial class RenameContainerArchivedToRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ArchivedAt",
                table: "Containers",
                newName: "RemovedAt");

            migrationBuilder.Sql("UPDATE [Containers] SET [Status] = N'Removed' WHERE [Status] = N'Archived';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Containers] SET [Status] = N'Archived' WHERE [Status] = N'Removed';");

            migrationBuilder.RenameColumn(
                name: "RemovedAt",
                table: "Containers",
                newName: "ArchivedAt");
        }
    }
}

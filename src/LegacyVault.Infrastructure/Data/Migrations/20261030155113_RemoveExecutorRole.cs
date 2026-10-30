using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveExecutorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bỏ hẳn Executor dự phòng (30/10/2026): xóa mọi dòng Backup trước khi xóa cột, để không còn dòng nào bị hiểu
            // nhầm là Executor chính. Không bảng nào có khóa ngoại tới Executors; chỉ dọn ReplacesExecutorId trỏ tới dòng bị xóa.
            migrationBuilder.Sql(
                "UPDATE [Executors] SET [ReplacesExecutorId] = NULL " +
                "WHERE [ReplacesExecutorId] IN (SELECT [Id] FROM [Executors] WHERE [Role] = N'Backup');");
            migrationBuilder.Sql("DELETE FROM [Executors] WHERE [Role] = N'Backup';");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Executors");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Executors",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                // Dòng Backup đã xóa không khôi phục được; mọi dòng còn lại là Executor chính.
                defaultValue: "Primary");
        }
    }
}

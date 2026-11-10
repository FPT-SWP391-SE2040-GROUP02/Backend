using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueUserPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chuẩn hóa số cũ giống PhoneRules.NormalizeContactPhone (số do Admin nhập trước đây chưa chuẩn hóa), số rỗng thành NULL.
            migrationBuilder.Sql(@"
UPDATE [Users]
SET [Phone] = NULLIF(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    [Phone], ' ', ''), CHAR(9), ''), CHAR(10), ''), CHAR(13), ''), '.', ''), '-', ''), '(', ''), ')', ''), '')
WHERE [Phone] IS NOT NULL;");

            // Số đang trùng: giữ ở tài khoản tạo sớm nhất, các tài khoản còn lại để trống để nhập số khác khi đăng nhập.
            migrationBuilder.Sql(@"
WITH [Ranked] AS (
    SELECT [Phone], ROW_NUMBER() OVER (PARTITION BY [Phone] ORDER BY [CreatedAt], [Id]) AS [Rn]
    FROM [Users]
    WHERE [Phone] IS NOT NULL
)
UPDATE [Ranked] SET [Phone] = NULL WHERE [Rn] > 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true,
                filter: "[Phone] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Phone",
                table: "Users");
        }
    }
}

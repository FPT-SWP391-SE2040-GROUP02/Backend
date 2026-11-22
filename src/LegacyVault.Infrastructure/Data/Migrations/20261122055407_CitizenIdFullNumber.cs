using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Bỏ khái niệm "4 số cuối": hồ sơ danh tính người nhận và lượt eKYC lưu số căn cước đầy đủ.
    /// Đổi tên và nới cột thay vì xóa để giữ dữ liệu cũ (dòng cũ vẫn chỉ có 4 số).
    /// </summary>
    public partial class CitizenIdFullNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CitizenIdLast4",
                table: "HandoverSubmissions",
                newName: "CitizenId");

            migrationBuilder.AlterColumn<string>(
                name: "CitizenId",
                table: "HandoverSubmissions",
                type: "varchar(12)",
                unicode: false,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4)",
                oldMaxLength: 4);

            migrationBuilder.RenameColumn(
                name: "CitizenIdLast4",
                table: "EkycVerifications",
                newName: "CitizenId");

            migrationBuilder.AlterColumn<string>(
                name: "CitizenId",
                table: "EkycVerifications",
                type: "varchar(12)",
                unicode: false,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(4)",
                oldUnicode: false,
                oldMaxLength: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Quay lại 4 số cuối: cắt số đầy đủ còn 4 số cuối trước khi thu cột.
            migrationBuilder.Sql("UPDATE HandoverSubmissions SET CitizenId = RIGHT(CitizenId, 4)");
            migrationBuilder.Sql("UPDATE EkycVerifications SET CitizenId = RIGHT(CitizenId, 4)");

            migrationBuilder.AlterColumn<string>(
                name: "CitizenId",
                table: "HandoverSubmissions",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(12)",
                oldUnicode: false,
                oldMaxLength: 12);

            migrationBuilder.RenameColumn(
                name: "CitizenId",
                table: "HandoverSubmissions",
                newName: "CitizenIdLast4");

            migrationBuilder.AlterColumn<string>(
                name: "CitizenId",
                table: "EkycVerifications",
                type: "varchar(4)",
                unicode: false,
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(12)",
                oldUnicode: false,
                oldMaxLength: 12);

            migrationBuilder.RenameColumn(
                name: "CitizenId",
                table: "EkycVerifications",
                newName: "CitizenIdLast4");
        }
    }
}

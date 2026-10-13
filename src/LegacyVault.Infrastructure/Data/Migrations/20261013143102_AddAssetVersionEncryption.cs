using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Thêm cột lưu DEK đã bọc cho mã hóa envelope nội dung tài sản.
    /// Tài sản tạo trước migration này (chỉ có ở database dev) là bản CHƯA mã hóa và không đọc lại được:
    /// xóa các dòng Assets/AssetVersions cũ cùng thư mục App_Data/asset-content rồi tạo lại.
    /// </summary>
    public partial class AddAssetVersionEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "AssetVersions",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "WrappedDataKey",
                table: "AssetVersions",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "WrappedDataKey",
                table: "AssetVersions");
        }
    }
}

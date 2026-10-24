using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAssetVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentDeletedAt",
                table: "Assets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "Assets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "Assets",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "Assets",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "Assets",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "Assets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StorageDeletedAt",
                table: "Assets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "Assets",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "WrappedDataKey",
                table: "Assets",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: false,
                defaultValue: new byte[0]);

            // Giữ lại nội dung của phiên bản hiện hành cho từng tài sản. Các phiên bản cũ hơn bị bỏ: bản ghi mất
            // cùng bảng, tệp mã hóa của chúng còn trong kho lưu trữ (không có đường dẫn nào trỏ tới nữa) và cần dọn thủ công.
            migrationBuilder.Sql(@"
UPDATE a
SET a.FileName = v.FileName,
    a.ContentType = v.ContentType,
    a.SizeBytes = v.SizeBytes,
    a.Sha256 = v.Sha256,
    a.StorageKey = v.StorageKey,
    a.EncryptionKeyId = v.EncryptionKeyId,
    a.WrappedDataKey = v.WrappedDataKey,
    a.ContentDeletedAt = v.ContentDeletedAt,
    a.StorageDeletedAt = v.StorageDeletedAt
FROM Assets a
JOIN AssetVersions v ON v.AssetId = a.Id AND v.VersionNumber = a.CurrentVersionNumber;");

            migrationBuilder.DropTable(
                name: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "AssetVersionId",
                table: "HandoverSnapshotItems");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "Assets");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_StorageKey",
                table: "Assets",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentVersionNumber",
                table: "Assets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AssetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentDeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageDeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StorageKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetVersions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Mỗi tài sản quay về đúng một phiên bản (số 1) mang nội dung hiện có.
            migrationBuilder.Sql(@"
INSERT INTO AssetVersions (Id, AssetId, VersionNumber, FileName, ContentType, SizeBytes, Sha256, StorageKey,
    EncryptionKeyId, WrappedDataKey, ContentDeletedAt, StorageDeletedAt, CreatedAt)
SELECT NEWID(), Id, 1, FileName, ContentType, SizeBytes, Sha256, StorageKey,
    EncryptionKeyId, WrappedDataKey, ContentDeletedAt, StorageDeletedAt, CreatedAt
FROM Assets;");

            migrationBuilder.AddColumn<Guid>(
                name: "AssetVersionId",
                table: "HandoverSnapshotItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
UPDATE i
SET i.AssetVersionId = v.Id
FROM HandoverSnapshotItems i
JOIN AssetVersions v ON v.AssetId = i.AssetId AND v.VersionNumber = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_AssetVersions_AssetId_VersionNumber",
                table: "AssetVersions",
                columns: new[] { "AssetId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetVersions_StorageKey",
                table: "AssetVersions",
                column: "StorageKey",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_Assets_StorageKey",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ContentDeletedAt",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "StorageDeletedAt",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WrappedDataKey",
                table: "Assets");
        }
    }
}

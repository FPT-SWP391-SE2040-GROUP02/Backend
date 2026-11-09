using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverSubmissionPaper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaperEncryptionKeyId",
                table: "HandoverSubmissions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "PaperSizeBytes",
                table: "HandoverSubmissions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "PaperStorageKey",
                table: "HandoverSubmissions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "PaperWrappedDataKey",
                table: "HandoverSubmissions",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaperEncryptionKeyId",
                table: "HandoverSubmissions");

            migrationBuilder.DropColumn(
                name: "PaperSizeBytes",
                table: "HandoverSubmissions");

            migrationBuilder.DropColumn(
                name: "PaperStorageKey",
                table: "HandoverSubmissions");

            migrationBuilder.DropColumn(
                name: "PaperWrappedDataKey",
                table: "HandoverSubmissions");
        }
    }
}

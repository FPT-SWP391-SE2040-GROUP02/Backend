using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemExecutorAndEncryptedKeyShard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutorRoleTitle",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalOrganization",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LegalVerifiedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedShard",
                table: "ExecutorKeyShards",
                type: "varchar(512)",
                unicode: false,
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "ExecutorKeyShards",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "WrappedDataKey",
                table: "ExecutorKeyShards",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutorRoleTitle",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LegalOrganization",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LegalVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EncryptedShard",
                table: "ExecutorKeyShards");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "ExecutorKeyShards");

            migrationBuilder.DropColumn(
                name: "WrappedDataKey",
                table: "ExecutorKeyShards");
        }
    }
}

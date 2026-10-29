using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveContentSha256 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarSha256",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DocumentImageSha256",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageSha256",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "SelfieImageSha256",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "DeathCaseFiles");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "Assets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarSha256",
                table: "Users",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentImageSha256",
                table: "OwnerIdentities",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingDocumentImageSha256",
                table: "EkycVerifications",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelfieImageSha256",
                table: "DeathCaseObjections",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "DeathCaseFiles",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "Assets",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }
    }
}

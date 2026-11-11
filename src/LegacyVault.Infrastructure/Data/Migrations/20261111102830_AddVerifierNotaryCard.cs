using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifierNotaryCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotaryCardContentType",
                table: "Users",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotaryCardEncryptionKeyId",
                table: "Users",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NotaryCardSizeBytes",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotaryCardStorageKey",
                table: "Users",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotaryCardUploadedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "NotaryCardWrappedDataKey",
                table: "Users",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotaryCardContentType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotaryCardEncryptionKeyId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotaryCardSizeBytes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotaryCardStorageKey",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotaryCardUploadedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotaryCardWrappedDataKey",
                table: "Users");
        }
    }
}

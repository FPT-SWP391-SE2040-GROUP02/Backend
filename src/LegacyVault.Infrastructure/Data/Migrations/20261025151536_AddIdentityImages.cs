using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DocumentImageCapturedAt",
                table: "OwnerIdentities",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentImageContentType",
                table: "OwnerIdentities",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentImageEncryptionKeyId",
                table: "OwnerIdentities",
                type: "varchar(64)",
                unicode: false,
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

            migrationBuilder.AddColumn<long>(
                name: "DocumentImageSizeBytes",
                table: "OwnerIdentities",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentImageStorageKey",
                table: "OwnerIdentities",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "DocumentImageWrappedDataKey",
                table: "OwnerIdentities",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingDocumentImageContentType",
                table: "EkycVerifications",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingDocumentImageEncryptionKeyId",
                table: "EkycVerifications",
                type: "varchar(64)",
                unicode: false,
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

            migrationBuilder.AddColumn<long>(
                name: "PendingDocumentImageSizeBytes",
                table: "EkycVerifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingDocumentImageStorageKey",
                table: "EkycVerifications",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PendingDocumentImageWrappedDataKey",
                table: "EkycVerifications",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SelfieImageCapturedAt",
                table: "DeathCaseObjections",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelfieImageContentType",
                table: "DeathCaseObjections",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelfieImageEncryptionKeyId",
                table: "DeathCaseObjections",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SelfieImageMatched",
                table: "DeathCaseObjections",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelfieImageSha256",
                table: "DeathCaseObjections",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SelfieImageSizeBytes",
                table: "DeathCaseObjections",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelfieImageStorageKey",
                table: "DeathCaseObjections",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SelfieImageWrappedDataKey",
                table: "DeathCaseObjections",
                type: "binary(60)",
                fixedLength: true,
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentImageCapturedAt",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "DocumentImageContentType",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "DocumentImageEncryptionKeyId",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "DocumentImageSha256",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "DocumentImageSizeBytes",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "DocumentImageStorageKey",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "DocumentImageWrappedDataKey",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageContentType",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageEncryptionKeyId",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageSha256",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageSizeBytes",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageStorageKey",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "PendingDocumentImageWrappedDataKey",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "SelfieImageCapturedAt",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageContentType",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageEncryptionKeyId",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageMatched",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageSha256",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageSizeBytes",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageStorageKey",
                table: "DeathCaseObjections");

            migrationBuilder.DropColumn(
                name: "SelfieImageWrappedDataKey",
                table: "DeathCaseObjections");
        }
    }
}

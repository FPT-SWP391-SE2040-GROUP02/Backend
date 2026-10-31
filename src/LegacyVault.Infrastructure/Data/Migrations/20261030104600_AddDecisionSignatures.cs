using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Signature",
                table: "DeathCaseDecisions",
                type: "varbinary(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SigningKeyId",
                table: "DeathCaseDecisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VerifierSigningGuards",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    LockLevel = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifierSigningGuards", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_VerifierSigningGuards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerifierSigningKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PublicKey = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    PrivateKeyEncrypted = table.Column<byte[]>(type: "varbinary(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifierSigningKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerifierSigningKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseDecisions_SigningKeyId",
                table: "DeathCaseDecisions",
                column: "SigningKeyId");

            migrationBuilder.CreateIndex(
                name: "UX_VerifierSigningKeys_ActivePerUser",
                table: "VerifierSigningKeys",
                column: "UserId",
                unique: true,
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_DeathCaseDecisions_VerifierSigningKeys_SigningKeyId",
                table: "DeathCaseDecisions",
                column: "SigningKeyId",
                principalTable: "VerifierSigningKeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeathCaseDecisions_VerifierSigningKeys_SigningKeyId",
                table: "DeathCaseDecisions");

            migrationBuilder.DropTable(
                name: "VerifierSigningGuards");

            migrationBuilder.DropTable(
                name: "VerifierSigningKeys");

            migrationBuilder.DropIndex(
                name: "IX_DeathCaseDecisions_SigningKeyId",
                table: "DeathCaseDecisions");

            migrationBuilder.DropColumn(
                name: "Signature",
                table: "DeathCaseDecisions");

            migrationBuilder.DropColumn(
                name: "SigningKeyId",
                table: "DeathCaseDecisions");
        }
    }
}

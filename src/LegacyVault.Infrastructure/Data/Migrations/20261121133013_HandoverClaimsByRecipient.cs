using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HandoverClaimsByRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HandoverRecords_VaultId",
                table: "HandoverRecords");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "HandoverRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestTokenHash",
                table: "Beneficiaries",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HandoverClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverClaims_HandoverRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "HandoverRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HandoverRecordFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverRecordFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverRecordFiles_HandoverRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "HandoverRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecords_VaultId",
                table: "HandoverRecords",
                column: "VaultId",
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Beneficiaries_RequestTokenHash",
                table: "Beneficiaries",
                column: "RequestTokenHash",
                unique: true,
                filter: "[RequestTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverClaims_RecordId_BeneficiaryId",
                table: "HandoverClaims",
                columns: new[] { "RecordId", "BeneficiaryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverClaims_UserId",
                table: "HandoverClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecordFiles_RecordId_ClaimId",
                table: "HandoverRecordFiles",
                columns: new[] { "RecordId", "ClaimId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandoverClaims");

            migrationBuilder.DropTable(
                name: "HandoverRecordFiles");

            migrationBuilder.DropIndex(
                name: "IX_HandoverRecords_VaultId",
                table: "HandoverRecords");

            migrationBuilder.DropIndex(
                name: "IX_Beneficiaries_RequestTokenHash",
                table: "Beneficiaries");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "HandoverRecords");

            migrationBuilder.DropColumn(
                name: "RequestTokenHash",
                table: "Beneficiaries");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecords_VaultId",
                table: "HandoverRecords",
                column: "VaultId",
                unique: true);
        }
    }
}

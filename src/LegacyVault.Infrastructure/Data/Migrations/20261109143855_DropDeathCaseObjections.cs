using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropDeathCaseObjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeathCaseObjections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeathCaseObjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcludedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcludedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Conclusion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ConclusionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FirstReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InsufficientEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    LastReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousCaseStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousPlanStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReportCount = table.Column<int>(type: "int", nullable: false),
                    SelfieDeadlineAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SelfieFailedCount = table.Column<int>(type: "int", nullable: false),
                    SelfieFailureWindowStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SelfieImageCapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SelfieImageContentType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    SelfieImageEncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    SelfieImageMatched = table.Column<bool>(type: "bit", nullable: true),
                    SelfieImageSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    SelfieImageStorageKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    SelfieImageWrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: true),
                    SelfiePassedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifierDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseObjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseObjections_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseObjections_CaseId",
                table: "DeathCaseObjections",
                column: "CaseId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseObjections_VaultId",
                table: "DeathCaseObjections",
                column: "VaultId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseObjections_VerifierUserId_Status",
                table: "DeathCaseObjections",
                columns: new[] { "VerifierUserId", "Status" });
        }
    }
}

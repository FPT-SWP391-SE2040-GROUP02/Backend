using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeathCaseObjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeathCaseObjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FirstReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastReportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReportCount = table.Column<int>(type: "int", nullable: false),
                    SelfieDeadlineAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreviousCaseStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousPlanStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SelfiePassedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SelfieFailedCount = table.Column<int>(type: "int", nullable: false),
                    SelfieFailureWindowStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VerifierDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InsufficientEvidenceCount = table.Column<int>(type: "int", nullable: false),
                    Conclusion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ConclusionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConcludedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConcludedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeathCaseObjections");
        }
    }
}

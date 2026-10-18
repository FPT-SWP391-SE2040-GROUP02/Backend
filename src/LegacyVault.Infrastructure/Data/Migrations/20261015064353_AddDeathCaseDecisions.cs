using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeathCaseDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeathCaseDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedParts = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseDecisions_DeathCaseAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "DeathCaseAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseDecisions_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseDecisions_Users_VerifierUserId",
                        column: x => x.VerifierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseDecisions_AssignmentId",
                table: "DeathCaseDecisions",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseDecisions_CaseId",
                table: "DeathCaseDecisions",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseDecisions_VerifierUserId",
                table: "DeathCaseDecisions",
                column: "VerifierUserId");

            migrationBuilder.CreateIndex(
                name: "UX_DeathCaseDecisions_FinalPerCase",
                table: "DeathCaseDecisions",
                column: "CaseId",
                unique: true,
                filter: "[Kind] IN ('Rejected','Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeathCaseDecisions");
        }
    }
}

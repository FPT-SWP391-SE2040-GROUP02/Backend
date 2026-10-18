using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckInPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GraceEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OverdueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AwaitingCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInPeriods_HandoverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "HandoverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckInSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleDays = table.Column<int>(type: "int", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    PendingCycleDays = table.Column<int>(type: "int", nullable: true),
                    PendingGraceDays = table.Column<int>(type: "int", nullable: true),
                    LastCheckInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInSchedules_HandoverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "HandoverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckInReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInReminders_CheckInPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "CheckInPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckInPeriods_DueAt",
                table: "CheckInPeriods",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInPeriods_PlanId_Sequence",
                table: "CheckInPeriods",
                columns: new[] { "PlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CheckInPeriods_OpenPerPlan",
                table: "CheckInPeriods",
                column: "PlanId",
                unique: true,
                filter: "[ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInReminders_PeriodId_Kind",
                table: "CheckInReminders",
                columns: new[] { "PeriodId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckInSchedules_PlanId",
                table: "CheckInSchedules",
                column: "PlanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckInReminders");

            migrationBuilder.DropTable(
                name: "CheckInSchedules");

            migrationBuilder.DropTable(
                name: "CheckInPeriods");
        }
    }
}

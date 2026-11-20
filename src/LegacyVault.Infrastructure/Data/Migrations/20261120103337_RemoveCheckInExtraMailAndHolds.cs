using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCheckInExtraMailAndHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bỏ điểm danh: hai trạng thái quá hạn điểm danh và chờ kiểm tra không còn, kế hoạch đang ở đó về ACTIVE.
            migrationBuilder.Sql("UPDATE [HandoverPlans] SET [Status] = 'Active' WHERE [Status] IN ('CheckinOverdue', 'AwaitingCheck');");
            migrationBuilder.Sql("UPDATE [DeathCases] SET [StatusBeforeCase] = 'Active' WHERE [StatusBeforeCase] IN ('CheckinOverdue', 'AwaitingCheck');");
            // Tham số và mẫu thư của điểm danh không còn ai đọc.
            migrationBuilder.Sql("DELETE FROM [SystemSettings] WHERE [Key] LIKE 'CheckIn.%';");
            migrationBuilder.Sql("DELETE FROM [EmailTemplates] WHERE [Key] IN ('EXECUTOR_OVERDUE', 'EXECUTOR_OVERDUE_REMINDER', 'CHECKIN_DUE_FIRST', 'CHECKIN_DUE_SECOND', 'CHECKIN_LATE');");

            migrationBuilder.DropTable(
                name: "CheckInReminders");

            migrationBuilder.DropTable(
                name: "CheckInSchedules");

            migrationBuilder.DropTable(
                name: "ExtraMailRules");

            migrationBuilder.DropTable(
                name: "ExtraMailSends");

            migrationBuilder.DropTable(
                name: "Holds");

            migrationBuilder.DropTable(
                name: "CheckInPeriods");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckInPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwaitingCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GraceEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LateReminderGapDays = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    MaxLateReminders = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    OverdueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProxyContactMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProxyContactedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProxyContactedWho = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedByExecutor = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CycleDays = table.Column<int>(type: "int", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    LastCheckInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PendingCycleDays = table.Column<int>(type: "int", nullable: true),
                    PendingGraceDays = table.Column<int>(type: "int", nullable: true),
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
                name: "ExtraMailRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ButtonText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DaysFromDue = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraMailRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtraMailSends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraMailSends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponsibleUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Holds_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckInReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_ExtraMailSends_RuleId_PeriodId",
                table: "ExtraMailSends",
                columns: new[] { "RuleId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holds_Status_ReviewAt",
                table: "Holds",
                columns: new[] { "Status", "ReviewAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Holds_VaultId_Status",
                table: "Holds",
                columns: new[] { "VaultId", "Status" });
        }
    }
}

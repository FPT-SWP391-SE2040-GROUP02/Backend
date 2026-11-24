using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverRecordDeviationAndReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutorReminderSentAt",
                table: "HandoverRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SplitDeviationConfirmedAt",
                table: "HandoverRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SplitDeviationConfirmedByUserId",
                table: "HandoverRecords",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutorReminderSentAt",
                table: "HandoverRecords");

            migrationBuilder.DropColumn(
                name: "SplitDeviationConfirmedAt",
                table: "HandoverRecords");

            migrationBuilder.DropColumn(
                name: "SplitDeviationConfirmedByUserId",
                table: "HandoverRecords");
        }
    }
}

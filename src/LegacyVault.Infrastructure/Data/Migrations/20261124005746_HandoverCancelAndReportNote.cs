using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HandoverCancelAndReportNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptNote",
                table: "HandoverReports",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "HandoverRecords",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByVerifierUserId",
                table: "HandoverRecords",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptNote",
                table: "HandoverReports");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "HandoverRecords");

            migrationBuilder.DropColumn(
                name: "CancelledByVerifierUserId",
                table: "HandoverRecords");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HandoverReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceiptExecutorSignedAt",
                table: "HandoverTurns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptExecutorUserId",
                table: "HandoverTurns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceiptIssueAt",
                table: "HandoverTurns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptIssueNote",
                table: "HandoverTurns",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptExecutorSignedAt",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "ReceiptExecutorUserId",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "ReceiptIssueAt",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "ReceiptIssueNote",
                table: "HandoverTurns");
        }
    }
}

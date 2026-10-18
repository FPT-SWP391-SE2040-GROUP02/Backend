using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "HandoverTurns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionDeadline",
                table: "HandoverTurns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetentionNoticeStage",
                table: "HandoverTurns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RetentionRemainingSeconds",
                table: "HandoverTurns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionStartedAt",
                table: "HandoverTurns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetentionStatus",
                table: "HandoverTurns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContentDeletedAt",
                table: "AssetVersions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StorageDeletedAt",
                table: "AssetVersions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverTurns_PlanId_RetentionStatus",
                table: "HandoverTurns",
                columns: new[] { "PlanId", "RetentionStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HandoverTurns_PlanId_RetentionStatus",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "RetentionDeadline",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "RetentionNoticeStage",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "RetentionRemainingSeconds",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "RetentionStartedAt",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "RetentionStatus",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "ContentDeletedAt",
                table: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "StorageDeletedAt",
                table: "AssetVersions");
        }
    }
}

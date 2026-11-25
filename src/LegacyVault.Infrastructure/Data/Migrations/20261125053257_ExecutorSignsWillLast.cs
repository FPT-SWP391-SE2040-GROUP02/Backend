using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExecutorSignsWillLast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExecutorId",
                table: "WillVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmedWillVersionNo",
                table: "Executors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeclinedWillVersionNo",
                table: "Executors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WillConfirmedAt",
                table: "Executors",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutorId",
                table: "WillVersions");

            migrationBuilder.DropColumn(
                name: "ConfirmedWillVersionNo",
                table: "Executors");

            migrationBuilder.DropColumn(
                name: "DeclinedWillVersionNo",
                table: "Executors");

            migrationBuilder.DropColumn(
                name: "WillConfirmedAt",
                table: "Executors");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyCheckInContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProxyContactMethod",
                table: "CheckInPeriods",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProxyContactedAt",
                table: "CheckInPeriods",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProxyContactedWho",
                table: "CheckInPeriods",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProxyContactMethod",
                table: "CheckInPeriods");

            migrationBuilder.DropColumn(
                name: "ProxyContactedAt",
                table: "CheckInPeriods");

            migrationBuilder.DropColumn(
                name: "ProxyContactedWho",
                table: "CheckInPeriods");
        }
    }
}

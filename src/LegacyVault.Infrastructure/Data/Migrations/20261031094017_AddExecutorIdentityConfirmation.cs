using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutorIdentityConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IdentityConfirmedAt",
                table: "HandoverTurns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IdentityConfirmedByUserId",
                table: "HandoverTurns",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentityConfirmedAt",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "IdentityConfirmedByUserId",
                table: "HandoverTurns");
        }
    }
}

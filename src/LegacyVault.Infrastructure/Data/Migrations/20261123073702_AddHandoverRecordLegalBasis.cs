using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverRecordLegalBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LegalBasisConfirmedAt",
                table: "HandoverRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LegalBasisConfirmedByUserId",
                table: "HandoverRecords",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LegalBasisConfirmedAt",
                table: "HandoverRecords");

            migrationBuilder.DropColumn(
                name: "LegalBasisConfirmedByUserId",
                table: "HandoverRecords");
        }
    }
}

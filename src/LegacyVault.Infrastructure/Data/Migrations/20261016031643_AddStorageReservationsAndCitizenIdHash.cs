using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageReservationsAndCitizenIdHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CitizenIdHash",
                table: "Beneficiaries",
                type: "binary(32)",
                fixedLength: true,
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StorageReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageReservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Beneficiaries_VaultId_CitizenIdHash",
                table: "Beneficiaries",
                columns: new[] { "VaultId", "CitizenIdHash" },
                unique: true,
                filter: "[CitizenIdHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorageReservations_VaultId_Status_ExpiresAt",
                table: "StorageReservations",
                columns: new[] { "VaultId", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageReservations");

            migrationBuilder.DropIndex(
                name: "IX_Beneficiaries_VaultId_CitizenIdHash",
                table: "Beneficiaries");

            migrationBuilder.DropColumn(
                name: "CitizenIdHash",
                table: "Beneficiaries");
        }
    }
}

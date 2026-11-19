using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerIdentityCitizenIdHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CitizenIdHash",
                table: "OwnerIdentities",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerIdentities_CitizenIdHash",
                table: "OwnerIdentities",
                column: "CitizenIdHash",
                unique: true,
                filter: "[CitizenIdHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OwnerIdentities_CitizenIdHash",
                table: "OwnerIdentities");

            migrationBuilder.DropColumn(
                name: "CitizenIdHash",
                table: "OwnerIdentities");
        }
    }
}

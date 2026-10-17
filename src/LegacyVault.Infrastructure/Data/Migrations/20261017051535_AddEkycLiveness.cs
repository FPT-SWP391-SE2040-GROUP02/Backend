using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEkycLiveness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LivenessPassed",
                table: "EkycVerifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LivenessSessionId",
                table: "EkycVerifications",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LivenessPassed",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "LivenessSessionId",
                table: "EkycVerifications");
        }
    }
}

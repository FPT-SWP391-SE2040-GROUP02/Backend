using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeathRegistrationPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeathRegistrationPlace",
                table: "HandoverDocuments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeathRegistrationProvinceCode",
                table: "DeathCaseVersions",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeathRegistrationWardCode",
                table: "DeathCaseVersions",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeathRegistrationPlace",
                table: "HandoverDocuments");

            migrationBuilder.DropColumn(
                name: "DeathRegistrationProvinceCode",
                table: "DeathCaseVersions");

            migrationBuilder.DropColumn(
                name: "DeathRegistrationWardCode",
                table: "DeathCaseVersions");
        }
    }
}

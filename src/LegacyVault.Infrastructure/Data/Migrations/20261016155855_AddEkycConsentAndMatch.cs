using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEkycConsentAndMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BackSideChecked",
                table: "EkycVerifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "HandoverTurnId",
                table: "EkycVerifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityMatch",
                table: "EkycVerifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotChecked");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "EkycVerifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "OwnerEnrollment");

            migrationBuilder.CreateTable(
                name: "EkycConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NoticeVersion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EkycConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EkycConsents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EkycConsents_UserId_Purpose_ConsentedAt",
                table: "EkycConsents",
                columns: new[] { "UserId", "Purpose", "ConsentedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EkycConsents");

            migrationBuilder.DropColumn(
                name: "BackSideChecked",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "HandoverTurnId",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "IdentityMatch",
                table: "EkycVerifications");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "EkycVerifications");
        }
    }
}

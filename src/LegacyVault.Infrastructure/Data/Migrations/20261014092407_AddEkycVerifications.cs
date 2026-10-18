using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEkycVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EkycVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CitizenIdLast4 = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
                    DocumentToken = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    DocumentTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FaceMatchScore = table.Column<double>(type: "float", nullable: true),
                    SpoofCheckPassed = table.Column<bool>(type: "bit", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EkycVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EkycVerifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EkycVerifications_UserId_CreatedAt",
                table: "EkycVerifications",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EkycVerifications");
        }
    }
}

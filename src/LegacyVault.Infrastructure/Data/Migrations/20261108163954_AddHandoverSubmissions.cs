using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HandoverSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameMatches = table.Column<bool>(type: "bit", nullable: false),
                    CitizenIdMatches = table.Column<bool>(type: "bit", nullable: false),
                    DateOfBirthMatches = table.Column<bool>(type: "bit", nullable: false),
                    CitizenIdLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Acknowledged = table.Column<bool>(type: "bit", nullable: false),
                    VoluntaryDecline = table.Column<bool>(type: "bit", nullable: false),
                    FrontImageStorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FrontImageContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FrontImageSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FrontImageEncryptionKeyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FrontImageWrappedDataKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    BackImageStorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BackImageContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BackImageSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    BackImageEncryptionKeyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BackImageWrappedDataKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SupplementReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverSubmissions_HandoverTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "HandoverTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverSubmissions_Status",
                table: "HandoverSubmissions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverSubmissions_TurnId_CreatedAt",
                table: "HandoverSubmissions",
                columns: new[] { "TurnId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandoverSubmissions");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoverDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HandoverDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DateOfDeath = table.Column<DateOnly>(type: "date", nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CertificateIssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    RecipientFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientBirthYear = table.Column<int>(type: "int", nullable: true),
                    RecipientCitizenIdLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    RecipientSignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecipientTermsVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplementReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VerifierSignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifierChecks = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    VerifierTermsVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverDocuments_HandoverTurns_TurnId",
                        column: x => x.TurnId,
                        principalTable: "HandoverTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverDocuments_TurnId",
                table: "HandoverDocuments",
                column: "TurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverDocuments_VaultId_CreatedAt",
                table: "HandoverDocuments",
                columns: new[] { "VaultId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandoverDocuments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HandoverReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HandoverReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmitterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DataProtected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverReports_HandoverRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "HandoverRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HandoverReports_Users_SubmitterUserId",
                        column: x => x.SubmitterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HandoverReports_Users_VerifierUserId",
                        column: x => x.VerifierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HandoverReports_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HandoverReportFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverReportFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverReportFiles_HandoverReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "HandoverReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverReportFiles_ReportId",
                table: "HandoverReportFiles",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverReports_RecordId",
                table: "HandoverReports",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverReports_SubmitterUserId",
                table: "HandoverReports",
                column: "SubmitterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverReports_VaultId",
                table: "HandoverReports",
                column: "VaultId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverReports_VerifierUserId_Status",
                table: "HandoverReports",
                columns: new[] { "VerifierUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandoverReportFiles");

            migrationBuilder.DropTable(
                name: "HandoverReports");
        }
    }
}

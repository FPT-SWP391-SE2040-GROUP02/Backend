using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeathCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeathCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StatusBeforeCase = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CurrentVersionNo = table.Column<int>(type: "int", nullable: false),
                    ReviewVersionNo = table.Column<int>(type: "int", nullable: true),
                    NotifiedVersionNo = table.Column<int>(type: "int", nullable: true),
                    ObjectionStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ObjectionEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviousCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCases_DeathCases_PreviousCaseId",
                        column: x => x.PreviousCaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCases_HandoverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "HandoverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCases_Users_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCases_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndReason = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseAssignments_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseAssignments_Users_VerifierUserId",
                        column: x => x.VerifierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseNotices_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseNotices_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: true),
                    IdentityEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    OwnerCitizenIdLast4 = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: true),
                    DateOfDeath = table.Column<DateOnly>(type: "date", nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PlanConfigVersion = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseVersions_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CopiedFromFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseFiles_DeathCaseVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "DeathCaseVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseFlags_DeathCaseVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "DeathCaseVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseFlags_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseAssignments_VerifierUserId_AssignedAt",
                table: "DeathCaseAssignments",
                columns: new[] { "VerifierUserId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_DeathCaseAssignments_ActivePerCase",
                table: "DeathCaseAssignments",
                column: "CaseId",
                unique: true,
                filter: "[EndedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseFiles_StorageKey",
                table: "DeathCaseFiles",
                column: "StorageKey");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseFiles_VersionId",
                table: "DeathCaseFiles",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseFlags_CaseId",
                table: "DeathCaseFlags",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseFlags_VersionId_Kind",
                table: "DeathCaseFlags",
                columns: new[] { "VersionId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseNotices_CaseId",
                table: "DeathCaseNotices",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseNotices_RecipientUserId",
                table: "DeathCaseNotices",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseNotices_Status_CreatedAt",
                table: "DeathCaseNotices",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeathCases_OpenedByUserId",
                table: "DeathCases",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCases_PlanId",
                table: "DeathCases",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCases_PreviousCaseId",
                table: "DeathCases",
                column: "PreviousCaseId");

            migrationBuilder.CreateIndex(
                name: "UX_DeathCases_OpenPerVault",
                table: "DeathCases",
                column: "VaultId",
                unique: true,
                filter: "[Status] <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseVersions_CaseId_VersionNo",
                table: "DeathCaseVersions",
                columns: new[] { "CaseId", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeathCaseAssignments");

            migrationBuilder.DropTable(
                name: "DeathCaseFiles");

            migrationBuilder.DropTable(
                name: "DeathCaseFlags");

            migrationBuilder.DropTable(
                name: "DeathCaseNotices");

            migrationBuilder.DropTable(
                name: "DeathCaseVersions");

            migrationBuilder.DropTable(
                name: "DeathCases");
        }
    }
}

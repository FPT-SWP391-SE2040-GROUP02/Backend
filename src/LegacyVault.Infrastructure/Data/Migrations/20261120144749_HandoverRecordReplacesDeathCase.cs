using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HandoverRecordReplacesDeathCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Không còn điểm danh và hồ sơ chứng tử: kho đang chờ điểm danh hoặc đang mở hồ sơ chứng tử trở về Đã kích hoạt,
            // Người thực thi mở bàn giao lại theo luồng biên bản. Xóa tham số và mẫu thư của các phần đã gỡ.
            migrationBuilder.Sql("UPDATE [HandoverPlans] SET [Status] = 'Active' WHERE [Status] IN ('CheckinOverdue', 'AwaitingCheck', 'DeathCaseOpen');");
            migrationBuilder.Sql("DELETE FROM [SystemSettings] WHERE [Key] LIKE 'CheckIn.%' OR [Key] LIKE 'DeathCase.%' OR [Key] = 'Handover.DecisionWindowDays';");
            migrationBuilder.Sql("DELETE FROM [EmailTemplates] WHERE [Key] IN ('DOC_VERIFIER_TURN', 'DOC_RECIPIENT_RETURNED', 'DOC_COMPLETED', 'EXECUTOR_OVERDUE', 'EXECUTOR_OVERDUE_REMINDER', 'CHECKIN_DUE_FIRST', 'CHECKIN_DUE_SECOND', 'CHECKIN_LATE');");

            migrationBuilder.DropTable(
                name: "CheckInReminders");

            migrationBuilder.DropTable(
                name: "CheckInSchedules");

            migrationBuilder.DropTable(
                name: "DeathCaseDecisions");

            migrationBuilder.DropTable(
                name: "DeathCaseFiles");

            migrationBuilder.DropTable(
                name: "DeathCaseFlags");

            migrationBuilder.DropTable(
                name: "ExtraMailRules");

            migrationBuilder.DropTable(
                name: "ExtraMailSends");

            migrationBuilder.DropTable(
                name: "HandoverDocuments");

            migrationBuilder.DropTable(
                name: "HandoverTemplateSets");

            migrationBuilder.DropTable(
                name: "CheckInPeriods");

            migrationBuilder.DropTable(
                name: "DeathCaseAssignments");

            migrationBuilder.DropTable(
                name: "DeathCaseVersions");

            migrationBuilder.DropTable(
                name: "DeathCases");

            migrationBuilder.AddColumn<Guid>(
                name: "RecordId",
                table: "HandoverTurns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HandoverRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverRecords_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HandoverRecordLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverRecordLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverRecordLines_HandoverRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "HandoverRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HandoverRecordSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SignerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverRecordSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverRecordSignatures_HandoverRecords_RecordId",
                        column: x => x.RecordId,
                        principalTable: "HandoverRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecordLines_BeneficiaryId",
                table: "HandoverRecordLines",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecordLines_RecordId_BeneficiaryId_AssetId",
                table: "HandoverRecordLines",
                columns: new[] { "RecordId", "BeneficiaryId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecords_VaultId",
                table: "HandoverRecords",
                column: "VaultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecords_VerifierUserId_Status",
                table: "HandoverRecords",
                columns: new[] { "VerifierUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecordSignatures_RecordId_RevisionNo",
                table: "HandoverRecordSignatures",
                columns: new[] { "RecordId", "RevisionNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HandoverRecordLines");

            migrationBuilder.DropTable(
                name: "HandoverRecordSignatures");

            migrationBuilder.DropTable(
                name: "HandoverRecords");

            migrationBuilder.DropColumn(
                name: "RecordId",
                table: "HandoverTurns");

            migrationBuilder.CreateTable(
                name: "CheckInPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwaitingCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GraceEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LateReminderGapDays = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    MaxLateReminders = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    OverdueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProxyContactMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProxyContactedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProxyContactedWho = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedByExecutor = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInPeriods_HandoverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "HandoverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckInSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CycleDays = table.Column<int>(type: "int", nullable: false),
                    GraceDays = table.Column<int>(type: "int", nullable: false),
                    LastCheckInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PendingCycleDays = table.Column<int>(type: "int", nullable: true),
                    PendingGraceDays = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInSchedules_HandoverPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "HandoverPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CurrentVersionNo = table.Column<int>(type: "int", nullable: false),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OpenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewVersionNo = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StatusBeforeCase = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SupplementOverdueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "ExtraMailRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ButtonText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DaysFromDue = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraMailRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtraMailSends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraMailSends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HandoverDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CertificateIssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CertificateNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DateOfDeath = table.Column<DateOnly>(type: "date", nullable: true),
                    DeathRegistrationPlace = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, defaultValue: ""),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OwnerFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OwnerResidenceAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    RecipientBirthYear = table.Column<int>(type: "int", nullable: true),
                    RecipientCitizenIdLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    RecipientDeclaredProtected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    RecipientFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientResidenceAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    RecipientSignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecipientTermsVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplementReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TemplateVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerifierChecks = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    VerifierSignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifierTermsVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "HandoverTemplateSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AcceptJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeclineJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeclineNotice = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverTemplateSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CheckInReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckInReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckInReminders_CheckInPeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "CheckInPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndReason = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemindedDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "DeathCaseVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateOfDeath = table.Column<DateOnly>(type: "date", nullable: true),
                    DeathRegistrationProvinceCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    DeathRegistrationWardCode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    IdentityEncrypted = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    OwnerCitizenIdLast4 = table.Column<string>(type: "char(4)", unicode: false, fixedLength: true, maxLength: 4, nullable: true),
                    PlanConfigVersion = table.Column<int>(type: "int", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: true)
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
                name: "DeathCaseDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedParts = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VerifierUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeathCaseDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeathCaseDecisions_DeathCaseAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "DeathCaseAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseDecisions_DeathCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "DeathCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeathCaseDecisions_Users_VerifierUserId",
                        column: x => x.VerifierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeathCaseFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CopiedFromFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EncryptionKeyId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "DEATH_CERTIFICATE"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WrappedDataKey = table.Column<byte[]>(type: "binary(60)", fixedLength: true, maxLength: 60, nullable: false)
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "IX_CheckInPeriods_DueAt",
                table: "CheckInPeriods",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInPeriods_PlanId_Sequence",
                table: "CheckInPeriods",
                columns: new[] { "PlanId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CheckInPeriods_OpenPerPlan",
                table: "CheckInPeriods",
                column: "PlanId",
                unique: true,
                filter: "[ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckInReminders_PeriodId_Kind",
                table: "CheckInReminders",
                columns: new[] { "PeriodId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckInSchedules_PlanId",
                table: "CheckInSchedules",
                column: "PlanId",
                unique: true);

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
                name: "IX_DeathCaseDecisions_AssignmentId",
                table: "DeathCaseDecisions",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseDecisions_CaseId",
                table: "DeathCaseDecisions",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DeathCaseDecisions_VerifierUserId",
                table: "DeathCaseDecisions",
                column: "VerifierUserId");

            migrationBuilder.CreateIndex(
                name: "UX_DeathCaseDecisions_FinalPerCase",
                table: "DeathCaseDecisions",
                column: "CaseId",
                unique: true,
                filter: "[Kind] IN ('Rejected','Approved')");

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

            migrationBuilder.CreateIndex(
                name: "IX_ExtraMailSends_RuleId_PeriodId",
                table: "ExtraMailSends",
                columns: new[] { "RuleId", "PeriodId" },
                unique: true);

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
    }
}

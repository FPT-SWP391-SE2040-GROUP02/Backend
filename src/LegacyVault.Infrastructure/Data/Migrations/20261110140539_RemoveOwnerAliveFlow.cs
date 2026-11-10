using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOwnerAliveFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Luồng "Owner báo còn sống" đã bị gỡ: giấy chứng tử nhà nước là căn cứ cuối cùng nên không còn cửa sổ phản đối.
            // Các giá trị OwnerAlive không còn trong enum (lưu dạng chữ), nên dòng cũ mang giá trị này phải xóa trước,
            // nếu không ứng dụng sẽ lỗi khi đọc chúng. Nhật ký (AuditLogs) được giữ nguyên, các giá trị hành động cũ vẫn còn trong enum.
            migrationBuilder.Sql(@"
DELETE FROM [IncidentEvents] WHERE [IncidentId] IN (SELECT [Id] FROM [Incidents] WHERE [Type] = 'OwnerAlive');
DELETE FROM [Incidents] WHERE [Type] = 'OwnerAlive';
DELETE FROM [Holds] WHERE [Type] = 'OwnerAlive';
DELETE FROM [EkycConsents] WHERE [Purpose] = 'OwnerAlive';
DELETE FROM [EkycVerifications] WHERE [Purpose] = 'OwnerAlive';
-- Cài đặt của luồng đã gỡ (thời gian phản đối, số lần gửi thư báo Chủ kho), tránh còn dòng mồ côi trong trang cấu hình của Admin.
DELETE FROM [SystemSettings] WHERE [Key] IN ('DeathCase.ObjectionHours', 'DeathCase.MaxNoticeAttempts');");

            migrationBuilder.DropTable(
                name: "DeathCaseNotices");

            migrationBuilder.DropIndex(
                name: "IX_Holds_VaultId_Type_Scope_TargetId",
                table: "Holds");

            migrationBuilder.DropColumn(
                name: "NotifiedVersionNo",
                table: "DeathCases");

            migrationBuilder.DropColumn(
                name: "ObjectionEndsAt",
                table: "DeathCases");

            migrationBuilder.DropColumn(
                name: "ObjectionStartsAt",
                table: "DeathCases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NotifiedVersionNo",
                table: "DeathCases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObjectionEndsAt",
                table: "DeathCases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ObjectionStartsAt",
                table: "DeathCases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeathCaseNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionNo = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Holds_VaultId_Type_Scope_TargetId",
                table: "Holds",
                columns: new[] { "VaultId", "Type", "Scope", "TargetId" },
                unique: true,
                filter: "[Status] = 'Active' AND [Type] = 'OwnerAlive'");

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
        }
    }
}

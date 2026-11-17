using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeathCaseRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Các giá trị enum đã gỡ (Rejected, OwnerAlive) được lưu dạng chữ; nếu còn dòng dùng chúng thì ứng dụng
            // không đọc được. Dừng ở đây để xử lý tay thay vì xóa dữ liệu âm thầm.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM DeathCases WHERE Status = 'Rejected')
   OR EXISTS (SELECT 1 FROM DeathCaseDecisions WHERE Kind = 'Rejected')
   OR EXISTS (SELECT 1 FROM Holds WHERE [Type] = 'OwnerAlive')
   OR EXISTS (SELECT 1 FROM EkycVerifications WHERE Purpose = 'OwnerAlive')
   OR EXISTS (SELECT 1 FROM EkycConsents WHERE Purpose = 'OwnerAlive')
    THROW 50000, N'Còn dữ liệu của luồng từ chối hồ sơ chứng tử hoặc báo còn sống (Rejected, OwnerAlive). Xử lý các dòng này trước khi chạy migration.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_DeathCases_DeathCases_PreviousCaseId",
                table: "DeathCases");

            migrationBuilder.DropIndex(
                name: "IX_Holds_VaultId_Type_Scope_TargetId",
                table: "Holds");

            migrationBuilder.DropIndex(
                name: "IX_DeathCases_PreviousCaseId",
                table: "DeathCases");

            migrationBuilder.DropIndex(
                name: "UX_DeathCases_OpenPerVault",
                table: "DeathCases");

            migrationBuilder.DropIndex(
                name: "UX_DeathCaseDecisions_FinalPerCase",
                table: "DeathCaseDecisions");

            migrationBuilder.DropColumn(
                name: "PreviousCaseId",
                table: "DeathCases");

            migrationBuilder.CreateIndex(
                name: "UX_DeathCases_OpenPerVault",
                table: "DeathCases",
                column: "VaultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DeathCaseDecisions_FinalPerCase",
                table: "DeathCaseDecisions",
                column: "CaseId",
                unique: true,
                filter: "[Kind] = 'Approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_DeathCases_OpenPerVault",
                table: "DeathCases");

            migrationBuilder.DropIndex(
                name: "UX_DeathCaseDecisions_FinalPerCase",
                table: "DeathCaseDecisions");

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousCaseId",
                table: "DeathCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holds_VaultId_Type_Scope_TargetId",
                table: "Holds",
                columns: new[] { "VaultId", "Type", "Scope", "TargetId" },
                unique: true,
                filter: "[Status] = 'Active' AND [Type] = 'OwnerAlive'");

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
                name: "UX_DeathCaseDecisions_FinalPerCase",
                table: "DeathCaseDecisions",
                column: "CaseId",
                unique: true,
                filter: "[Kind] IN ('Rejected','Approved')");

            migrationBuilder.AddForeignKey(
                name: "FK_DeathCases_DeathCases_PreviousCaseId",
                table: "DeathCases",
                column: "PreviousCaseId",
                principalTable: "DeathCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

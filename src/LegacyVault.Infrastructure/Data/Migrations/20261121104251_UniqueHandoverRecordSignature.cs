using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueHandoverRecordSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HandoverRecordSignatures_RecordId_RevisionNo_Role_BeneficiaryId",
                table: "HandoverRecordSignatures",
                columns: new[] { "RecordId", "RevisionNo", "Role", "BeneficiaryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HandoverRecordSignatures_RecordId_RevisionNo_Role_BeneficiaryId",
                table: "HandoverRecordSignatures");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "VaultEntitlements",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "PaymentOrders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            // Dữ liệu cũ: điền mã gói từ hạng gốc để quyền dịch vụ và đơn đang chạy giữ đúng gói (FREE, XS, XS_MAX).
            migrationBuilder.Sql(@"
                UPDATE VaultEntitlements SET PlanCode = CASE Tier WHEN 'Xs' THEN 'XS' WHEN 'XsMax' THEN 'XS_MAX' ELSE 'FREE' END
                WHERE PlanCode IS NULL;
                UPDATE PaymentOrders SET PlanCode = CASE Tier WHEN 'Xs' THEN 'XS' WHEN 'XsMax' THEN 'XS_MAX' ELSE 'FREE' END
                WHERE PlanCode IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "VaultEntitlements");

            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "PaymentOrders");
        }
    }
}

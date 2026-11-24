using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBeneficiarySlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Gói không còn giới hạn số người nhận: bỏ các tham số Admin về suất người nhận
            // (Plans.Free.Slots, Plans.<Gói>.Slots, Plans.MaxTotalSlots) cùng hai cột suất ở kho và đơn thanh toán.
            migrationBuilder.Sql("DELETE FROM [SystemSettings] WHERE [Key] = 'Plans.MaxTotalSlots' OR ([Key] LIKE 'Plans.%.Slots');");

            migrationBuilder.DropColumn(
                name: "IncludedBeneficiarySlots",
                table: "VaultEntitlements");

            migrationBuilder.DropColumn(
                name: "IncludedBeneficiarySlots",
                table: "PaymentOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IncludedBeneficiarySlots",
                table: "VaultEntitlements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IncludedBeneficiarySlots",
                table: "PaymentOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}

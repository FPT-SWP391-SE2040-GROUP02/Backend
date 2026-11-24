using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveExtraSlotPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cấu hình Admin: bỏ giá suất và số suất tối đa mỗi đơn; hai khóa còn lại đổi tên bỏ đoạn Addon.
            migrationBuilder.Sql(
                "DELETE FROM SystemSettings WHERE [Key] IN ('Plans.Addon.SlotPricePerYear', 'Plans.Addon.MaxSlotsPerOrder');");
            migrationBuilder.Sql(
                "UPDATE SystemSettings SET [Key] = 'Plans.MaxTotalSlots' WHERE [Key] = 'Plans.Addon.MaxTotalSlots' "
                + "AND NOT EXISTS (SELECT 1 FROM SystemSettings WHERE [Key] = 'Plans.MaxTotalSlots');");
            migrationBuilder.Sql(
                "UPDATE SystemSettings SET [Key] = 'Plans.OrderTimeoutMinutes' WHERE [Key] = 'Plans.Addon.OrderTimeoutMinutes' "
                + "AND NOT EXISTS (SELECT 1 FROM SystemSettings WHERE [Key] = 'Plans.OrderTimeoutMinutes');");

            migrationBuilder.DropColumn(
                name: "ExtraBeneficiarySlots",
                table: "VaultEntitlements");

            migrationBuilder.DropColumn(
                name: "ExtraSlots",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "SlotAmount",
                table: "PaymentOrders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE SystemSettings SET [Key] = 'Plans.Addon.MaxTotalSlots' WHERE [Key] = 'Plans.MaxTotalSlots';");
            migrationBuilder.Sql(
                "UPDATE SystemSettings SET [Key] = 'Plans.Addon.OrderTimeoutMinutes' WHERE [Key] = 'Plans.OrderTimeoutMinutes';");

            migrationBuilder.AddColumn<int>(
                name: "ExtraBeneficiarySlots",
                table: "VaultEntitlements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExtraSlots",
                table: "PaymentOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SlotAmount",
                table: "PaymentOrders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}

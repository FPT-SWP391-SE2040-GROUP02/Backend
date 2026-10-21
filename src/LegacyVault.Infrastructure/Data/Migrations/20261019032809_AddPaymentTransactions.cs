using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ProviderTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    Gateway = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MatchedPaymentCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TransferType = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    TransferAmount = table.Column<long>(type: "bigint", nullable: false),
                    ReferenceCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TransactionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReconciliationReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PaymentOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentOrderId",
                table: "PaymentTransactions",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentOrderId_Matched",
                table: "PaymentTransactions",
                column: "PaymentOrderId",
                unique: true,
                filter: "[Status] = 'Matched'");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Provider_ProviderTransactionId",
                table: "PaymentTransactions",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status_ReceivedAt",
                table: "PaymentTransactions",
                columns: new[] { "Status", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentTransactions");
        }
    }
}

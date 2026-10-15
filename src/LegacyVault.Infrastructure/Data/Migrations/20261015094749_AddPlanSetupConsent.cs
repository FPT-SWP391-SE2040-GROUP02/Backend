using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanSetupConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivationPolicyAcknowledgedAt",
                table: "HandoverPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActivationPolicyVersion",
                table: "HandoverPlans",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedScopeVersion",
                table: "Executors",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvitationSentAt",
                table: "Executors",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivationPolicyAcknowledgedAt",
                table: "HandoverPlans");

            migrationBuilder.DropColumn(
                name: "ActivationPolicyVersion",
                table: "HandoverPlans");

            migrationBuilder.DropColumn(
                name: "AcceptedScopeVersion",
                table: "Executors");

            migrationBuilder.DropColumn(
                name: "InvitationSentAt",
                table: "Executors");
        }
    }
}

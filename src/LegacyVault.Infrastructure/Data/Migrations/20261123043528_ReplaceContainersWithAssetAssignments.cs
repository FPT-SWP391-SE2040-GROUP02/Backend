using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegacyVault.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceContainersWithAssetAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Beneficiaries_BeneficiaryId",
                        column: x => x.BeneficiaryId,
                        principalTable: "Beneficiaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssetId_BeneficiaryId",
                table: "AssetAssignments",
                columns: new[] { "AssetId", "BeneficiaryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_BeneficiaryId",
                table: "AssetAssignments",
                column: "BeneficiaryId");

            // Chuyển dữ liệu cũ: mỗi cặp (người nhận của gói, tài sản trong gói) thành một dòng gán.
            // Tài sản chỉ ở tối đa một gói và mỗi người chỉ có một chỉ định trong gói nên các cặp không trùng.
            migrationBuilder.Sql(@"
INSERT INTO AssetAssignments (Id, AssetId, BeneficiaryId, CreatedAt)
SELECT NEWID(), ca.AssetId, d.BeneficiaryId,
       CASE WHEN ca.AddedAt > d.CreatedAt THEN ca.AddedAt ELSE d.CreatedAt END
FROM Designations d
INNER JOIN Containers c ON c.Id = d.ContainerId AND c.Status <> 'Removed'
INNER JOIN ContainerAssets ca ON ca.ContainerId = c.Id;");

            // Bản chụp di chúc đã ký: đổi từ cặp gói - người nhận và gói - tài sản sang cặp tài sản - người nhận.
            migrationBuilder.Sql(@"
UPDATE WillVersions
SET SnapshotJson = N'{""assignments"":' + ISNULL((
    SELECT a.assetId AS assetId, r.beneficiaryId AS beneficiaryId
    FROM OPENJSON(WillVersions.SnapshotJson, '$.recipients')
         WITH (containerId uniqueidentifier '$.containerId', beneficiaryId uniqueidentifier '$.beneficiaryId') AS r
    INNER JOIN OPENJSON(WillVersions.SnapshotJson, '$.assets')
         WITH (containerId uniqueidentifier '$.containerId', assetId uniqueidentifier '$.assetId') AS a
         ON a.containerId = r.containerId
    FOR JSON PATH), N'[]') + N'}';");

            migrationBuilder.DropTable(
                name: "ContainerAssets");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "Containers");

            migrationBuilder.DropIndex(
                name: "UX_HandoverTurns_ActivePerDesignation",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "ContainerId",
                table: "HandoverTurns");

            migrationBuilder.DropColumn(
                name: "DesignationId",
                table: "HandoverTurns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetAssignments");

            migrationBuilder.AddColumn<Guid>(
                name: "ContainerId",
                table: "HandoverTurns",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DesignationId",
                table: "HandoverTurns",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Containers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Containers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Containers_Vaults_VaultId",
                        column: x => x.VaultId,
                        principalTable: "Vaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContainerAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainerAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContainerAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContainerAssets_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BeneficiaryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Designations_Beneficiaries_BeneficiaryId",
                        column: x => x.BeneficiaryId,
                        principalTable: "Beneficiaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Designations_Containers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_HandoverTurns_ActivePerDesignation",
                table: "HandoverTurns",
                column: "DesignationId",
                unique: true,
                filter: "[Status] IN ('Invited','InitialResponseExpired','IdentityInProgress','ReadyToAccept','HandoverCommitted')");

            migrationBuilder.CreateIndex(
                name: "IX_ContainerAssets_AssetId",
                table: "ContainerAssets",
                column: "AssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContainerAssets_ContainerId",
                table: "ContainerAssets",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_VaultId_Status",
                table: "Containers",
                columns: new[] { "VaultId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Designations_BeneficiaryId",
                table: "Designations",
                column: "BeneficiaryId");

            migrationBuilder.CreateIndex(
                name: "IX_Designations_ContainerId_BeneficiaryId",
                table: "Designations",
                columns: new[] { "ContainerId", "BeneficiaryId" },
                unique: true);
        }
    }
}

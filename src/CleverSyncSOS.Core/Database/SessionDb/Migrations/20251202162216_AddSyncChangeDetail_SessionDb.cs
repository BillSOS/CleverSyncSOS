using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncChangeDetail_SessionDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncChangeDetails",
                columns: table => new
                {
                    ChangeDetailId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SyncId = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FieldsChanged = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChangeDetails", x => x.ChangeDetailId);
                    table.ForeignKey(
                        name: "FK_SyncChangeDetails_SyncHistory_SyncId",
                        column: x => x.SyncId,
                        principalTable: "SyncHistory",
                        principalColumn: "SyncId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChangeDetails_ChangedAt",
                table: "SyncChangeDetails",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChangeDetails_ChangeType",
                table: "SyncChangeDetails",
                column: "ChangeType");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChangeDetails_SyncId_EntityType",
                table: "SyncChangeDetails",
                columns: new[] { "SyncId", "EntityType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncChangeDetails");
        }
    }
}

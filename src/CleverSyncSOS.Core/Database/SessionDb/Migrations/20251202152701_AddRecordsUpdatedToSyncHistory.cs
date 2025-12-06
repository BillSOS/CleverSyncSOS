using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordsUpdatedToSyncHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecordsUpdated",
                table: "SyncHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordsUpdated",
                table: "SyncHistory");
        }
    }
}

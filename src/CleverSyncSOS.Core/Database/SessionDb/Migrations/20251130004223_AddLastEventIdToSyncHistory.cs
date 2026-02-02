using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddLastEventIdToSyncHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add column only if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE name = 'LastEventId' AND object_id = OBJECT_ID('SyncHistory'))
                    ALTER TABLE SyncHistory ADD LastEventId nvarchar(max) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEventId",
                table: "SyncHistory");
        }
    }
}

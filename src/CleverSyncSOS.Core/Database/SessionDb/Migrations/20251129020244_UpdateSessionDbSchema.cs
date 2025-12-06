using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSessionDbSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop indexes if they exist
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Schools_SchoolPrefix' AND object_id = OBJECT_ID('Schools'))
                    DROP INDEX IX_Schools_SchoolPrefix ON Schools;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Districts_DistrictPrefix' AND object_id = OBJECT_ID('Districts'))
                    DROP INDEX IX_Districts_DistrictPrefix ON Districts;
            ");

            // Drop columns if they exist
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.columns WHERE name = 'KeyVaultConnectionStringSecretName' AND object_id = OBJECT_ID('Schools'))
                    ALTER TABLE Schools DROP COLUMN KeyVaultConnectionStringSecretName;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.columns WHERE name = 'SchoolPrefix' AND object_id = OBJECT_ID('Schools'))
                    ALTER TABLE Schools DROP COLUMN SchoolPrefix;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.columns WHERE name = 'DistrictPrefix' AND object_id = OBJECT_ID('Districts'))
                    ALTER TABLE Districts DROP COLUMN DistrictPrefix;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.columns WHERE name = 'KeyVaultSecretPrefix' AND object_id = OBJECT_ID('Districts'))
                    ALTER TABLE Districts DROP COLUMN KeyVaultSecretPrefix;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyVaultConnectionStringSecretName",
                table: "Schools",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolPrefix",
                table: "Schools",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DistrictPrefix",
                table: "Districts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeyVaultSecretPrefix",
                table: "Districts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schools_SchoolPrefix",
                table: "Schools",
                column: "SchoolPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_DistrictPrefix",
                table: "Districts",
                column: "DistrictPrefix");
        }
    }
}

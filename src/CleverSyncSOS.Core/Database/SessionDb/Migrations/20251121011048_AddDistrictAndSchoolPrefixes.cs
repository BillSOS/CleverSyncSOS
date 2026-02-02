using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleverSyncSOS.Core.Database.SessionDb.Migrations
{
    /// <inheritdoc />
    public partial class AddDistrictAndSchoolPrefixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add new columns as nullable first
            migrationBuilder.AddColumn<string>(
                name: "SchoolPrefix",
                table: "Schools",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistrictPrefix",
                table: "Districts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Step 2: Backfill DistrictPrefix from existing data
            // If KeyVaultSecretPrefix exists, use it; otherwise create from Name
            migrationBuilder.Sql(@"
                UPDATE Districts
                SET DistrictPrefix = CASE
                    WHEN KeyVaultSecretPrefix IS NOT NULL AND KeyVaultSecretPrefix <> ''
                    THEN KeyVaultSecretPrefix
                    ELSE REPLACE(REPLACE(Name, ' ', '-'), '''', '') + '-District'
                END
            ");

            // Step 3: Backfill SchoolPrefix from existing data
            // Extract prefix from KeyVaultConnectionStringSecretName or create from Name
            migrationBuilder.Sql(@"
                UPDATE Schools
                SET SchoolPrefix = CASE
                    WHEN KeyVaultConnectionStringSecretName IS NOT NULL AND KeyVaultConnectionStringSecretName <> ''
                    THEN REPLACE(KeyVaultConnectionStringSecretName, '-ConnectionString', '')
                    ELSE REPLACE(REPLACE(Name, ' ', '-'), '''', '')
                END
            ");

            // Step 4: Make columns non-nullable with default value
            migrationBuilder.AlterColumn<string>(
                name: "SchoolPrefix",
                table: "Schools",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "DistrictPrefix",
                table: "Districts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Step 5: Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_Schools_SchoolPrefix",
                table: "Schools",
                column: "SchoolPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_DistrictPrefix",
                table: "Districts",
                column: "DistrictPrefix");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Schools_SchoolPrefix",
                table: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_Districts_DistrictPrefix",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "SchoolPrefix",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DistrictPrefix",
                table: "Districts");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OsbApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAccuracy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "accuracy",
                table: "osm_features",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Minimal heuristic accuracy:
            // - start at 50 if there is at least one tag
            // - +30 if name exists
            // - +20 if has address (addr:* tags)
            migrationBuilder.Sql("""
                UPDATE osm_features
                SET accuracy =
                    (CASE WHEN tags IS NOT NULL AND tags::text <> '{}' THEN 50 ELSE 0 END) +
                    (CASE WHEN name IS NOT NULL AND length(name) > 0 THEN 30 ELSE 0 END) +
                    (CASE WHEN tags ? 'addr:street' OR tags ? 'addr:housenumber' THEN 20 ELSE 0 END)
                ;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accuracy",
                table: "osm_features");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace OsbApi.Migrations
{
    /// <inheritdoc />
    public partial class AddOsmIdAndGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert geometry -> geography in-place (meters-aware distance queries).
            migrationBuilder.Sql("""
                ALTER TABLE osm_features
                ALTER COLUMN geom
                TYPE geography(Point,4326)
                USING geom::geography;
                """);

            // Add osm_id and backfill from existing identity id (for already inserted test rows).
            migrationBuilder.AddColumn<long>(
                name: "osm_id",
                table: "osm_features",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE osm_features
                SET osm_id = id
                WHERE osm_id IS NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE osm_features
                ALTER COLUMN osm_id SET NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_osm_features_osm_id",
                table: "osm_features",
                column: "osm_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_osm_features_osm_id",
                table: "osm_features");

            migrationBuilder.DropColumn(
                name: "osm_id",
                table: "osm_features");

            migrationBuilder.Sql("""
                ALTER TABLE osm_features
                ALTER COLUMN geom
                TYPE geometry
                USING geom::geometry;
                """);
        }
    }
}

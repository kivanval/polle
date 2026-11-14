using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FarmHomePoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive and nullable: farms created before this migration keep working; the API requires a point
            // on every create/update and the map asks owners of older farms to place one.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.AddColumn<Point>(
                name: "home_point",
                schema: "public",
                table: "farms",
                type: "geometry(Point,4326)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_farms_home_point",
                schema: "public",
                table: "farms",
                sql: "home_point IS NULL OR (NOT ST_IsEmpty(home_point) AND ST_X(home_point) BETWEEN -180 AND 180 AND ST_Y(home_point) > -90 AND ST_Y(home_point) < 90)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_farms_home_point",
                schema: "public",
                table: "farms");

            migrationBuilder.DropColumn(
                name: "home_point",
                schema: "public",
                table: "farms");
        }
    }
}

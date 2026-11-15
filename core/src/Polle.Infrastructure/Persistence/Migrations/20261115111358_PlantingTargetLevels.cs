using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlantingTargetLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "fertilizer_target_percent",
                schema: "public",
                table: "field_plantings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "irrigation_target_percent",
                schema: "public",
                table: "field_plantings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_plantings_targets",
                schema: "public",
                table: "field_plantings",
                sql: "(irrigation_target_percent IS NULL OR (irrigation_mm IS NOT NULL AND irrigation_target_percent BETWEEN 0 AND 100)) AND (fertilizer_target_percent IS NULL OR (fertilizer_material_id IS NOT NULL AND fertilizer_target_percent BETWEEN 0 AND 100))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_field_plantings_targets",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropColumn(
                name: "fertilizer_target_percent",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropColumn(
                name: "irrigation_target_percent",
                schema: "public",
                table: "field_plantings");
        }
    }
}

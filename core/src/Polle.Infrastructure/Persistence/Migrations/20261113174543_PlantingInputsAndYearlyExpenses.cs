using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlantingInputsAndYearlyExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "fertilizer_material_id",
                schema: "public",
                table: "field_plantings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "fertilizer_rate_kg_ha",
                schema: "public",
                table: "field_plantings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "irrigation_mm",
                schema: "public",
                table: "field_plantings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "covers_year",
                schema: "public",
                table: "farm_expenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_plantings_fertilizer",
                schema: "public",
                table: "field_plantings",
                sql: "(fertilizer_material_id IS NULL AND fertilizer_rate_kg_ha IS NULL) OR (fertilizer_material_id IS NOT NULL AND fertilizer_rate_kg_ha > 0 AND fertilizer_rate_kg_ha <= 10000)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_plantings_irrigation",
                schema: "public",
                table: "field_plantings",
                sql: "irrigation_mm IS NULL OR irrigation_mm BETWEEN 0 AND 5000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_field_plantings_fertilizer",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_field_plantings_irrigation",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropColumn(
                name: "fertilizer_material_id",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropColumn(
                name: "fertilizer_rate_kg_ha",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropColumn(
                name: "irrigation_mm",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropColumn(
                name: "covers_year",
                schema: "public",
                table: "farm_expenses");
        }
    }
}

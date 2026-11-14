using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FieldConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "drought_level",
                schema: "public",
                table: "fields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "soil_drainage",
                schema: "public",
                table: "fields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "soil_texture",
                schema: "public",
                table: "fields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_drought_level",
                schema: "public",
                table: "fields",
                sql: "drought_level IS NULL OR drought_level IN ('drought_prone', 'moderate', 'low')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_soil_drainage",
                schema: "public",
                table: "fields",
                sql: "soil_drainage IS NULL OR soil_drainage IN ('well_drained', 'moderate', 'poor')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_soil_texture",
                schema: "public",
                table: "fields",
                sql: "soil_texture IS NULL OR soil_texture IN ('sand', 'loamy_sand', 'sandy_loam', 'loam', 'silt_loam', 'clay_loam', 'clay', 'peat')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_drought_level",
                schema: "public",
                table: "fields");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_soil_drainage",
                schema: "public",
                table: "fields");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_soil_texture",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "drought_level",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "soil_drainage",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "soil_texture",
                schema: "public",
                table: "fields");
        }
    }
}

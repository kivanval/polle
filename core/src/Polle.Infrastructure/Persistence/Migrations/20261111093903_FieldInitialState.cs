using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FieldInitialState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "initial_depletion_mm",
                schema: "public",
                table: "fields",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "initial_k_kg",
                schema: "public",
                table: "fields",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "initial_n_kg",
                schema: "public",
                table: "fields",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "initial_p_kg",
                schema: "public",
                table: "fields",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_initial_depletion_mm",
                schema: "public",
                table: "fields",
                sql: "initial_depletion_mm IS NULL OR initial_depletion_mm BETWEEN 0 AND 1000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_initial_k_kg",
                schema: "public",
                table: "fields",
                sql: "initial_k_kg IS NULL OR initial_k_kg BETWEEN 0 AND 2000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_initial_n_kg",
                schema: "public",
                table: "fields",
                sql: "initial_n_kg IS NULL OR initial_n_kg BETWEEN 0 AND 2000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_initial_p_kg",
                schema: "public",
                table: "fields",
                sql: "initial_p_kg IS NULL OR initial_p_kg BETWEEN 0 AND 2000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_initial_depletion_mm",
                schema: "public",
                table: "fields");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_initial_k_kg",
                schema: "public",
                table: "fields");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_initial_n_kg",
                schema: "public",
                table: "fields");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fields_initial_p_kg",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "initial_depletion_mm",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "initial_k_kg",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "initial_n_kg",
                schema: "public",
                table: "fields");

            migrationBuilder.DropColumn(
                name: "initial_p_kg",
                schema: "public",
                table: "fields");
        }
    }
}

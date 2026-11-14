using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StrategyCustomNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "public",
                table: "strategy_jobs",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                schema: "public",
                table: "strategy_jobs",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "public",
                table: "field_strategies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                schema: "public",
                table: "field_strategies",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                schema: "public",
                table: "strategy_jobs");

            migrationBuilder.DropColumn(
                name: "summary",
                schema: "public",
                table: "strategy_jobs");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "public",
                table: "field_strategies");

            migrationBuilder.DropColumn(
                name: "summary",
                schema: "public",
                table: "field_strategies");
        }
    }
}

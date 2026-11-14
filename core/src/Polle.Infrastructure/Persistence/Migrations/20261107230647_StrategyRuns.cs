using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StrategyRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_strategies",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    start_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    end_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    baseline_json = table.Column<string>(type: "jsonb", nullable: true),
                    saved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_strategies", x => x.id);
                    table.ForeignKey(
                        name: "fk_field_strategies_fields_field_id",
                        column: x => x.field_id,
                        principalSchema: "public",
                        principalTable: "fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "strategy_runs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    end_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_strategy_runs_fields_field_id",
                        column: x => x.field_id,
                        principalSchema: "public",
                        principalTable: "fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "strategy_jobs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_jobs", x => x.id);
                    table.CheckConstraint("ck_strategy_jobs_kind", "kind IN ('optimization', 'evaluation')");
                    table.CheckConstraint("ck_strategy_jobs_state", "state IN ('processing', 'ready', 'failed')");
                    table.ForeignKey(
                        name: "fk_strategy_jobs_strategy_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "public",
                        principalTable: "strategy_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_field_strategies_field_id_saved_at_utc",
                schema: "public",
                table: "field_strategies",
                columns: new[] { "field_id", "saved_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_strategy_jobs_run_id_key",
                schema: "public",
                table: "strategy_jobs",
                columns: new[] { "run_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strategy_runs_field_id_created_at_utc",
                schema: "public",
                table: "strategy_runs",
                columns: new[] { "field_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_strategies",
                schema: "public");

            migrationBuilder.DropTable(
                name: "strategy_jobs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "strategy_runs",
                schema: "public");
        }
    }
}

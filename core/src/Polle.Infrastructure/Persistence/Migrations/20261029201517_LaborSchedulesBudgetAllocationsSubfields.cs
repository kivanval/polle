using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaborSchedulesBudgetAllocationsSubfields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.AddColumn<Guid>(
                name: "parent_field_id",
                schema: "public",
                table: "fields",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "labor_category_id",
                schema: "public",
                table: "farm_operator_shifts",
                type: "integer",
                nullable: true);

            // Existing shifts keep labor_category_id NULL: they become the farm default week, which every labour type
            // without its own week follows. labor_categories is the data team's table; fresh databases get its original
            // shape so the reference below works everywhere. This migration never inserts or edits its rows.
            // Overlaps are now checked per labour type; NULL (the default week) is compared as 0, a serial id never used.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS public.labor_categories (
                    id serial PRIMARY KEY,
                    name varchar(50) NOT NULL UNIQUE);
                ALTER TABLE public.farm_operator_shifts
                    ADD CONSTRAINT fk_farm_operator_shifts_labor_categories_labor_category_id
                    FOREIGN KEY (labor_category_id) REFERENCES public.labor_categories (id) ON DELETE RESTRICT;
                ALTER TABLE public.farm_operator_shifts DROP CONSTRAINT ex_farm_operator_shifts_overlap;
                ALTER TABLE public.farm_operator_shifts
                ADD CONSTRAINT ex_farm_operator_shifts_overlap
                EXCLUDE USING gist (
                    farm_id WITH =,
                    (coalesce(labor_category_id, 0)) WITH =,
                    month WITH =,
                    day_of_week WITH =,
                    numrange(extract(epoch from start_time), extract(epoch from end_time), '[)') WITH &&
                );
                CREATE INDEX ix_farm_operator_shifts_labor_category_id ON public.farm_operator_shifts (labor_category_id);
                """);

            migrationBuilder.CreateTable(
                name: "farm_budget_allocations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_usd = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_budget_allocations", x => x.id);
                    table.CheckConstraint("ck_farm_budget_allocations_amount", "amount_usd >= 0");
                    table.CheckConstraint("ck_farm_budget_allocations_category", "category IN ('seeds', 'fertilizers', 'crop_protection', 'fuel', 'machinery', 'labor', 'irrigation', 'other')");
                    table.ForeignKey(
                        name: "fk_farm_budget_allocations_farms_farm_id",
                        column: x => x.farm_id,
                        principalSchema: "public",
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fields_parent_field_id",
                schema: "public",
                table: "fields",
                column: "parent_field_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fields_parent",
                schema: "public",
                table: "fields",
                sql: "parent_field_id IS NULL OR parent_field_id <> id");

            migrationBuilder.CreateIndex(
                name: "ix_farm_budget_allocations_farm_id_category",
                schema: "public",
                table: "farm_budget_allocations",
                columns: new[] { "farm_id", "category" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_fields_fields_parent_field_id",
                schema: "public",
                table: "fields",
                column: "parent_field_id",
                principalSchema: "public",
                principalTable: "fields",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Reverting would delete labour-type schedules, budget allocations and subfield links. Use a reviewed forward migration.");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FarmWorkersExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.CreateTable(
                name: "farm_workers",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    position = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    labor_category_id = table.Column<int>(type: "integer", nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    pay_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pay_rate_usd = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    hired_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_workers", x => x.id);
                    table.CheckConstraint("ck_farm_workers_name", "length(btrim(full_name)) > 0");
                    table.CheckConstraint("ck_farm_workers_pay_rate", "pay_rate_usd >= 0");
                    table.CheckConstraint("ck_farm_workers_pay_type", "pay_type IN ('hourly', 'monthly')");
                    table.ForeignKey(
                        name: "fk_farm_workers_farms_farm_id",
                        column: x => x.farm_id,
                        principalSchema: "public",
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "farm_expenses",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    unit_price_usd = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    amount_usd = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    supplier = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: true),
                    worker_id = table.Column<int>(type: "integer", nullable: true),
                    equipment_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_expenses", x => x.id);
                    table.CheckConstraint("ck_farm_expenses_amount", "amount_usd >= 0");
                    table.CheckConstraint("ck_farm_expenses_category", "category IN ('seeds', 'fertilizers', 'crop_protection', 'fuel', 'machinery', 'labor', 'irrigation', 'other')");
                    table.CheckConstraint("ck_farm_expenses_description", "length(btrim(description)) > 0");
                    table.CheckConstraint("ck_farm_expenses_quantity", "quantity IS NULL OR quantity > 0");
                    table.CheckConstraint("ck_farm_expenses_unit_price", "unit_price_usd IS NULL OR unit_price_usd >= 0");
                    table.ForeignKey(
                        name: "fk_farm_expenses_farm_fleet_equipment_id",
                        column: x => x.equipment_id,
                        principalSchema: "public",
                        principalTable: "farm_fleet",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_farm_expenses_farm_workers_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "public",
                        principalTable: "farm_workers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_farm_expenses_farms_farm_id",
                        column: x => x.farm_id,
                        principalSchema: "public",
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_farm_expenses_fields_field_id",
                        column: x => x.field_id,
                        principalSchema: "public",
                        principalTable: "fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "farm_worker_shifts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    worker_id = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_worker_shifts", x => x.id);
                    table.CheckConstraint("ck_farm_worker_shifts_day", "day_of_week BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_farm_worker_shifts_interval", "start_time < end_time");
                    table.CheckConstraint("ck_farm_worker_shifts_minutes", "extract(second from start_time) = 0 AND extract(second from end_time) = 0");
                    table.CheckConstraint("ck_farm_worker_shifts_month", "month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_farm_worker_shifts_farm_workers_worker_id",
                        column: x => x.worker_id,
                        principalSchema: "public",
                        principalTable: "farm_workers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_farm_expenses_equipment_id",
                schema: "public",
                table: "farm_expenses",
                column: "equipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_farm_expenses_farm_id_spent_on",
                schema: "public",
                table: "farm_expenses",
                columns: new[] { "farm_id", "spent_on" });

            migrationBuilder.CreateIndex(
                name: "ix_farm_expenses_field_id",
                schema: "public",
                table: "farm_expenses",
                column: "field_id");

            migrationBuilder.CreateIndex(
                name: "ix_farm_expenses_worker_id",
                schema: "public",
                table: "farm_expenses",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "ix_farm_worker_shifts_worker_id_month_day_of_week",
                schema: "public",
                table: "farm_worker_shifts",
                columns: new[] { "worker_id", "month", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_farm_workers_farm_id",
                schema: "public",
                table: "farm_workers",
                column: "farm_id");

            migrationBuilder.CreateIndex(
                name: "ix_farm_workers_labor_category_id",
                schema: "public",
                table: "farm_workers",
                column: "labor_category_id");

            // labor_categories is the data team's table, mapped by EF as a query, and exclusion constraints are not
            // modelled by EF: both are kept here. A removed labour type leaves its workers without one.
            migrationBuilder.Sql("""
                ALTER TABLE public.farm_workers
                    ADD CONSTRAINT fk_farm_workers_labor_categories_labor_category_id
                    FOREIGN KEY (labor_category_id) REFERENCES public.labor_categories (id) ON DELETE SET NULL;
                ALTER TABLE public.farm_worker_shifts
                ADD CONSTRAINT ex_farm_worker_shifts_overlap
                EXCLUDE USING gist (
                    worker_id WITH =,
                    month WITH =,
                    day_of_week WITH =,
                    numrange(extract(epoch from start_time), extract(epoch from end_time), '[)') WITH &&
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "farm_expenses",
                schema: "public");

            migrationBuilder.DropTable(
                name: "farm_worker_shifts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "farm_workers",
                schema: "public");
        }
    }
}

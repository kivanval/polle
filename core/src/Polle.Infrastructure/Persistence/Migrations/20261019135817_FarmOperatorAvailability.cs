using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FarmOperatorAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.AlterColumn<int>(
                name: "units_count",
                schema: "public",
                table: "farm_fleet",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.CreateTable(
                name: "farm_operator_shifts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farm_operator_shifts", x => x.id);
                    table.CheckConstraint("ck_farm_operator_shifts_day", "day_of_week BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_farm_operator_shifts_interval", "start_time < end_time");
                    table.CheckConstraint("ck_farm_operator_shifts_minutes", "extract(second from start_time) = 0 AND extract(second from end_time) = 0");
                    table.CheckConstraint("ck_farm_operator_shifts_month", "month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_farm_operator_shifts_farms_farm_id",
                        column: x => x.farm_id,
                        principalSchema: "public",
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_farm_operator_shifts_farm_id_month_day_of_week",
                schema: "public",
                table: "farm_operator_shifts",
                columns: new[] { "farm_id", "month", "day_of_week" });

            // Half-open intervals allow adjacent shifts. btree_gist is already installed by the agricultural domain migration.
            migrationBuilder.Sql("""
                ALTER TABLE public.farm_operator_shifts
                ADD CONSTRAINT ex_farm_operator_shifts_overlap
                EXCLUDE USING gist (
                    farm_id WITH =,
                    month WITH =,
                    day_of_week WITH =,
                    numrange(extract(epoch from start_time), extract(epoch from end_time), '[)') WITH &&
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Reverting would delete operator schedules and replace unknown equipment quantities with fabricated counts. Use a reviewed forward migration.");
        }
    }
}

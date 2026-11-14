using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FleetMonthlyHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Yearly hours become optional monthly limits (January first). Existing yearly values are kept as an even
            // split across the 12 months so no farmer input is lost; farmers can reshape it on the resources page.
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';
                ALTER TABLE public.farm_fleet ADD COLUMN monthly_hours_per_unit numeric(5,1)[];
                UPDATE public.farm_fleet
                   SET monthly_hours_per_unit = array_fill(round(annual_hours_per_unit / 12, 1), ARRAY[12])
                 WHERE annual_hours_per_unit IS NOT NULL;
                ALTER TABLE public.farm_fleet DROP CONSTRAINT ck_farm_fleet_hours, DROP COLUMN annual_hours_per_unit;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_farm_fleet_monthly_hours",
                schema: "public",
                table: "farm_fleet",
                sql: "monthly_hours_per_unit IS NULL OR (array_ndims(monthly_hours_per_unit) = 1 AND cardinality(monthly_hours_per_unit) = 12 AND array_position(monthly_hours_per_unit, NULL) IS NULL AND 0 <= ALL (monthly_hours_per_unit) AND 744 >= ALL (monthly_hours_per_unit))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Monthly limits cannot be folded back into one yearly value without losing their shape; write a reviewed forward migration instead.");
        }
    }
}

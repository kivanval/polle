using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FarmResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // public.farm_fleet and public.equipment_categories were created by the data team outside EF. Fresh databases get
            // them in that original shape first, so the adaptation below runs identically everywhere. Category rows are the
            // data team's to load; this migration never inserts or edits them.
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';
                CREATE TABLE IF NOT EXISTS public.equipment_categories (
                    id serial PRIMARY KEY,
                    slug varchar(100) NOT NULL UNIQUE,
                    name varchar(255) NOT NULL,
                    description text);
                CREATE TABLE IF NOT EXISTS public.farm_fleet (
                    id serial PRIMARY KEY,
                    farm_id bigint NOT NULL,
                    equipment_category_id integer NOT NULL REFERENCES public.equipment_categories (id),
                    custom_name varchar(100),
                    units_count integer NOT NULL DEFAULT 1);
                LOCK TABLE public.farm_fleet IN ACCESS EXCLUSIVE MODE;
                DO $preflight$
                BEGIN
                    IF EXISTS (SELECT 1 FROM public.farm_fleet) THEN
                        RAISE EXCEPTION 'farm_fleet already has rows keyed by bigint farm ids; map them to public.farms before migrating. No rows were changed.';
                    END IF;
                END $preflight$;
                """);

            // farm_id becomes the application's farm uuid. The table is empty (checked above), so no values are converted.
            migrationBuilder.Sql("""
                ALTER TABLE public.farm_fleet ALTER COLUMN farm_id TYPE uuid USING NULL;
                ALTER TABLE public.farm_fleet
                    ADD COLUMN annual_hours_per_unit numeric(6,1),
                    ADD COLUMN created_at_utc timestamp with time zone NOT NULL DEFAULT now(),
                    ADD COLUMN updated_at_utc timestamp with time zone NOT NULL DEFAULT now(),
                    ADD CONSTRAINT fk_farm_fleet_farms_farm_id FOREIGN KEY (farm_id) REFERENCES public.farms (id) ON DELETE CASCADE,
                    ADD CONSTRAINT ck_farm_fleet_units CHECK (units_count BETWEEN 1 AND 1000),
                    ADD CONSTRAINT ck_farm_fleet_hours CHECK (annual_hours_per_unit IS NULL OR (annual_hours_per_unit > 0 AND annual_hours_per_unit <= 8784));
                CREATE INDEX ix_farm_fleet_farm_id ON public.farm_fleet (farm_id);
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "budget_usd",
                schema: "public",
                table: "farms",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_farms_budget",
                schema: "public",
                table: "farms",
                sql: "budget_usd IS NULL OR budget_usd >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Reverting farm resources would drop budgets and fleet hours and cannot restore bigint farm ids; write a reviewed forward migration instead.");
        }
    }
}

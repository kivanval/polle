using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlantingPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lock before preflight so another writer cannot introduce ambiguous dates between check and DDL.
            // Do not synthesize dates, close periods, remove rows or reinterpret legacy crop identifiers.
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';
                SET LOCAL search_path = public;
                LOCK TABLE public.field_plantings IN ACCESS EXCLUSIVE MODE;
                DO $preflight$
                BEGIN
                    IF EXISTS (SELECT 1 FROM public.field_plantings
                        WHERE planting_date IS NULL
                           OR planting_date NOT BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'
                           OR (harvesting_date IS NOT NULL AND
                               (harvesting_date < planting_date OR harvesting_date NOT BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'))) THEN
                        RAISE EXCEPTION 'Planting periods need owner-reviewed dates before migration; no rows were changed.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM public.field_plantings a JOIN public.field_plantings b
                        ON a.field_id = b.field_id AND a.id < b.id
                        WHERE daterange(a.planting_date, a.harvesting_date, '[]') && daterange(b.planting_date, b.harvesting_date, '[]')) THEN
                        RAISE EXCEPTION 'Overlapping planting periods need owner review before migration; no rows were changed.';
                    END IF;
                END $preflight$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_field_plantings_dates",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            // Raw DDL deliberately avoids EF's generated null-to-default UPDATE.
            migrationBuilder.Sql("ALTER TABLE public.field_plantings ALTER COLUMN planting_date SET NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_plantings_dates",
                schema: "public",
                table: "field_plantings",
                sql: "harvesting_date IS NULL OR harvesting_date >= planting_date");

            migrationBuilder.AddCheckConstraint(
                name: "ck_field_plantings_finite_dates",
                schema: "public",
                table: "field_plantings",
                sql: "planting_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31' AND (harvesting_date IS NULL OR harvesting_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31')");

            migrationBuilder.Sql("""
                ALTER TABLE public.field_plantings
                ADD CONSTRAINT ex_field_plantings_period EXCLUDE USING gist
                    (field_id WITH =, daterange(planting_date, harvesting_date, '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Removing the growing-period invariant requires a reviewed forward migration. The shared btree_gist extension must not be dropped.");
        }
    }
}

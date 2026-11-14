using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgriculturalDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT 1 FROM lakehouse.crops) THEN
                        RAISE EXCEPTION 'Crop source is empty; review catalog before migrating.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM lakehouse.crops WHERE crop_id IS NULL OR crop_id = '' OR crop_id <> btrim(crop_id) OR length(crop_id) > 100 OR crop IS NULL OR btrim(crop) = '')
                       OR EXISTS (SELECT crop_id FROM lakehouse.crops GROUP BY crop_id HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Crop source contains invalid or duplicate identifiers/names; no catalog imported.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "crops",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_ua = table.Column<string>(type: "text", nullable: true),
                    scientific_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crops", x => x.id);
                });

            // Scientific tables remain untouched. Preserve their IDs in the operational snapshot.
            migrationBuilder.Sql("""
                INSERT INTO public.crops (id, name, name_ua, scientific_name)
                SELECT crop_id, crop, "Crop_UA", "Crop_Latin" FROM lakehouse.crops;
                """);

            migrationBuilder.CreateTable(
                name: "farms",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farms", x => x.id);
                    table.CheckConstraint("ck_farms_name", "length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "fk_farms_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fields",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    boundary = table.Column<Polygon>(type: "geometry(Polygon,4326)", nullable: false),
                    area_hectares = table.Column<double>(type: "double precision", nullable: false, computedColumnSql: "ST_Area(boundary::geography) / 10000.0", stored: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fields", x => x.id);
                    table.CheckConstraint("ck_fields_area", "area_hectares > 0 AND area_hectares < 'Infinity'::float8");
                    table.CheckConstraint("ck_fields_boundary", "NOT ST_IsEmpty(boundary) AND ST_IsValid(boundary) AND ST_NumInteriorRings(boundary) = 0 AND ST_NPoints(boundary) BETWEEN 4 AND 501 AND ST_XMin(Box3D(boundary)) >= -180 AND ST_XMax(Box3D(boundary)) <= 180 AND ST_YMin(Box3D(boundary)) > -90 AND ST_YMax(Box3D(boundary)) < 90 AND ST_XMax(Box3D(boundary)) - ST_XMin(Box3D(boundary)) < 180");
                    table.CheckConstraint("ck_fields_name", "length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "fk_fields_farms_farm_id",
                        column: x => x.farm_id,
                        principalSchema: "public",
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "field_plantings",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crop_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    season_year = table.Column<int>(type: "integer", nullable: false),
                    planting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    harvesting_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_plantings", x => x.id);
                    table.CheckConstraint("ck_field_plantings_dates", "harvesting_date IS NULL OR planting_date IS NULL OR harvesting_date >= planting_date");
                    table.CheckConstraint("ck_field_plantings_year", "season_year BETWEEN 1 AND 9999");
                    table.ForeignKey(
                        name: "fk_field_plantings_crops_crop_id",
                        column: x => x.crop_id,
                        principalSchema: "public",
                        principalTable: "crops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_field_plantings_fields_field_id",
                        column: x => x.field_id,
                        principalSchema: "public",
                        principalTable: "fields",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_farms_owner_user_id",
                schema: "public",
                table: "farms",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_field_plantings_crop_id",
                schema: "public",
                table: "field_plantings",
                column: "crop_id");

            migrationBuilder.CreateIndex(
                name: "ix_field_plantings_field_id_season_year",
                schema: "public",
                table: "field_plantings",
                columns: new[] { "field_id", "season_year" });

            migrationBuilder.CreateIndex(
                name: "ix_fields_boundary",
                schema: "public",
                table: "fields",
                column: "boundary")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_fields_farm_id",
                schema: "public",
                table: "fields",
                column: "farm_id");
        }

        // Refuse destructive rollback on a shared database. Use a reviewed forward migration.
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Agricultural data rollback requires a reviewed forward migration; tables and PostGIS will not be dropped.");
    }
}

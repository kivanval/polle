using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CropProfilePlantings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "crop_profile_id",
                schema: "public",
                table: "field_plantings",
                type: "bigint",
                nullable: true);

            // Map each lakehouse crop to the solver catalog by scientific name (sugar beet by hand), then pick the profile
            // whose season matches the lakehouse id, otherwise the main spring profile, otherwise the lowest id.
            migrationBuilder.Sql("""
                WITH lake AS (
                    SELECT l.crop_id, coalesce(
                        (SELECT c.id FROM public.crops c WHERE lower(c.scientific_name) = lower(l."Crop_Latin") ORDER BY c.id LIMIT 1),
                        CASE l.crop_id WHEN 'sugar_beet' THEN 7725 END) AS public_crop
                    FROM lakehouse.crops l)
                UPDATE public.field_plantings p SET crop_profile_id = (
                    SELECT cp.id FROM public.crop_profiles cp JOIN lake ON cp.crop_id = lake.public_crop
                    WHERE lake.crop_id = p.crop_id
                    ORDER BY (p.crop_id LIKE 'winter%' AND cp.system_name ILIKE 'Winter%Grain%') DESC,
                             (cp.system_name ~* '^Spring .*(Grain|Main|Standard|Full Season|Dry Bean|Bulb|Fresh Market)') DESC,
                             cp.id
                    LIMIT 1);

                DO $check$
                BEGIN
                    IF EXISTS (SELECT 1 FROM public.field_plantings WHERE crop_profile_id IS NULL) THEN
                        RAISE EXCEPTION 'Some plantings have a crop with no solver crop profile (e.g. cabbage); map them by hand first. No rows were changed.';
                    END IF;
                END $check$;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "crop_profile_id",
                schema: "public",
                table: "field_plantings",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            // public.crop_profiles is the data team's table, mapped through a query, so EF does not emit this key.
            migrationBuilder.Sql("""
                ALTER TABLE public.field_plantings ADD CONSTRAINT fk_field_plantings_crop_profiles_crop_profile_id
                    FOREIGN KEY (crop_profile_id) REFERENCES public.crop_profiles (id) ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_field_plantings_crop_profile_id",
                schema: "public",
                table: "field_plantings",
                column: "crop_profile_id");

            // Expand step only: crop_id stays (now nullable) so the previously deployed API keeps reading plantings until the
            // new one is live. The new API never reads or writes it; a later migration drops it once every deploy has moved on.
            migrationBuilder.Sql("ALTER TABLE public.field_plantings ALTER COLUMN crop_id DROP NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Plantings moved to solver crop profiles; going back to lakehouse ids needs a reviewed forward migration.");
        }
    }
}

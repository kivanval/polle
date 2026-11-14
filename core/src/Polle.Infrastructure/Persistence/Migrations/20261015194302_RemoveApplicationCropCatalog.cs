using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveApplicationCropCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drops only the application-owned duplicate catalog. Scientific schemas are only read.
            migrationBuilder.Sql("""
                SET LOCAL lock_timeout = '5s';
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM public.field_plantings) THEN
                        RAISE EXCEPTION 'public.field_plantings contains planting history; review crop references before removing public.crops.';
                    END IF;
                    IF EXISTS (SELECT id, name, name_ua, scientific_name FROM public.crops
                               EXCEPT SELECT crop_id, crop, "Crop_UA", "Crop_Latin" FROM lakehouse.crops) THEN
                        RAISE EXCEPTION 'public.crops contains entries not reproducible from lakehouse.crops; catalog not removed.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE confrelid = 'public.crops'::regclass AND conname <> 'fk_field_plantings_crops_crop_id') THEN
                        RAISE EXCEPTION 'Unexpected foreign keys reference public.crops; catalog not removed.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_field_plantings_crops_crop_id",
                schema: "public",
                table: "field_plantings");

            migrationBuilder.DropTable(
                name: "crops",
                schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            // Restores the snapshot exactly as AgriculturalDomain seeded it.
            migrationBuilder.Sql("""
                INSERT INTO public.crops (id, name, name_ua, scientific_name)
                SELECT crop_id, crop, "Crop_UA", "Crop_Latin" FROM lakehouse.crops;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_field_plantings_crops_crop_id",
                schema: "public",
                table: "field_plantings",
                column: "crop_id",
                principalSchema: "public",
                principalTable: "crops",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

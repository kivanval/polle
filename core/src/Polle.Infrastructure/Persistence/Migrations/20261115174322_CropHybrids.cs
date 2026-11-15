using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Polle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CropHybrids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crop_hybrids",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    crop_id = table.Column<long>(type: "bigint", nullable: false),
                    crop_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    maturity_group = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    drought_tolerance = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    heat_tolerance = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    key_traits = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    recommended_conditions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crop_hybrids", x => x.id);
                    table.CheckConstraint("ck_crop_hybrids_drought_tolerance", "drought_tolerance IN ('high', 'medium', 'low')");
                    table.CheckConstraint("ck_crop_hybrids_heat_tolerance", "heat_tolerance IN ('high', 'medium', 'low')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_crop_hybrids_crop_id_name",
                schema: "public",
                table: "crop_hybrids",
                columns: new[] { "crop_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crop_hybrids_crop_profile_id",
                schema: "public",
                table: "crop_hybrids",
                column: "crop_profile_id");

            // public.crops and public.crop_profiles are the data team's tables, mapped through queries, so EF does not emit
            // these keys. A hybrid belongs to its crop: removing a crop from the catalog removes its hybrids, and removing a
            // profile only widens the hybrid to the whole crop.
            migrationBuilder.Sql("""
                ALTER TABLE public.crop_hybrids ADD CONSTRAINT fk_crop_hybrids_crops_crop_id
                    FOREIGN KEY (crop_id) REFERENCES public.crops (id) ON DELETE CASCADE;
                ALTER TABLE public.crop_hybrids ADD CONSTRAINT fk_crop_hybrids_crop_profiles_crop_profile_id
                    FOREIGN KEY (crop_profile_id) REFERENCES public.crop_profiles (id) ON DELETE SET NULL;
                """);

            // Hybrids and varieties sold in Ukraine and neighbouring markets, matched to the catalog by scientific name so
            // the ids of the data team's rows are never hard-coded. A crop missing from the catalog is skipped. Winter
            // wheat and winter rapeseed point at the crop's winter profile when there is one. Tolerances are the breeders'
            // published ratings rounded to high, medium or low; they are advice, not solver inputs.
            migrationBuilder.Sql("""
                INSERT INTO public.crop_hybrids (crop_id, crop_profile_id, name, maturity_group, drought_tolerance, heat_tolerance,
                    key_traits, recommended_conditions)
                SELECT c.id,
                    CASE WHEN h.winter THEN (SELECT p.id FROM public.crop_profiles p
                        WHERE p.crop_id = c.id AND p.system_name ILIKE 'Winter%' ORDER BY p.id LIMIT 1) END,
                    h.name, h.maturity_group, h.drought, h.heat, h.traits, h.conditions
                FROM (VALUES
                    ('Zea mays', false, 'P9241', 'FAO 340', 'high', 'medium',
                        'Mid-early dent hybrid with a deep root system, stable yield under moisture stress and fast grain dry-down.',
                        'Steppe and forest-steppe; rainfed fields with a moderate summer moisture deficit.'),
                    ('Zea mays', false, 'DKC 4351', 'FAO 350', 'medium', 'medium',
                        'Mid-early hybrid with strong early vigour and stalks that stand well; pays back fertile soil and good moisture.',
                        'Forest-steppe and irrigated steppe; fields with reliable moisture.'),
                    ('Zea mays', false, 'SY Torino', 'FAO 290', 'high', 'medium',
                        'Early hybrid that flowers before the hottest weeks and dries down fast, which lowers drying costs.',
                        'Northern forest-steppe and Polissia, or steppe fields where an early harvest avoids late-summer drought.'),
                    ('Zea mays', false, 'DKC 3730', 'FAO 280', 'medium', 'medium',
                        'Early hybrid with quick dry-down and good cold tolerance at emergence.',
                        'Polissia and northern forest-steppe; early sowing on cool soils.'),
                    ('Helianthus annuus', false, 'NK Neoma', 'Mid-early', 'high', 'high',
                        'Clearfield herbicide tolerance, broomrape resistance (races A–E) and stable yield in dry seasons.',
                        'Southern and eastern steppe; broomrape-infested fields under the Clearfield system.'),
                    ('Helianthus annuus', false, 'P64LE25', 'Mid-early', 'high', 'medium',
                        'Express Sun (tribenuron) herbicide tolerance, good broomrape resistance and high oil content.',
                        'Steppe and forest-steppe; fields with heavy broadleaf weed pressure.'),
                    ('Helianthus annuus', false, 'LG 50.479 SX', 'Mid-early', 'medium', 'medium',
                        'Express Sun herbicide tolerance, quick early growth and stable oil yield.',
                        'Forest-steppe and northern steppe.'),
                    ('Glycine max', false, 'ES Mentor', 'MG 00 (early)', 'medium', 'medium',
                        'Very early, high-protein soybean that matures before late-summer drought and clears the field for a winter crop.',
                        'Forest-steppe and Polissia; fields where winter wheat follows.'),
                    ('Glycine max', false, 'Adelfia', 'MG 0 (mid-early)', 'medium', 'medium',
                        'Ukrainian variety that resists pod shattering and lodging and stays stable on moderately dry fields.',
                        'Forest-steppe and northern steppe.'),
                    ('Triticum aestivum', true, 'Bohdana', 'Mid-early', 'high', 'high',
                        'Ukrainian steppe winter wheat with high drought tolerance, good winter hardiness and good grain quality.',
                        'Southern and central steppe; rainfed fields after a dry autumn.'),
                    ('Triticum aestivum', true, 'Podolyanka', 'Mid-season', 'medium', 'medium',
                        'Broadly adapted Ukrainian winter wheat with good winter hardiness and stable bread-making quality.',
                        'Forest-steppe and Polissia; also the northern steppe.'),
                    ('Sorghum bicolor', false, 'Buster', 'Early', 'high', 'high',
                        'Sorghum hybrid with strong drought and heat tolerance; needs less water than maize.',
                        'Dry steppe; fields where maize suffers from summer drought.'),
                    ('Hordeum vulgare', false, 'Planet', 'Mid-season (spring)', 'medium', 'medium',
                        'Spring malting barley with high yield and plump grain.',
                        'Forest-steppe and Polissia; fertile fields with reliable spring rain.'),
                    ('Brassica napus', true, 'DK Exception', 'Mid-early (winter)', 'medium', 'medium',
                        'Winter rapeseed hybrid with strong autumn vigour and good winter hardiness.',
                        'Forest-steppe and western regions; fields with enough autumn moisture to establish.'),
                    ('Solanum tuberosum', false, 'Riviera', 'Very early', 'high', 'high',
                        'Very early table potato that tolerates heat and drought; ready in about 45–55 days.',
                        'South and steppe; an early harvest before summer heat.'),
                    ('Solanum tuberosum', false, 'Bellarosa', 'Early', 'medium', 'medium',
                        'Early red-skinned table potato with large tubers that copes with short dry spells.',
                        'Forest-steppe and Polissia; light and medium soils.'),
                    ('Pisum sativum', false, 'Astronaute', 'Mid-early', 'medium', 'medium',
                        'Semi-leafless spring pea that stands well for an easy harvest.',
                        'Forest-steppe and northern steppe.')
                ) AS h(scientific_name, winter, name, maturity_group, drought, heat, traits, conditions)
                JOIN LATERAL (SELECT id FROM public.crops WHERE scientific_name = h.scientific_name ORDER BY id LIMIT 1) c ON true
                ON CONFLICT (crop_id, name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Dropping crop_hybrids loses its rows; remove it with a reviewed forward migration.");
        }
    }
}

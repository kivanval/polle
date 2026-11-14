using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FieldPlantingConfiguration : IEntityTypeConfiguration<FieldPlanting>
{
    public void Configure(EntityTypeBuilder<FieldPlanting> builder)
    {
        builder.ToTable("field_plantings", "public", table =>
        {
            table.HasCheckConstraint("ck_field_plantings_year", "season_year BETWEEN 1 AND 9999");
            table.HasCheckConstraint("ck_field_plantings_dates", "harvesting_date IS NULL OR harvesting_date >= planting_date");
            table.HasCheckConstraint("ck_field_plantings_irrigation", "irrigation_mm IS NULL OR irrigation_mm BETWEEN 0 AND 5000");
            table.HasCheckConstraint("ck_field_plantings_fertilizer",
                "(fertilizer_material_id IS NULL AND fertilizer_rate_kg_ha IS NULL) OR (fertilizer_material_id IS NOT NULL AND fertilizer_rate_kg_ha > 0 AND fertilizer_rate_kg_ha <= 10000)");
            table.HasCheckConstraint("ck_field_plantings_finite_dates", "planting_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31' AND (harvesting_date IS NULL OR harvesting_date BETWEEN DATE '0001-01-01' AND DATE '9999-12-31')");
        });
        builder.HasKey(planting => planting.Id);
        builder.Property(planting => planting.Notes).HasMaxLength(4000);
        builder.HasOne(planting => planting.Field).WithMany().HasForeignKey(planting => planting.FieldId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(planting => planting.CropProfile).WithMany().HasForeignKey(planting => planting.CropProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(planting => new { planting.FieldId, planting.SeasonYear });
        // The database also holds ex_field_plantings_period, written in SQL:
        // EXCLUDE USING gist (field_id WITH =, daterange(planting_date, harvesting_date, '[]') WITH &&).
        // EF does not model PostgreSQL exclusion constraints, so a migration must keep it.
    }
}

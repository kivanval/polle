using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FieldConfiguration : IEntityTypeConfiguration<Field>
{
    public void Configure(EntityTypeBuilder<Field> builder)
    {
        builder.ToTable("fields", "public", table =>
        {
            table.HasCheckConstraint("ck_fields_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_fields_boundary", "NOT ST_IsEmpty(boundary) AND ST_IsValid(boundary) AND ST_NumInteriorRings(boundary) = 0 AND ST_NPoints(boundary) BETWEEN 4 AND 501 AND ST_XMin(Box3D(boundary)) >= -180 AND ST_XMax(Box3D(boundary)) <= 180 AND ST_YMin(Box3D(boundary)) > -90 AND ST_YMax(Box3D(boundary)) < 90 AND ST_XMax(Box3D(boundary)) - ST_XMin(Box3D(boundary)) < 180");
            table.HasCheckConstraint("ck_fields_area", "area_hectares > 0 AND area_hectares < 'Infinity'::float8");
            table.HasCheckConstraint("ck_fields_parent", "parent_field_id IS NULL OR parent_field_id <> id");
            table.HasCheckConstraint("ck_fields_drought_level", $"drought_level IS NULL OR drought_level IN ({Quoted(FieldConditions.DroughtLevels)})");
            table.HasCheckConstraint("ck_fields_soil_drainage", $"soil_drainage IS NULL OR soil_drainage IN ({Quoted(FieldConditions.Drainages)})");
            table.HasCheckConstraint("ck_fields_soil_texture", $"soil_texture IS NULL OR soil_texture IN ({Quoted(FieldConditions.Textures)})");
            table.HasCheckConstraint("ck_fields_initial_n_kg", $"initial_n_kg IS NULL OR initial_n_kg BETWEEN 0 AND {FieldConditions.MaxInitialNutrientKg}");
            table.HasCheckConstraint("ck_fields_initial_p_kg", $"initial_p_kg IS NULL OR initial_p_kg BETWEEN 0 AND {FieldConditions.MaxInitialNutrientKg}");
            table.HasCheckConstraint("ck_fields_initial_k_kg", $"initial_k_kg IS NULL OR initial_k_kg BETWEEN 0 AND {FieldConditions.MaxInitialNutrientKg}");
            table.HasCheckConstraint("ck_fields_initial_depletion_mm", $"initial_depletion_mm IS NULL OR initial_depletion_mm BETWEEN 0 AND {FieldConditions.MaxInitialDepletionMm}");
        });
        builder.Property(field => field.DroughtLevel).HasMaxLength(20);
        builder.Property(field => field.SoilDrainage).HasMaxLength(20);
        builder.Property(field => field.SoilTexture).HasMaxLength(20);
        builder.HasKey(field => field.Id);
        builder.Property(field => field.Name).HasMaxLength(150).IsRequired();
        builder.Property(field => field.Description).HasMaxLength(2000);
        builder.Property(field => field.Boundary).HasColumnType("geometry(Polygon,4326)").IsRequired();
        builder.Property(field => field.AreaHectares).HasComputedColumnSql("ST_Area(boundary::geography) / 10000.0", stored: true);
        builder.HasOne(field => field.Farm).WithMany().HasForeignKey(field => field.FarmId).OnDelete(DeleteBehavior.Restrict);
        // A parent with subfields cannot be deleted; the API asks to delete the subfields first.
        builder.HasOne<Field>().WithMany().HasForeignKey(field => field.ParentFieldId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(field => field.FarmId);
        builder.HasIndex(field => field.ParentFieldId);
        builder.HasIndex(field => field.Boundary).HasMethod("gist");
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(value => $"'{value}'"));
}

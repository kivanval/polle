using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

/// <summary>
/// Reads the solver's public.crops catalog. Mapped to a query, not a table, so migrations never touch the data team's
/// table; names come from the jsonb translations.
/// </summary>
public sealed class CropConfiguration : IEntityTypeConfiguration<Crop>
{
    public void Configure(EntityTypeBuilder<Crop> builder)
    {
        builder.ToSqlQuery("""
            SELECT id, coalesce(name->>'en', scientific_name, id::text) AS name, name->>'uk' AS name_uk, scientific_name
            FROM public.crops
            """);
        builder.HasKey(crop => crop.Id);
        builder.HasMany(crop => crop.Profiles).WithOne(profile => profile.Crop).HasForeignKey(profile => profile.CropId);
    }
}

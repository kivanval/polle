using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

/// <summary>
/// Hybrids and varieties per catalog crop. The table is the application's own; its keys to public.crops and
/// public.crop_profiles (the data team's tables, mapped through queries) are added by the migration in SQL.
/// </summary>
public sealed class CropHybridConfiguration : IEntityTypeConfiguration<CropHybrid>
{
    public void Configure(EntityTypeBuilder<CropHybrid> builder)
    {
        builder.ToTable("crop_hybrids", "public", table =>
        {
            table.HasCheckConstraint("ck_crop_hybrids_drought_tolerance", "drought_tolerance IN ('high', 'medium', 'low')");
            table.HasCheckConstraint("ck_crop_hybrids_heat_tolerance", "heat_tolerance IN ('high', 'medium', 'low')");
        });
        builder.HasKey(hybrid => hybrid.Id);
        builder.Property(hybrid => hybrid.Name).HasMaxLength(120);
        builder.Property(hybrid => hybrid.MaturityGroup).HasMaxLength(40);
        builder.Property(hybrid => hybrid.DroughtTolerance).HasMaxLength(8);
        builder.Property(hybrid => hybrid.HeatTolerance).HasMaxLength(8);
        builder.Property(hybrid => hybrid.KeyTraits).HasMaxLength(500);
        builder.Property(hybrid => hybrid.RecommendedConditions).HasMaxLength(500);
        builder.HasOne(hybrid => hybrid.Crop).WithMany().HasForeignKey(hybrid => hybrid.CropId);
        builder.HasIndex(hybrid => new { hybrid.CropId, hybrid.Name }).IsUnique();
        builder.HasIndex(hybrid => hybrid.CropProfileId);
    }
}

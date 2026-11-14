using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

/// <summary>Reads the solver's public.crop_profiles through a query, so migrations never alter the data team's table.</summary>
public sealed class CropProfileConfiguration : IEntityTypeConfiguration<CropProfile>
{
    public void Configure(EntityTypeBuilder<CropProfile> builder)
    {
        builder.ToSqlQuery("SELECT id, crop_id, system_name, is_perennial FROM public.crop_profiles");
        builder.HasKey(profile => profile.Id);
    }
}

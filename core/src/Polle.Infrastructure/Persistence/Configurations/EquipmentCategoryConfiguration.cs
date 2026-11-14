using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

/// <summary>Reads the data team's equipment_categories. Mapped to a query so migrations never alter it.</summary>
public sealed class EquipmentCategoryConfiguration : IEntityTypeConfiguration<EquipmentCategory>
{
    public void Configure(EntityTypeBuilder<EquipmentCategory> builder)
    {
        builder.ToSqlQuery("SELECT id, slug, name, description FROM public.equipment_categories");
        builder.HasKey(category => category.Id);
    }
}

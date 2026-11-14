using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

/// <summary>Reads the data team's labor_categories. Mapped to a query so migrations never alter it.</summary>
public sealed class LaborCategoryConfiguration : IEntityTypeConfiguration<LaborCategory>
{
    public void Configure(EntityTypeBuilder<LaborCategory> builder)
    {
        builder.ToSqlQuery("SELECT id, name FROM public.labor_categories");
        builder.HasKey(category => category.Id);
    }
}

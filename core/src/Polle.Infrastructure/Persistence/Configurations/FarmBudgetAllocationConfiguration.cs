using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FarmBudgetAllocationConfiguration : IEntityTypeConfiguration<FarmBudgetAllocation>
{
    public void Configure(EntityTypeBuilder<FarmBudgetAllocation> builder)
    {
        builder.ToTable("farm_budget_allocations", "public", table =>
        {
            table.HasCheckConstraint("ck_farm_budget_allocations_category",
                "category IN (" + string.Join(", ", FarmBudgetAllocation.Categories.Select(category => $"'{category}'")) + ")");
            table.HasCheckConstraint("ck_farm_budget_allocations_amount", "amount_usd >= 0");
        });
        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.Id).UseSerialColumn();
        builder.Property(allocation => allocation.Category).HasMaxLength(30).IsRequired();
        builder.Property(allocation => allocation.AmountUsd).HasPrecision(14, 2);
        builder.HasOne(allocation => allocation.Farm).WithMany().HasForeignKey(allocation => allocation.FarmId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(allocation => new { allocation.FarmId, allocation.Category }).IsUnique();
    }
}

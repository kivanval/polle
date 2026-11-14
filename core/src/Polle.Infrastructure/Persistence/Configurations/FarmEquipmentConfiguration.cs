using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

/// <summary>
/// public.farm_fleet, shared with the data team: uuid farm_id, monthly hour limits and timestamps. Its FK to
/// equipment_categories lives in the database as SQL, since equipment_categories is mapped as a query.
/// </summary>
public sealed class FarmEquipmentConfiguration : IEntityTypeConfiguration<FarmEquipment>
{
    public void Configure(EntityTypeBuilder<FarmEquipment> builder)
    {
        builder.ToTable("farm_fleet", "public", table =>
        {
            table.HasCheckConstraint("ck_farm_fleet_units", "units_count BETWEEN 1 AND 1000");
            table.HasCheckConstraint("ck_farm_fleet_monthly_hours", "monthly_hours_per_unit IS NULL OR (array_ndims(monthly_hours_per_unit) = 1 AND cardinality(monthly_hours_per_unit) = 12 AND array_position(monthly_hours_per_unit, NULL) IS NULL AND 0 <= ALL (monthly_hours_per_unit) AND 744 >= ALL (monthly_hours_per_unit))");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).UseSerialColumn();
        builder.Property(item => item.CustomName).HasMaxLength(100);
        builder.Property(item => item.UnitsCount).IsRequired(false);
        builder.Property(item => item.MonthlyHoursPerUnit).HasColumnType("numeric(5,1)[]");
        builder.HasOne(item => item.Farm).WithMany().HasForeignKey(item => item.FarmId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => item.FarmId);
    }
}

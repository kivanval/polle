using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FarmOperatorShiftConfiguration : IEntityTypeConfiguration<FarmOperatorShift>
{
    public void Configure(EntityTypeBuilder<FarmOperatorShift> builder)
    {
        builder.ToTable("farm_operator_shifts", "public", table =>
        {
            table.HasCheckConstraint("ck_farm_operator_shifts_month", "month BETWEEN 1 AND 12");
            table.HasCheckConstraint("ck_farm_operator_shifts_day", "day_of_week BETWEEN 1 AND 7");
            table.HasCheckConstraint("ck_farm_operator_shifts_interval", "start_time < end_time");
            table.HasCheckConstraint("ck_farm_operator_shifts_minutes", "extract(second from start_time) = 0 AND extract(second from end_time) = 0");
        });
        builder.HasKey(shift => shift.Id);
        builder.Property(shift => shift.Id).UseSerialColumn();
        builder.Property(shift => shift.StartTime).HasColumnType("time without time zone");
        builder.Property(shift => shift.EndTime).HasColumnType("time without time zone");
        builder.HasOne(shift => shift.Farm).WithMany().HasForeignKey(shift => shift.FarmId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(shift => new { shift.FarmId, shift.Month, shift.DayOfWeek });
        // labor_category_id references the data team's public.labor_categories; that FK and the per-type exclusion
        // constraint ex_farm_operator_shifts_overlap live in the database as SQL; EF models neither.
    }
}

using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FarmWorkerShiftConfiguration : IEntityTypeConfiguration<FarmWorkerShift>
{
    public void Configure(EntityTypeBuilder<FarmWorkerShift> builder)
    {
        builder.ToTable("farm_worker_shifts", "public", table =>
        {
            table.HasCheckConstraint("ck_farm_worker_shifts_month", "month BETWEEN 1 AND 12");
            table.HasCheckConstraint("ck_farm_worker_shifts_day", "day_of_week BETWEEN 1 AND 7");
            table.HasCheckConstraint("ck_farm_worker_shifts_interval", "start_time < end_time");
            table.HasCheckConstraint("ck_farm_worker_shifts_minutes", "extract(second from start_time) = 0 AND extract(second from end_time) = 0");
        });
        builder.HasKey(shift => shift.Id);
        builder.Property(shift => shift.Id).UseSerialColumn();
        builder.Property(shift => shift.StartTime).HasColumnType("time without time zone");
        builder.Property(shift => shift.EndTime).HasColumnType("time without time zone");
        builder.HasOne(shift => shift.Worker).WithMany().HasForeignKey(shift => shift.WorkerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(shift => new { shift.WorkerId, shift.Month, shift.DayOfWeek });
        // ex_farm_worker_shifts_overlap (no overlapping shifts of one worker on one weekday of a month) lives in the
        // database as SQL; EF does not model exclusion constraints.
    }
}

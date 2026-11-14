using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FarmWorkerConfiguration : IEntityTypeConfiguration<FarmWorker>
{
    public void Configure(EntityTypeBuilder<FarmWorker> builder)
    {
        builder.ToTable("farm_workers", "public", table =>
        {
            table.HasCheckConstraint("ck_farm_workers_name", "length(btrim(full_name)) > 0");
            table.HasCheckConstraint("ck_farm_workers_pay_type",
                "pay_type IN (" + string.Join(", ", FarmWorker.PayTypes.Select(type => $"'{type}'")) + ")");
            table.HasCheckConstraint("ck_farm_workers_pay_rate", "pay_rate_usd >= 0");
        });
        builder.HasKey(worker => worker.Id);
        builder.Property(worker => worker.Id).UseSerialColumn();
        builder.Property(worker => worker.FullName).HasMaxLength(150).IsRequired();
        builder.Property(worker => worker.Position).HasMaxLength(100);
        builder.Property(worker => worker.Phone).HasMaxLength(40);
        builder.Property(worker => worker.PayType).HasMaxLength(20).IsRequired();
        builder.Property(worker => worker.PayRateUsd).HasPrecision(12, 2);
        builder.Property(worker => worker.Notes).HasMaxLength(2000);
        builder.HasOne(worker => worker.Farm).WithMany().HasForeignKey(worker => worker.FarmId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(worker => worker.FarmId);
        // labor_category_id references the data team's public.labor_categories (mapped as a query); its FK lives in
        // the database as SQL.
        builder.HasIndex(worker => worker.LaborCategoryId);
    }
}

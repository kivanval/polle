using Polle.Domain.Agriculture;
using Polle.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FarmConfiguration : IEntityTypeConfiguration<Farm>
{
    public void Configure(EntityTypeBuilder<Farm> builder)
    {
        builder.ToTable("farms", "public", table =>
        {
            table.HasCheckConstraint("ck_farms_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_farms_budget", "budget_usd IS NULL OR budget_usd >= 0");
            table.HasCheckConstraint("ck_farms_home_point", "home_point IS NULL OR (NOT ST_IsEmpty(home_point) AND ST_X(home_point) BETWEEN -180 AND 180 AND ST_Y(home_point) > -90 AND ST_Y(home_point) < 90)");
        });
        builder.HasKey(farm => farm.Id);
        builder.Property(farm => farm.Name).HasMaxLength(150).IsRequired();
        builder.Property(farm => farm.Description).HasMaxLength(2000);
        builder.Property(farm => farm.Location).HasMaxLength(500);
        builder.Property(farm => farm.HomePoint).HasColumnType("geometry(Point,4326)");
        builder.Property(farm => farm.BudgetUsd).HasPrecision(14, 2);
        builder.HasOne<User>().WithMany().HasForeignKey(farm => farm.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(farm => farm.OwnerUserId);
    }
}

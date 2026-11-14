using Polle.Domain.Agriculture;
using Polle.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class PlanningYearConfiguration : IEntityTypeConfiguration<PlanningYear>
{
    public void Configure(EntityTypeBuilder<PlanningYear> builder)
    {
        builder.ToTable("planning_years", "public", table => table.HasCheckConstraint("ck_planning_years_year", "year BETWEEN 1900 AND 2200"));
        // One row per user and year: the key itself prevents duplicates.
        builder.HasKey(item => new { item.UserId, item.Year });
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

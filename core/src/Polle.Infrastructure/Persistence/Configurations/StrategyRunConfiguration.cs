using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class StrategyRunConfiguration : IEntityTypeConfiguration<StrategyRun>
{
    public void Configure(EntityTypeBuilder<StrategyRun> builder)
    {
        builder.ToTable("strategy_runs", "public");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.StartMonth).HasMaxLength(7);
        builder.Property(run => run.EndMonth).HasMaxLength(7);
        builder.HasOne(run => run.Field).WithMany().HasForeignKey(run => run.FieldId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(run => run.Jobs).WithOne().HasForeignKey(job => job.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(run => new { run.FieldId, run.CreatedAtUtc });
    }
}

public sealed class StrategyJobConfiguration : IEntityTypeConfiguration<StrategyJob>
{
    public void Configure(EntityTypeBuilder<StrategyJob> builder)
    {
        builder.ToTable("strategy_jobs", "public", table =>
        {
            table.HasCheckConstraint("ck_strategy_jobs_state", "state IN ('processing', 'ready', 'failed')");
            table.HasCheckConstraint("ck_strategy_jobs_kind", "kind IN ('optimization', 'evaluation')");
        });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Key).HasMaxLength(40);
        builder.Property(job => job.Name).HasMaxLength(StrategyText.NameMaxLength);
        builder.Property(job => job.Summary).HasMaxLength(StrategyText.SummaryMaxLength);
        builder.Property(job => job.Kind).HasMaxLength(20);
        builder.Property(job => job.State).HasMaxLength(20);
        builder.Property(job => job.TaskId).HasMaxLength(100);
        builder.Property(job => job.ResultJson).HasColumnType("jsonb");
        builder.Property(job => job.Error).HasMaxLength(2000);
        builder.HasIndex(job => new { job.RunId, job.Key }).IsUnique();
    }
}

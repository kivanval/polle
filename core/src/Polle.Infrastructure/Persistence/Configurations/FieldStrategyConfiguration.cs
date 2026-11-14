using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FieldStrategyConfiguration : IEntityTypeConfiguration<FieldStrategy>
{
    public void Configure(EntityTypeBuilder<FieldStrategy> builder)
    {
        builder.ToTable("field_strategies", "public");
        builder.HasKey(strategy => strategy.Id);
        builder.Property(strategy => strategy.Key).HasMaxLength(40);
        builder.Property(strategy => strategy.Name).HasMaxLength(StrategyText.NameMaxLength);
        builder.Property(strategy => strategy.Summary).HasMaxLength(StrategyText.SummaryMaxLength);
        builder.Property(strategy => strategy.StartMonth).HasMaxLength(7);
        builder.Property(strategy => strategy.EndMonth).HasMaxLength(7);
        builder.Property(strategy => strategy.ResultJson).HasColumnType("jsonb");
        builder.Property(strategy => strategy.BaselineJson).HasColumnType("jsonb");
        builder.HasOne(strategy => strategy.Field).WithMany().HasForeignKey(strategy => strategy.FieldId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(strategy => new { strategy.FieldId, strategy.SavedAtUtc });
    }
}

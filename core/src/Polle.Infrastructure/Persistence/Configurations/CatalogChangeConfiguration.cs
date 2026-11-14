using Polle.Domain.Catalog;
using Polle.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class CatalogChangeConfiguration : IEntityTypeConfiguration<CatalogChange>
{
    public void Configure(EntityTypeBuilder<CatalogChange> builder)
    {
        builder.ToTable("catalog_changes", "public", table =>
            table.HasCheckConstraint("ck_catalog_changes_action", "action IN ('insert', 'update', 'delete')"));
        builder.HasKey(change => change.Id);
        builder.Property(change => change.TableName).HasMaxLength(63).IsRequired();
        builder.Property(change => change.Action).HasMaxLength(10).IsRequired();
        builder.Property(change => change.RowKey).HasColumnType("jsonb").IsRequired();
        builder.Property(change => change.Before).HasColumnType("jsonb");
        builder.Property(change => change.After).HasColumnType("jsonb");
        builder.Property(change => change.Cascade).HasColumnType("jsonb");
        // Keep the history if an account is ever removed.
        builder.HasOne<User>().WithMany().HasForeignKey(change => change.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(change => new { change.TableName, change.ChangedAtUtc });
        builder.HasIndex(change => change.ChangedAtUtc);
    }
}

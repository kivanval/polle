using Polle.Domain.Agriculture;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Polle.Infrastructure.Persistence.Configurations;

public sealed class FarmExpenseConfiguration : IEntityTypeConfiguration<FarmExpense>
{
    public void Configure(EntityTypeBuilder<FarmExpense> builder)
    {
        builder.ToTable("farm_expenses", "public", table =>
        {
            table.HasCheckConstraint("ck_farm_expenses_category",
                "category IN (" + string.Join(", ", FarmBudgetAllocation.Categories.Select(category => $"'{category}'")) + ")");
            table.HasCheckConstraint("ck_farm_expenses_amount", "amount_usd >= 0");
            table.HasCheckConstraint("ck_farm_expenses_quantity", "quantity IS NULL OR quantity > 0");
            table.HasCheckConstraint("ck_farm_expenses_unit_price", "unit_price_usd IS NULL OR unit_price_usd >= 0");
            table.HasCheckConstraint("ck_farm_expenses_description", "length(btrim(description)) > 0");
        });
        builder.HasKey(expense => expense.Id);
        builder.Property(expense => expense.Id).UseSerialColumn();
        builder.Property(expense => expense.Category).HasMaxLength(30).IsRequired();
        builder.Property(expense => expense.Description).HasMaxLength(200).IsRequired();
        builder.Property(expense => expense.Quantity).HasPrecision(14, 3);
        builder.Property(expense => expense.Unit).HasMaxLength(20);
        builder.Property(expense => expense.UnitPriceUsd).HasPrecision(14, 4);
        builder.Property(expense => expense.AmountUsd).HasPrecision(14, 2);
        builder.Property(expense => expense.Supplier).HasMaxLength(150);
        builder.Property(expense => expense.DocumentNumber).HasMaxLength(60);
        builder.Property(expense => expense.Notes).HasMaxLength(2000);
        builder.HasOne(expense => expense.Farm).WithMany().HasForeignKey(expense => expense.FarmId).OnDelete(DeleteBehavior.Cascade);
        // Links are optional context; removing a field, worker or machine keeps the money on record.
        builder.HasOne<Field>().WithMany().HasForeignKey(expense => expense.FieldId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<FarmWorker>().WithMany().HasForeignKey(expense => expense.WorkerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<FarmEquipment>().WithMany().HasForeignKey(expense => expense.EquipmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(expense => new { expense.FarmId, expense.SpentOn });
        builder.HasIndex(expense => expense.FieldId);
        builder.HasIndex(expense => expense.WorkerId);
        builder.HasIndex(expense => expense.EquipmentId);
    }
}

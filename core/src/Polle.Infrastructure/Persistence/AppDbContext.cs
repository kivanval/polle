using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Polle.Domain.Users;
using Polle.Domain.Agriculture;
using Polle.Domain.Catalog;

namespace Polle.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Farm> Farms => Set<Farm>();
    public DbSet<Field> Fields => Set<Field>();
    public DbSet<FieldPlanting> FieldPlantings => Set<FieldPlanting>();
    public DbSet<Crop> Crops => Set<Crop>();
    public DbSet<CropProfile> CropProfiles => Set<CropProfile>();
    public DbSet<CropHybrid> CropHybrids => Set<CropHybrid>();
    public DbSet<StrategyRun> StrategyRuns => Set<StrategyRun>();
    public DbSet<StrategyJob> StrategyJobs => Set<StrategyJob>();
    public DbSet<FieldStrategy> FieldStrategies => Set<FieldStrategy>();
    public DbSet<FarmEquipment> FarmEquipment => Set<FarmEquipment>();
    public DbSet<FarmOperatorShift> FarmOperatorShifts => Set<FarmOperatorShift>();
    public DbSet<EquipmentCategory> EquipmentCategories => Set<EquipmentCategory>();
    public DbSet<LaborCategory> LaborCategories => Set<LaborCategory>();
    public DbSet<FarmBudgetAllocation> FarmBudgetAllocations => Set<FarmBudgetAllocation>();
    public DbSet<FarmWorker> FarmWorkers => Set<FarmWorker>();
    public DbSet<FarmWorkerShift> FarmWorkerShifts => Set<FarmWorkerShift>();
    public DbSet<FarmExpense> FarmExpenses => Set<FarmExpense>();
    public DbSet<PlanningYear> PlanningYears => Set<PlanningYear>();
    public DbSet<CatalogChange> CatalogChanges => Set<CatalogChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

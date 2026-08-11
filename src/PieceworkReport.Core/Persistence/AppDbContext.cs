using Microsoft.EntityFrameworkCore;

namespace PieceworkReport.Core.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SecurityAuditEntry> SecurityAuditEntries => Set<SecurityAuditEntry>();
    public DbSet<SchemaVersionRecord> SchemaVersions => Set<SchemaVersionRecord>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialSpecification> MaterialSpecifications => Set<MaterialSpecification>();
    public DbSet<MachineSpecification> MachineSpecifications => Set<MachineSpecification>();
    public DbSet<WagePeriod> WagePeriods => Set<WagePeriod>();
    public DbSet<WagePeriodWorkday> WagePeriodWorkdays => Set<WagePeriodWorkday>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<EmployeePricingOverride> EmployeePricingOverrides => Set<EmployeePricingOverride>();
    public DbSet<ProductionRecord> ProductionRecords => Set<ProductionRecord>();
    public DbSet<PayAdjustment> PayAdjustments => Set<PayAdjustment>();
    public DbSet<ExportSnapshot> ExportSnapshots => Set<ExportSnapshot>();
    public DbSet<CodeSequence> CodeSequences => Set<CodeSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<Employee>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Machine>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Material>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<MaterialSpecification>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<MaterialSpecification>().HasIndex(x => new { x.MaterialId, x.Description }).IsUnique();
        modelBuilder.Entity<MachineSpecification>().HasIndex(x => new { x.MachineId, x.MaterialSpecificationId }).IsUnique();
        modelBuilder.Entity<WagePeriod>().HasIndex(x => new { x.Year, x.Month }).IsUnique();
        modelBuilder.Entity<WagePeriodWorkday>().HasIndex(x => new { x.WagePeriodId, x.WorkDate }).IsUnique();
        modelBuilder.Entity<PricingRule>().HasIndex(x => new { x.WagePeriodId, x.MachineSpecificationId }).IsUnique();
        modelBuilder.Entity<EmployeePricingOverride>().HasIndex(x => new { x.PricingRuleId, x.EmployeeId }).IsUnique();
        modelBuilder.Entity<ProductionRecord>().HasIndex(x => new { x.WagePeriodId, x.WorkDate, x.EmployeeId, x.MachineId, x.MaterialSpecificationId }).IsUnique();
        modelBuilder.Entity<ExportSnapshot>().HasIndex(x => new { x.WagePeriodId, x.Version }).IsUnique();

        ConfigureDecimal<WagePeriod>(modelBuilder, nameof(WagePeriod.Budget), 18, 2);
        ConfigureDecimal<Material>(modelBuilder, nameof(Material.LegacyBuckleCount), 18, 3);
        ConfigureDecimal<MaterialSpecification>(modelBuilder, nameof(MaterialSpecification.BuckleCount), 18, 3);
        ConfigureDecimal<PricingRule>(modelBuilder, nameof(PricingRule.TargetDailyWage), 18, 6);
        ConfigureDecimal<PricingRule>(modelBuilder, nameof(PricingRule.DefaultTargetBuckleCount), 18, 3);
        ConfigureDecimal<PricingRule>(modelBuilder, nameof(PricingRule.StandardDailyPieces), 18, 3);
        ConfigureDecimal<PricingRule>(modelBuilder, nameof(PricingRule.DirectPieceRate), 18, 6);
        ConfigureDecimal<EmployeePricingOverride>(modelBuilder, nameof(EmployeePricingOverride.TargetBuckleCount), 18, 3);
        ConfigureDecimal<ProductionRecord>(modelBuilder, nameof(ProductionRecord.Quantity), 18, 3);
        ConfigureDecimal<PayAdjustment>(modelBuilder, nameof(PayAdjustment.Amount), 18, 2);
        ConfigureDecimal<ExportSnapshot>(modelBuilder, nameof(ExportSnapshot.PieceworkTotal), 18, 2);
        ConfigureDecimal<ExportSnapshot>(modelBuilder, nameof(ExportSnapshot.AdjustmentTotal), 18, 2);

        modelBuilder.Entity<MaterialSpecification>().HasOne(x => x.Material).WithMany(x => x.Specifications).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MachineSpecification>().HasOne(x => x.Machine).WithMany(x => x.Specifications).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MachineSpecification>().HasOne(x => x.MaterialSpecification).WithMany(x => x.Machines).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WagePeriodWorkday>().HasOne(x => x.WagePeriod).WithMany(x => x.Workdays).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PricingRule>().HasOne(x => x.MachineSpecification).WithMany(x => x.PricingRules).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PricingRule>().HasOne(x => x.Machine).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PricingRule>().HasOne(x => x.Material).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<EmployeePricingOverride>().HasOne(x => x.PricingRule).WithMany(x => x.EmployeeOverrides).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<EmployeePricingOverride>().HasOne(x => x.Employee).WithMany(x => x.PricingOverrides).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionRecord>().HasOne(x => x.Employee).WithMany(x => x.ProductionRecords).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionRecord>().HasOne(x => x.Machine).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionRecord>().HasOne(x => x.Material).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionRecord>().HasOne(x => x.MaterialSpecification).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PayAdjustment>().HasOne(x => x.Employee).WithMany(x => x.PayAdjustments).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDecimal<TEntity>(ModelBuilder modelBuilder, string property, int precision, int scale) where TEntity : class =>
        modelBuilder.Entity<TEntity>().Property(property).HasPrecision(precision, scale);
}

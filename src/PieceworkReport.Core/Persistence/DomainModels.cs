using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Core.Data;

public enum UserRole
{
    Clerk,
    Manager
}

public enum PricingMode
{
    AttainmentBased = 0,
    DirectPieceRate = 1
}

public sealed class AppUser
{
    public int Id { get; set; }
    [MaxLength(50)] public required string Username { get; set; }
    [MaxLength(300)] public required string PasswordHash { get; set; }
    [MaxLength(64)] public required string SecurityStamp { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SecurityAuditEntry
{
    public int Id { get; set; }
    [MaxLength(80)] public required string EventType { get; set; }
    [MaxLength(50)] public required string Username { get; set; }
    [MaxLength(240)] public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class SchemaVersionRecord
{
    [Key] public int Id { get; set; } = 1;
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class Employee
{
    public int Id { get; set; }
    [MaxLength(30)] public required string Code { get; set; }
    [MaxLength(80)] public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ProductionRecord> ProductionRecords { get; set; } = [];
    public ICollection<PayAdjustment> PayAdjustments { get; set; } = [];
    public ICollection<EmployeePricingOverride> PricingOverrides { get; set; } = [];
}

public sealed class Machine
{
    public int Id { get; set; }
    [MaxLength(30)] public required string Code { get; set; }
    [MaxLength(80)] public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<MachineSpecification> Specifications { get; set; } = [];
}

public sealed class Material
{
    public int Id { get; set; }
    [MaxLength(40)] public required string Code { get; set; }
    [MaxLength(100)] public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    [Column("Specification"), MaxLength(160)] public string LegacySpecification { get; set; } = "-";
    [Column("BuckleCount")] public decimal LegacyBuckleCount { get; set; }
    public ICollection<MaterialSpecification> Specifications { get; set; } = [];
}

public sealed class MaterialSpecification
{
    public int Id { get; set; }
    [MaxLength(60)] public required string Code { get; set; }
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    [MaxLength(160)] public required string Description { get; set; }
    public decimal BuckleCount { get; set; }
    [MaxLength(240)] public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<MachineSpecification> Machines { get; set; } = [];
}

public sealed class MachineSpecification
{
    public int Id { get; set; }
    public int MachineId { get; set; }
    public Machine Machine { get; set; } = null!;
    public int MaterialSpecificationId { get; set; }
    public MaterialSpecification MaterialSpecification { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    [MaxLength(240)] public string? Note { get; set; }
    public ICollection<PricingRule> PricingRules { get; set; } = [];
}

public sealed class WagePeriod
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Budget { get; set; }
    public int PlannedWorkdays { get; set; }
    public int PlannedHeadcount { get; set; }
    public bool ExportOutdated { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public ICollection<WagePeriodWorkday> Workdays { get; set; } = [];
    public ICollection<PricingRule> PricingRules { get; set; } = [];
    public ICollection<ProductionRecord> ProductionRecords { get; set; } = [];
    public ICollection<PayAdjustment> PayAdjustments { get; set; } = [];
    [NotMapped] public string DisplayName => $"{Year}年{Month}月";
    [NotMapped] public decimal TargetDailyWage => PricingMath.TargetDailyWage(Budget, PlannedWorkdays, PlannedHeadcount);
}

public sealed class WagePeriodWorkday
{
    public int Id { get; set; }
    public int WagePeriodId { get; set; }
    public WagePeriod WagePeriod { get; set; } = null!;
    public DateTime WorkDate { get; set; }
}

public sealed class PricingRule
{
    public int Id { get; set; }
    public int WagePeriodId { get; set; }
    public WagePeriod WagePeriod { get; set; } = null!;
    public int? MachineSpecificationId { get; set; }
    public MachineSpecification? MachineSpecification { get; set; }
    public PricingMode Mode { get; set; }
    public decimal? TargetDailyWage { get; set; }
    public decimal? DefaultTargetBuckleCount { get; set; }
    public decimal? DirectPieceRate { get; set; }
    [MaxLength(240)] public string? Note { get; set; }
    public ICollection<EmployeePricingOverride> EmployeeOverrides { get; set; } = [];
    public int MachineId { get; set; }
    public Machine Machine { get; set; } = null!;
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public decimal? StandardDailyPieces { get; set; }
}

public sealed class EmployeePricingOverride
{
    public int Id { get; set; }
    public int PricingRuleId { get; set; }
    public PricingRule PricingRule { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public decimal TargetBuckleCount { get; set; }
}

public sealed class ProductionRecord
{
    public int Id { get; set; }
    public int WagePeriodId { get; set; }
    public WagePeriod WagePeriod { get; set; } = null!;
    public DateTime WorkDate { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int MachineId { get; set; }
    public Machine Machine { get; set; } = null!;
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public int? MaterialSpecificationId { get; set; }
    public MaterialSpecification? MaterialSpecification { get; set; }
    public decimal Quantity { get; set; }
    [MaxLength(240)] public string? Note { get; set; }
    [MaxLength(20)] public string Source { get; set; } = "Manual";
    [MaxLength(50)] public required string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class PayAdjustment
{
    public int Id { get; set; }
    public int WagePeriodId { get; set; }
    public WagePeriod WagePeriod { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateTime AdjustmentDate { get; set; }
    [MaxLength(80)] public required string Category { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(240)] public string? Note { get; set; }
    [MaxLength(50)] public required string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class ExportSnapshot
{
    public int Id { get; set; }
    public int WagePeriodId { get; set; }
    public WagePeriod WagePeriod { get; set; } = null!;
    public int Version { get; set; }
    [MaxLength(180)] public required string FileName { get; set; }
    public decimal PieceworkTotal { get; set; }
    public decimal AdjustmentTotal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(50)] public required string CreatedBy { get; set; }
}

public sealed class CodeSequence
{
    [Key, MaxLength(80)] public required string Name { get; set; }
    public int NextValue { get; set; }
}

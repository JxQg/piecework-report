using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Web.Services;

public sealed record CalculationIssue(string Code, string Message);

public sealed record WageLineResult(
    int RecordId,
    DateTime WorkDate,
    int EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string MachineCode,
    string MachineName,
    string MaterialCode,
    string MaterialName,
    string SpecificationCode,
    string Specification,
    decimal Quantity,
    decimal BuckleCount,
    decimal TargetBuckleCount,
    decimal TargetDailyWage,
    decimal BuckleRate,
    decimal PieceRate,
    decimal AttainmentRate,
    decimal Wage,
    string? Note);

public sealed record DailyWageResult(
    DateTime WorkDate,
    int EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    decimal AttainmentRate,
    decimal PieceworkWage,
    decimal AdjustmentAmount,
    decimal TotalWage);

public sealed record EmployeeWageResult(
    int EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    decimal PieceworkWage,
    decimal AdjustmentAmount,
    decimal TotalWage);

public sealed class PeriodWageReport
{
    public required WagePeriod Period { get; init; }
    public required IReadOnlyList<WageLineResult> Lines { get; init; }
    public required IReadOnlyList<DailyWageResult> DailyRows { get; init; }
    public required IReadOnlyList<EmployeeWageResult> EmployeeRows { get; init; }
    public required IReadOnlyList<CalculationIssue> Issues { get; init; }
    public decimal PieceworkTotal { get; init; }
    public decimal AdjustmentTotal { get; init; }
    public decimal FinalTotal { get; init; }
    public decimal PieceworkVariance { get; init; }
    public decimal FinalVariance { get; init; }
}

public sealed class WageCalculationService(AppDbContext db)
{
    public async Task<PeriodWageReport?> CalculateAsync(int periodId)
    {
        var period = await db.WagePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId);
        if (period is null) return null;

        var records = await db.ProductionRecords.AsNoTracking()
            .Where(x => x.WagePeriodId == periodId)
            .Include(x => x.Employee)
            .Include(x => x.Machine)
            .Include(x => x.Material)
            .Include(x => x.MaterialSpecification)!.ThenInclude(x => x!.Material)
            .OrderBy(x => x.WorkDate).ThenBy(x => x.Employee.Code)
            .ToListAsync();
        var rules = await db.PricingRules.AsNoTracking()
            .Where(x => x.WagePeriodId == periodId && x.MachineSpecificationId != null)
            .Include(x => x.MachineSpecification)!.ThenInclude(x => x!.MaterialSpecification).ThenInclude(x => x.Material)
            .ToDictionaryAsync(x => (x.MachineSpecification!.MachineId, x.MachineSpecification.MaterialSpecificationId));
        var ruleIds = rules.Values.Select(x => x.Id).ToList();
        var overrides = await db.EmployeePricingOverrides.AsNoTracking()
            .Where(x => ruleIds.Contains(x.PricingRuleId))
            .ToDictionaryAsync(x => (x.PricingRuleId, x.EmployeeId));
        var adjustments = await db.PayAdjustments.AsNoTracking()
            .Where(x => x.WagePeriodId == periodId)
            .Include(x => x.Employee)
            .ToListAsync();

        var issues = new List<CalculationIssue>();
        var lines = new List<WageLineResult>();
        foreach (var record in records)
        {
            var specification = record.MaterialSpecification;
            if (specification is null)
            {
                issues.Add(new CalculationIssue("MISSING_SPECIFICATION", $"{record.WorkDate:yyyy-MM-dd} {record.Employee.Name} 的旧计件记录尚未关联规格。"));
                continue;
            }

            if (!rules.TryGetValue((record.MachineId, specification.Id), out var rule))
            {
                issues.Add(new CalculationIssue("MISSING_RULE", $"{record.WorkDate:yyyy-MM-dd} {record.Employee.Name} 的 {specification.Code} 尚未配置计价规则。"));
                continue;
            }

            overrides.TryGetValue((rule.Id, record.EmployeeId), out var employeeOverride);
            var target = PricingMath.ResolveTargetBuckleCount(rule, employeeOverride);
            if (target <= 0)
            {
                issues.Add(new CalculationIssue("MISSING_EMPLOYEE_TARGET", $"{record.Employee.Name} 在 {specification.Code} 的达标扣数无效。"));
                continue;
            }

            var pieceRate = PricingMath.PieceRate(rule, specification, target);
            if (pieceRate <= 0)
            {
                issues.Add(new CalculationIssue("INVALID_RULE", $"{specification.Code} 的计价参数无效。"));
                continue;
            }

            var buckleRate = rule.Mode == PricingMode.AttainmentBased
                ? PricingMath.BuckleRate(rule, target)
                : pieceRate / specification.BuckleCount;
            lines.Add(new WageLineResult(
                record.Id,
                record.WorkDate.Date,
                record.EmployeeId,
                record.Employee.Code,
                record.Employee.Name,
                record.Machine.Code,
                record.Machine.Name,
                specification.Material.Code,
                specification.Material.Name,
                specification.Code,
                specification.Description,
                record.Quantity,
                specification.BuckleCount,
                target,
                rule.TargetDailyWage ?? 0,
                buckleRate,
                pieceRate,
                PricingMath.AttainmentRate(record.Quantity, specification, target),
                record.Quantity * pieceRate,
                record.Note));
        }

        var adjustmentByDay = adjustments
            .GroupBy(x => (x.AdjustmentDate.Date, x.EmployeeId))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
        var dayKeys = lines.Select(x => (x.WorkDate, x.EmployeeId, x.EmployeeCode, x.EmployeeName))
            .Concat(adjustments.Select(x => (x.AdjustmentDate.Date, x.EmployeeId, x.Employee.Code, x.Employee.Name)))
            .Distinct().ToList();
        var dailyRows = dayKeys.Select(key =>
        {
            var dayLines = lines.Where(x => x.WorkDate == key.Item1 && x.EmployeeId == key.Item2).ToList();
            var piecework = dayLines.Sum(x => x.Wage);
            var adjustment = adjustmentByDay.GetValueOrDefault((key.Item1, key.Item2));
            return new DailyWageResult(key.Item1, key.Item2, key.Item3, key.Item4,
                dayLines.Sum(x => x.AttainmentRate), piecework, adjustment, piecework + adjustment);
        }).OrderBy(x => x.WorkDate).ThenBy(x => x.EmployeeCode).ToList();

        var employees = await db.Employees.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
        var relevantIds = lines.Select(x => x.EmployeeId).Union(adjustments.Select(x => x.EmployeeId)).ToHashSet();
        var employeeRows = employees.Where(x => relevantIds.Contains(x.Id)).Select(employee =>
        {
            var pieceworkExact = lines.Where(x => x.EmployeeId == employee.Id).Sum(x => x.Wage);
            var adjustment = adjustments.Where(x => x.EmployeeId == employee.Id).Sum(x => x.Amount);
            return new EmployeeWageResult(employee.Id, employee.Code, employee.Name,
                PricingMath.RoundMoney(pieceworkExact), PricingMath.RoundMoney(adjustment), PricingMath.RoundMoney(pieceworkExact + adjustment));
        }).ToList();

        var exactPieceworkTotal = lines.Sum(x => x.Wage);
        var adjustmentTotal = adjustments.Sum(x => x.Amount);
        var pieceworkTotal = PricingMath.RoundMoney(exactPieceworkTotal);
        var finalTotal = PricingMath.RoundMoney(exactPieceworkTotal + adjustmentTotal);
        return new PeriodWageReport
        {
            Period = period,
            Lines = lines,
            DailyRows = dailyRows,
            EmployeeRows = employeeRows,
            Issues = issues.Distinct().ToList(),
            PieceworkTotal = pieceworkTotal,
            AdjustmentTotal = PricingMath.RoundMoney(adjustmentTotal),
            FinalTotal = finalTotal,
            PieceworkVariance = pieceworkTotal - period.Budget,
            FinalVariance = finalTotal - period.Budget
        };
    }
}

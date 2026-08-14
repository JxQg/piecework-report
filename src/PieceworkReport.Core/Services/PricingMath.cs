using PieceworkReport.Core.Data;

namespace PieceworkReport.Core.Services;

public static class PricingMath
{
    public static decimal TargetDailyWage(decimal budget, int plannedWorkdays, int plannedHeadcount) =>
        plannedWorkdays > 0 && plannedHeadcount > 0
            ? Math.Round(budget / plannedWorkdays / plannedHeadcount, 6, MidpointRounding.AwayFromZero)
            : 0m;

    public static decimal ResolveTargetBuckleCount(PricingRule rule, EmployeePricingOverride? employeeOverride) =>
        employeeOverride?.TargetBuckleCount ?? rule.DefaultTargetBuckleCount ?? 0m;

    public static decimal BuckleRate(PricingRule rule, decimal targetBuckleCount) =>
        BuckleRate(rule.TargetDailyWage ?? 0m, targetBuckleCount);

    public static decimal BuckleRate(decimal targetDailyWage, decimal targetBuckleCount) =>
        targetBuckleCount > 0 ? targetDailyWage / targetBuckleCount : 0m;

    public static decimal PieceRate(PricingRule rule, MaterialSpecification specification, decimal targetBuckleCount) =>
        rule.Mode == PricingMode.DirectPieceRate
            ? rule.DirectPieceRate ?? 0m
            : BuckleRate(rule, targetBuckleCount) * specification.BuckleCount;

    public static decimal AttainmentRate(decimal quantity, MaterialSpecification specification, decimal targetBuckleCount) =>
        targetBuckleCount > 0 ? quantity * specification.BuckleCount / targetBuckleCount : 0m;

    public static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

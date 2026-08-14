using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Tests;

public sealed class PricingMathTests
{
    [Fact]
    public void TargetDailyWage_UsesBudgetWorkdaysAndHeadcount() => Assert.Equal(239.130435m, PricingMath.TargetDailyWage(88_000m, 23, 16));

    [Fact]
    public void AttainmentPricing_UsesEmployeeTargetAndSpecificationBuckleCount()
    {
        var rule = new PricingRule { Mode = PricingMode.AttainmentBased, TargetDailyWage = 260m, DefaultTargetBuckleCount = 20_000m };
        var specification = new MaterialSpecification { Code = "P000001-S0001", Description = "十扣", BuckleCount = 10m, Material = null! };
        var employeeOverride = new EmployeePricingOverride { TargetBuckleCount = 25_000m, Employee = null!, PricingRule = null! };
        var target = PricingMath.ResolveTargetBuckleCount(rule, employeeOverride);
        Assert.Equal(25_000m, target);
        Assert.Equal(0.0104m, PricingMath.BuckleRate(rule, target));
        Assert.Equal(0.104m, PricingMath.PieceRate(rule, specification, target));
        Assert.Equal(0.4m, PricingMath.AttainmentRate(1_000m, specification, target));
    }

    [Fact]
    public void DirectPricing_KeepsPieceRateAndStillCalculatesAttainment()
    {
        var rule = new PricingRule { Mode = PricingMode.DirectPieceRate, DirectPieceRate = 0.875m, DefaultTargetBuckleCount = 1_000m };
        var specification = new MaterialSpecification { Code = "P000001-S0001", Description = "两扣", BuckleCount = 2m, Material = null! };
        Assert.Equal(0.875m, PricingMath.PieceRate(rule, specification, 1_000m));
        Assert.Equal(0.5m, PricingMath.AttainmentRate(250m, specification, 1_000m));
        Assert.Equal(0.26m, PricingMath.BuckleRate(260m, 1_000m));
    }

    [Theory]
    [InlineData(0, 23, 16)]
    [InlineData(88000, 0, 16)]
    [InlineData(88000, 23, 0)]
    public void TargetDailyWage_InvalidInputsReturnZero(decimal budget, int workdays, int headcount) => Assert.Equal(0m, PricingMath.TargetDailyWage(budget, workdays, headcount));
}

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

public sealed record PeriodOption(int Id, int Year, int Month)
{
    public string DisplayName => $"{Year}年{Month}月";
}

public sealed class IndexModel(AppDbContext db, WageCalculationService calculationService) : PageModel
{
    public IReadOnlyList<PeriodOption> Periods { get; private set; } = [];
    public PeriodOption? SelectedPeriod { get; private set; }
    public WagePeriod? ManagerPeriod { get; private set; }
    public PeriodWageReport? ManagerReport { get; private set; }
    public int ProductionRecordCount { get; private set; }
    public int TodayRecordCount { get; private set; }
    public int WorkdayCount { get; private set; }
    public int PendingSpecificationCount { get; private set; }
    public int PricingRuleCount { get; private set; }

    public async Task OnGetAsync(int? periodId)
    {
        Periods = await db.WagePeriods.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .Select(x => new PeriodOption(x.Id, x.Year, x.Month)).ToListAsync();
        SelectedPeriod = periodId.HasValue ? Periods.SingleOrDefault(x => x.Id == periodId) : Periods.FirstOrDefault();
        PendingSpecificationCount = await db.MaterialSpecifications.CountAsync(x => x.IsActive && !x.Machines.Any(m => m.IsActive));
        if (SelectedPeriod is null) return;

        ProductionRecordCount = await db.ProductionRecords.CountAsync(x => x.WagePeriodId == SelectedPeriod.Id);
        TodayRecordCount = await db.ProductionRecords.CountAsync(x => x.WagePeriodId == SelectedPeriod.Id && x.WorkDate == DateTime.Today.Date);
        WorkdayCount = await db.WagePeriodWorkdays.CountAsync(x => x.WagePeriodId == SelectedPeriod.Id);
        if (!User.IsInRole("Manager")) return;

        ManagerPeriod = await db.WagePeriods.AsNoTracking().SingleAsync(x => x.Id == SelectedPeriod.Id);
        ManagerReport = await calculationService.CalculateAsync(SelectedPeriod.Id);
        PricingRuleCount = await db.PricingRules.CountAsync(x => x.WagePeriodId == SelectedPeriod.Id);
    }
}

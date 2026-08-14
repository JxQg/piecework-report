using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Web.Pages;

[Authorize(Roles = "Manager")]
public sealed class PeriodsModel(AppDbContext db, BusinessDataDeletionService deletionService, OperationAuditService operationAuditService) : PageModel
{
    public IReadOnlyList<WagePeriod> Periods { get; private set; } = [];
    public IReadOnlyList<int> MonthDays { get; private set; } = [];
    [BindProperty] public PeriodInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? editId)
    {
        await LoadAsync();
        if (editId.HasValue)
        {
            var period = await db.WagePeriods.AsNoTracking().Include(x => x.Workdays).SingleOrDefaultAsync(x => x.Id == editId);
            if (period is not null) Input = new PeriodInput { Id = period.Id, Year = period.Year, Month = period.Month, Budget = period.Budget, PlannedHeadcount = period.PlannedHeadcount, SelectedDays = period.Workdays.Select(x => x.WorkDate.Day).OrderBy(x => x).ToList() };
        }
        BuildMonthDays();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.SelectedDays = Input.SelectedDays.Distinct().OrderBy(x => x).ToList();
        if (Input.Year is >= 2020 and <= 2100 && Input.Month is >= 1 and <= 12)
        {
            var days = DateTime.DaysInMonth(Input.Year, Input.Month);
            if (Input.SelectedDays.Count == 0 || Input.SelectedDays.Any(x => x < 1 || x > days)) ModelState.AddModelError("Input.SelectedDays", "请至少选择一个有效工作日。");
        }
        if (await db.WagePeriods.AnyAsync(x => x.Year == Input.Year && x.Month == Input.Month && x.Id != Input.Id)) ModelState.AddModelError(string.Empty, "该工资月份已经存在。");
        WagePeriod? existing = null;
        if (Input.Id != 0)
        {
            existing = await db.WagePeriods.Include(x => x.Workdays).SingleOrDefaultAsync(x => x.Id == Input.Id);
            if (existing is null) ModelState.AddModelError(string.Empty, "工资月份不存在或已删除。");
            else
            {
                var hasHistory = await db.PricingRules.AnyAsync(x => x.WagePeriodId == existing.Id)
                    || await db.ProductionRecords.AnyAsync(x => x.WagePeriodId == existing.Id)
                    || await db.PayAdjustments.AnyAsync(x => x.WagePeriodId == existing.Id)
                    || await db.ExportSnapshots.AnyAsync(x => x.WagePeriodId == existing.Id);
                if (hasHistory && (existing.Year != Input.Year || existing.Month != Input.Month))
                    ModelState.AddModelError(string.Empty, "该工资月份已有业务历史，不能修改年月。");
                var recordDays = await db.ProductionRecords.Where(x => x.WagePeriodId == existing.Id).Select(x => x.WorkDate.Day).Distinct().ToListAsync();
                if (recordDays.Any(day => !Input.SelectedDays.Contains(day)))
                    ModelState.AddModelError("Input.SelectedDays", "不能移除已有计件记录的工作日。");
            }
        }
        if (!ModelState.IsValid) { await LoadAsync(); BuildMonthDays(); return Page(); }

        WagePeriod period;
        if (Input.Id == 0) { period = new WagePeriod { Year = Input.Year, Month = Input.Month }; db.WagePeriods.Add(period); await db.SaveChangesAsync(); }
        else { period = existing!; period.Year = Input.Year; period.Month = Input.Month; db.WagePeriodWorkdays.RemoveRange(period.Workdays); }
        period.Budget = Input.Budget; period.PlannedHeadcount = Input.PlannedHeadcount; period.PlannedWorkdays = Input.SelectedDays.Count; period.ExportOutdated = true; period.UpdatedAt = DateTime.Now;
        db.WagePeriodWorkdays.AddRange(Input.SelectedDays.Select(day => new WagePeriodWorkday { WagePeriodId = period.Id, WorkDate = new DateTime(Input.Year, Input.Month, day) }));
        operationAuditService.Record(Input.Id == 0 ? "新增工资月份" : "修改工资月份", User.Identity?.Name ?? "unknown", $"工资月份 {period.DisplayName}，{period.PlannedWorkdays} 个工作日");
        await db.SaveChangesAsync(); FlashMessage = $"{period.DisplayName}已保存，共 {period.PlannedWorkdays} 个实际工作日。"; return RedirectToPage(new { editId = period.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await deletionService.DeleteWagePeriodAsync(id);
        if (!result.IsDeleted) { ModelState.AddModelError(string.Empty, result.ErrorMessage!); await LoadAsync(); BuildMonthDays(); return Page(); }
        operationAuditService.Record("删除工资月份", User.Identity?.Name ?? "unknown", $"工资月份 ID {id}");
        await db.SaveChangesAsync();
        FlashMessage = "工资月份已删除。";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Periods = await db.WagePeriods.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync();
        if (Input.Year != 0) return;
        Input.Year = DateTime.Today.Year; Input.Month = DateTime.Today.Month; Input.PlannedHeadcount = 16;
        Input.SelectedDays = Enumerable.Range(1, DateTime.DaysInMonth(Input.Year, Input.Month)).Where(day => new DateTime(Input.Year, Input.Month, day).DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).ToList();
    }
    private void BuildMonthDays() => MonthDays = Input.Year is >= 2020 and <= 2100 && Input.Month is >= 1 and <= 12 ? Enumerable.Range(1, DateTime.DaysInMonth(Input.Year, Input.Month)).ToList() : [];
    public sealed class PeriodInput
    {
        public int Id { get; set; }
        [Range(2020, 2100)] public int Year { get; set; }
        [Range(1, 12)] public int Month { get; set; }
        [Range(typeof(decimal), "0.01", "99999999")] public decimal Budget { get; set; }
        [Range(1, 1000)] public int PlannedHeadcount { get; set; }
        public List<int> SelectedDays { get; set; } = [];
    }
}

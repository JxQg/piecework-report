using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Web.Pages;

[Authorize(Roles = "Manager")]
public sealed class AdjustmentsModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<WagePeriod> Periods { get; private set; } = [];
    public WagePeriod? SelectedPeriod { get; private set; }
    public IReadOnlyList<Employee> Employees { get; private set; } = [];
    public IReadOnlyList<PayAdjustment> Adjustments { get; private set; } = [];

    [BindProperty] public AdjustmentInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? periodId) => await LoadAsync(periodId);

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(Input.PeriodId);
            return Page();
        }

        var period = await db.WagePeriods.SingleOrDefaultAsync(x => x.Id == Input.PeriodId);
        if (period is null || Input.AdjustmentDate.Year != period.Year || Input.AdjustmentDate.Month != period.Month)
        {
            ModelState.AddModelError("Input.AdjustmentDate", "增项日期必须属于所选工资月份。");
            await LoadAsync(Input.PeriodId);
            return Page();
        }

        db.PayAdjustments.Add(new PayAdjustment
        {
            WagePeriodId = period.Id,
            EmployeeId = Input.EmployeeId,
            AdjustmentDate = Input.AdjustmentDate.Date,
            Category = Input.Category.Trim(),
            Amount = Input.Amount,
            Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim(),
            UpdatedBy = User.Identity?.Name ?? "unknown"
        });
        period.ExportOutdated = true;
        period.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        FlashMessage = "工资增项已添加。";
        return RedirectToPage(new { periodId = period.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, int periodId)
    {
        var entity = await db.PayAdjustments.SingleAsync(x => x.Id == id && x.WagePeriodId == periodId);
        db.PayAdjustments.Remove(entity);
        var period = await db.WagePeriods.SingleAsync(x => x.Id == periodId);
        period.ExportOutdated = true;
        await db.SaveChangesAsync();
        return RedirectToPage(new { periodId });
    }

    private async Task LoadAsync(int? periodId)
    {
        Periods = await db.WagePeriods.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync();
        SelectedPeriod = periodId.HasValue ? Periods.SingleOrDefault(x => x.Id == periodId) : Periods.FirstOrDefault();
        Employees = await db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        if (SelectedPeriod is null) return;
        Input.PeriodId = SelectedPeriod.Id;
        Input.AdjustmentDate = new DateTime(SelectedPeriod.Year, SelectedPeriod.Month, 1);
        Adjustments = await db.PayAdjustments.AsNoTracking()
            .Where(x => x.WagePeriodId == SelectedPeriod.Id)
            .Include(x => x.Employee)
            .OrderByDescending(x => x.AdjustmentDate)
            .ThenBy(x => x.Employee.Code)
            .ToListAsync();
    }

    public sealed class AdjustmentInput
    {
        [Range(1, int.MaxValue)] public int PeriodId { get; set; }
        [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
        [DataType(DataType.Date)] public DateTime AdjustmentDate { get; set; }
        [Required, MaxLength(80)] public string Category { get; set; } = string.Empty;
        [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "增项金额必须大于 0")] public decimal Amount { get; set; }
        [MaxLength(240)] public string? Note { get; set; }
    }
}

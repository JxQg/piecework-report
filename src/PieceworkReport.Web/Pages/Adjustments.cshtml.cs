using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Web.Pages;

[Authorize(Roles = "Manager")]
public sealed class AdjustmentsModel(AppDbContext db, OperationAuditService operationAuditService) : PageModel
{
    public IReadOnlyList<WagePeriod> Periods { get; private set; } = [];
    public WagePeriod? SelectedPeriod { get; private set; }
    public IReadOnlyList<Employee> Employees { get; private set; } = [];
    public IReadOnlyList<PayAdjustment> Adjustments { get; private set; } = [];

    [BindProperty] public AdjustmentInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? periodId, int? editId)
    {
        await LoadAsync(periodId);
        if (editId is null || SelectedPeriod is null) return;
        var entity = await db.PayAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == editId && x.WagePeriodId == SelectedPeriod.Id);
        if (entity is null) return;
        Input = new AdjustmentInput { Id = entity.Id, PeriodId = entity.WagePeriodId, EmployeeId = entity.EmployeeId, AdjustmentDate = entity.AdjustmentDate, Category = entity.Category, Amount = entity.Amount, Note = entity.Note };
    }

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
        if (!await db.Employees.AnyAsync(x => x.Id == Input.EmployeeId && x.IsActive))
        {
            ModelState.AddModelError("Input.EmployeeId", "员工无效或已停用。");
            await LoadAsync(Input.PeriodId);
            return Page();
        }

        PayAdjustment entity;
        if (Input.Id == 0)
        {
            entity = new PayAdjustment { WagePeriodId = period.Id, Category = Input.Category.Trim(), UpdatedBy = User.Identity?.Name ?? "unknown" };
            db.PayAdjustments.Add(entity);
        }
        else
        {
            var existing = await db.PayAdjustments.SingleOrDefaultAsync(x => x.Id == Input.Id && x.WagePeriodId == period.Id);
            if (existing is null)
            {
                ModelState.AddModelError(string.Empty, "工资增项不存在或不属于当前工资月份。");
                await LoadAsync(Input.PeriodId);
                return Page();
            }
            entity = existing;
        }
        entity.EmployeeId = Input.EmployeeId;
        entity.AdjustmentDate = Input.AdjustmentDate.Date;
        entity.Category = Input.Category.Trim();
        entity.Amount = Input.Amount;
        entity.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();
        entity.UpdatedBy = User.Identity?.Name ?? "unknown";
        entity.UpdatedAt = DateTime.Now;
        period.ExportOutdated = true;
        period.UpdatedAt = DateTime.Now;
        operationAuditService.Record(Input.Id == 0 ? "新增工资增项" : "修改工资增项", User.Identity?.Name ?? "unknown", $"工资月份 {period.DisplayName}，增项 ID {entity.Id}");
        await db.SaveChangesAsync();
        FlashMessage = Input.Id == 0 ? "工资增项已添加。" : "工资增项已修改。";
        return RedirectToPage(new { periodId = period.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, int periodId)
    {
        var entity = await db.PayAdjustments.SingleOrDefaultAsync(x => x.Id == id && x.WagePeriodId == periodId);
        if (entity is null) return NotFound();
        db.PayAdjustments.Remove(entity);
        var period = await db.WagePeriods.SingleAsync(x => x.Id == periodId);
        period.ExportOutdated = true;
        period.UpdatedAt = DateTime.Now;
        operationAuditService.Record("删除工资增项", User.Identity?.Name ?? "unknown", $"工资月份 {period.DisplayName}，增项 ID {id}");
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
        public int Id { get; set; }
        [Range(1, int.MaxValue)] public int PeriodId { get; set; }
        [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
        [DataType(DataType.Date)] public DateTime AdjustmentDate { get; set; }
        [Required, MaxLength(80)] public string Category { get; set; } = string.Empty;
        [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "增项金额必须大于 0")] public decimal Amount { get; set; }
        [MaxLength(240)] public string? Note { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Web.Pages;

[Authorize(Roles = "Manager")]
public sealed class PricingModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<WagePeriod> Periods { get; private set; } = [];
    public WagePeriod? SelectedPeriod { get; private set; }
    public IReadOnlyList<MachineSpecification> MachineSpecifications { get; private set; } = [];
    public IReadOnlyList<PricingRule> Rules { get; private set; } = [];
    public IReadOnlyList<Employee> Employees { get; private set; } = [];
    [BindProperty] public PricingInput Input { get; set; } = new();
    [BindProperty] public OverrideInput Override { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? periodId, int? editId)
    {
        await LoadAsync(periodId);
        if (editId is null) { Input.PeriodId = SelectedPeriod?.Id ?? 0; Input.TargetDailyWage = SelectedPeriod?.TargetDailyWage ?? 0; return; }
        var rule = Rules.SingleOrDefault(x => x.Id == editId);
        if (rule is null) return;
        Input = new PricingInput { Id = rule.Id, PeriodId = rule.WagePeriodId, MachineSpecificationId = rule.MachineSpecificationId ?? 0, Mode = rule.Mode, TargetDailyWage = rule.TargetDailyWage, DefaultTargetBuckleCount = rule.DefaultTargetBuckleCount, DirectPieceRate = rule.DirectPieceRate, Note = rule.Note };
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        RemoveModelStatePrefix("Override");
        ValidatePricing();
        var period = await db.WagePeriods.SingleOrDefaultAsync(x => x.Id == Input.PeriodId);
        var link = await db.MachineSpecifications.Include(x => x.MaterialSpecification).SingleOrDefaultAsync(x => x.Id == Input.MachineSpecificationId && x.IsActive);
        if (period is null) ModelState.AddModelError("Input.PeriodId", "工资月份无效。");
        if (link is null) ModelState.AddModelError("Input.MachineSpecificationId", "机器规格无效或已停用。");
        if (await db.PricingRules.AnyAsync(x => x.WagePeriodId == Input.PeriodId && x.MachineSpecificationId == Input.MachineSpecificationId && x.Id != Input.Id)) ModelState.AddModelError(string.Empty, "该月份已存在相同机器规格的规则。");
        if (!ModelState.IsValid || period is null || link is null) { await LoadAsync(Input.PeriodId); return Page(); }

        PricingRule rule;
        if (Input.Id == 0)
        {
            rule = new PricingRule { WagePeriodId = period.Id, MachineSpecificationId = link.Id, MachineId = link.MachineId, MaterialId = link.MaterialSpecification.MaterialId, Machine = null!, Material = null! };
            db.PricingRules.Add(rule);
        }
        else
        {
            rule = await db.PricingRules.SingleAsync(x => x.Id == Input.Id);
            rule.MachineSpecificationId = link.Id; rule.MachineId = link.MachineId; rule.MaterialId = link.MaterialSpecification.MaterialId;
        }
        rule.Mode = Input.Mode;
        rule.TargetDailyWage = Input.Mode == PricingMode.AttainmentBased ? Input.TargetDailyWage : null;
        rule.DefaultTargetBuckleCount = Input.DefaultTargetBuckleCount;
        rule.DirectPieceRate = Input.Mode == PricingMode.DirectPieceRate ? Input.DirectPieceRate : null;
        rule.StandardDailyPieces = null;
        rule.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();
        period.ExportOutdated = true; period.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(); FlashMessage = "计价规则已保存。"; return RedirectToPage(new { periodId = period.Id, editId = rule.Id });
    }

    public async Task<IActionResult> OnPostSaveOverrideAsync()
    {
        RemoveModelStatePrefix("Input");
        if (!ModelState.IsValid) { await LoadAsync(Override.PeriodId); return Page(); }
        var rule = await db.PricingRules.SingleOrDefaultAsync(x => x.Id == Override.RuleId && x.WagePeriodId == Override.PeriodId);
        if (rule is null) return NotFound();
        var entity = await db.EmployeePricingOverrides.SingleOrDefaultAsync(x => x.PricingRuleId == rule.Id && x.EmployeeId == Override.EmployeeId);
        if (entity is null) { entity = new EmployeePricingOverride { PricingRuleId = rule.Id, EmployeeId = Override.EmployeeId }; db.EmployeePricingOverrides.Add(entity); }
        entity.TargetBuckleCount = Override.TargetBuckleCount;
        var period = await db.WagePeriods.SingleAsync(x => x.Id == Override.PeriodId); period.ExportOutdated = true; period.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(); FlashMessage = "员工达标扣数已保存。"; return RedirectToPage(new { periodId = Override.PeriodId, editId = rule.Id });
    }

    public async Task<IActionResult> OnPostDeleteOverrideAsync(int id, int periodId)
    {
        var entity = await db.EmployeePricingOverrides.Include(x => x.PricingRule).SingleOrDefaultAsync(x => x.Id == id && x.PricingRule.WagePeriodId == periodId);
        if (entity is null) return NotFound(); db.EmployeePricingOverrides.Remove(entity);
        var period = await db.WagePeriods.SingleAsync(x => x.Id == periodId); period.ExportOutdated = true; await db.SaveChangesAsync(); return RedirectToPage(new { periodId, editId = entity.PricingRuleId });
    }

    private void ValidatePricing()
    {
        if (Input.DefaultTargetBuckleCount is not > 0) ModelState.AddModelError("Input.DefaultTargetBuckleCount", "默认达标扣数必须大于 0。");
        if (Input.Mode == PricingMode.AttainmentBased && Input.TargetDailyWage is not > 0) ModelState.AddModelError("Input.TargetDailyWage", "达标工资必须大于 0。");
        if (Input.Mode == PricingMode.DirectPieceRate && Input.DirectPieceRate is not > 0) ModelState.AddModelError("Input.DirectPieceRate", "直接每件单价必须大于 0。");
    }

    private void RemoveModelStatePrefix(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList())
            ModelState.Remove(key);
    }

    private async Task LoadAsync(int? periodId)
    {
        Periods = await db.WagePeriods.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync();
        SelectedPeriod = periodId.HasValue ? Periods.SingleOrDefault(x => x.Id == periodId) : Periods.FirstOrDefault();
        MachineSpecifications = await db.MachineSpecifications.AsNoTracking().Where(x => x.IsActive && x.Machine.IsActive && x.MaterialSpecification.IsActive && x.MaterialSpecification.Material.IsActive).Include(x => x.Machine).Include(x => x.MaterialSpecification).ThenInclude(x => x.Material).OrderBy(x => x.Machine.Code).ThenBy(x => x.MaterialSpecification.Code).ToListAsync();
        Employees = await db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        if (SelectedPeriod is null) return;
        Rules = await db.PricingRules.AsNoTracking().Where(x => x.WagePeriodId == SelectedPeriod.Id && x.MachineSpecificationId != null).Include(x => x.MachineSpecification)!.ThenInclude(x => x!.Machine).Include(x => x.MachineSpecification)!.ThenInclude(x => x!.MaterialSpecification).ThenInclude(x => x.Material).Include(x => x.EmployeeOverrides).ThenInclude(x => x.Employee).OrderBy(x => x.Machine.Code).ThenBy(x => x.Material.Code).ToListAsync();
    }

    public sealed class PricingInput
    {
        public int Id { get; set; }
        [Range(1, int.MaxValue)] public int PeriodId { get; set; }
        [Range(1, int.MaxValue)] public int MachineSpecificationId { get; set; }
        public PricingMode Mode { get; set; } = PricingMode.AttainmentBased;
        public decimal? TargetDailyWage { get; set; }
        public decimal? DefaultTargetBuckleCount { get; set; }
        public decimal? DirectPieceRate { get; set; }
        [MaxLength(240)] public string? Note { get; set; }
    }
    public sealed class OverrideInput
    {
        [Range(1, int.MaxValue)] public int PeriodId { get; set; }
        [Range(1, int.MaxValue)] public int RuleId { get; set; }
        [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
        [Range(typeof(decimal), "0.001", "999999999")] public decimal TargetBuckleCount { get; set; }
    }
}

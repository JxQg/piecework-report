using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

public sealed class ProductionModel(AppDbContext db, WageCalculationService calculations) : PageModel
{
    public IReadOnlyList<PeriodOption> Periods { get; private set; } = [];
    public PeriodOption? SelectedPeriod { get; private set; }
    public IReadOnlyList<DateTime> Workdays { get; private set; } = [];
    public IReadOnlyList<Employee> Employees { get; private set; } = [];
    public IReadOnlyList<MachineSpecification> MachineSpecifications { get; private set; } = [];
    public IReadOnlyList<RecordView> Records { get; private set; } = [];
    public decimal? ManagerDailyAttainment { get; private set; }
    public decimal? ManagerDailyWage { get; private set; }
    [BindProperty] public ProductionInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? periodId, DateTime? workDate) => await LoadAsync(periodId, workDate);

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var period = await db.WagePeriods.AsNoTracking().Where(x => x.Id == Input.PeriodId).Select(x => new { x.Id, x.Year, x.Month }).SingleOrDefaultAsync();
        if (period is null || Input.WorkDate.Year != period.Year || Input.WorkDate.Month != period.Month) ModelState.AddModelError("Input.WorkDate", "日期必须属于所选工资月份。");
        else if (!await db.WagePeriodWorkdays.AnyAsync(x => x.WagePeriodId == period.Id && x.WorkDate == Input.WorkDate.Date)) ModelState.AddModelError("Input.WorkDate", "所选日期未配置为本月工作日。");
        var link = await db.MachineSpecifications.AsNoTracking().Include(x => x.MaterialSpecification).SingleOrDefaultAsync(x => x.Id == Input.MachineSpecificationId && x.IsActive && x.Machine.IsActive && x.MaterialSpecification.IsActive);
        if (link is null) ModelState.AddModelError("Input.MachineSpecificationId", "机器规格无效或已停用。");

        int ruleId = 0; decimal defaultTarget = 0;
        if (period is not null && link is not null)
        {
            var rule = await db.PricingRules.Where(x => x.WagePeriodId == period.Id && x.MachineSpecificationId == link.Id)
                .Select(x => new { x.Id, x.DefaultTargetBuckleCount, IsValid = x.Mode == PricingMode.AttainmentBased ? x.TargetDailyWage > 0 : x.DirectPieceRate > 0 }).SingleOrDefaultAsync();
            if (rule is null || !rule.IsValid) ModelState.AddModelError(string.Empty, "当前机器规格尚未完成可用计价配置，请联系经理。");
            else { ruleId = rule.Id; defaultTarget = rule.DefaultTargetBuckleCount ?? 0; }
        }
        if (ruleId != 0)
        {
            var target = await db.EmployeePricingOverrides.Where(x => x.PricingRuleId == ruleId && x.EmployeeId == Input.EmployeeId).Select(x => (decimal?)x.TargetBuckleCount).SingleOrDefaultAsync() ?? defaultTarget;
            if (target <= 0) ModelState.AddModelError(string.Empty, "该员工尚未配置有效达标数，请联系经理。");
        }
        if (!ModelState.IsValid || period is null || link is null) { await LoadAsync(Input.PeriodId, Input.WorkDate); return Page(); }

        var record = await db.ProductionRecords.SingleOrDefaultAsync(x => x.WagePeriodId == period.Id && x.WorkDate == Input.WorkDate.Date && x.EmployeeId == Input.EmployeeId && x.MachineId == link.MachineId && x.MaterialSpecificationId == link.MaterialSpecificationId);
        if (record is null)
        {
            record = new ProductionRecord { WagePeriodId = period.Id, WorkDate = Input.WorkDate.Date, EmployeeId = Input.EmployeeId, MachineId = link.MachineId, MaterialId = link.MaterialSpecification.MaterialId, MaterialSpecificationId = link.MaterialSpecificationId, UpdatedBy = User.Identity?.Name ?? "unknown" };
            db.ProductionRecords.Add(record);
        }
        record.Quantity = Input.Quantity; record.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim(); record.Source = "Manual"; record.UpdatedBy = User.Identity?.Name ?? "unknown"; record.UpdatedAt = DateTime.Now;
        await db.WagePeriods.Where(x => x.Id == period.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExportOutdated, true).SetProperty(x => x.UpdatedAt, DateTime.Now));
        await db.SaveChangesAsync(); FlashMessage = "计件记录已保存；相同员工、日期、机器和规格会更新原记录。"; return RedirectToPage(new { periodId = period.Id, workDate = Input.WorkDate.ToString("yyyy-MM-dd") });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, int periodId, DateTime workDate)
    {
        var record = await db.ProductionRecords.SingleOrDefaultAsync(x => x.Id == id && x.WagePeriodId == periodId); if (record is null) return NotFound(); db.ProductionRecords.Remove(record);
        var period = await db.WagePeriods.SingleAsync(x => x.Id == periodId); period.ExportOutdated = true; period.UpdatedAt = DateTime.Now; await db.SaveChangesAsync(); return RedirectToPage(new { periodId, workDate = workDate.ToString("yyyy-MM-dd") });
    }

    private async Task LoadAsync(int? periodId, DateTime? requestedDate)
    {
        Periods = await db.WagePeriods.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).Select(x => new PeriodOption(x.Id, x.Year, x.Month)).ToListAsync();
        SelectedPeriod = periodId.HasValue ? Periods.SingleOrDefault(x => x.Id == periodId) : Periods.FirstOrDefault();
        Employees = await db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        MachineSpecifications = await db.MachineSpecifications.AsNoTracking().Where(x => x.IsActive && x.Machine.IsActive && x.MaterialSpecification.IsActive && x.MaterialSpecification.Material.IsActive).Include(x => x.Machine).Include(x => x.MaterialSpecification).ThenInclude(x => x.Material).OrderBy(x => x.Machine.Code).ThenBy(x => x.MaterialSpecification.Code).ToListAsync();
        if (SelectedPeriod is null) return;
        Workdays = await db.WagePeriodWorkdays.AsNoTracking().Where(x => x.WagePeriodId == SelectedPeriod.Id).OrderBy(x => x.WorkDate).Select(x => x.WorkDate).ToListAsync();
        var workDate = requestedDate?.Date;
        if (workDate is null || !Workdays.Contains(workDate.Value)) workDate = Workdays.Contains(DateTime.Today) ? DateTime.Today : Workdays.FirstOrDefault();
        if (workDate == default) workDate = new DateTime(SelectedPeriod.Year, SelectedPeriod.Month, 1);
        Input.PeriodId = SelectedPeriod.Id; Input.WorkDate = workDate.Value;
        var records = await db.ProductionRecords.AsNoTracking().Where(x => x.WagePeriodId == SelectedPeriod.Id && x.WorkDate == workDate.Value).Include(x => x.Employee).Include(x => x.Machine).Include(x => x.MaterialSpecification)!.ThenInclude(x => x!.Material).OrderBy(x => x.Employee.Code).ThenBy(x => x.MaterialSpecification!.Code).ToListAsync();
        Dictionary<int, WageLineResult> money = [];
        if (User.IsInRole("Manager"))
        {
            var report = await calculations.CalculateAsync(SelectedPeriod.Id);
            money = report?.Lines.Where(x => x.WorkDate == workDate.Value).ToDictionary(x => x.RecordId) ?? [];
            ManagerDailyAttainment = money.Values.Sum(x => x.AttainmentRate);
            ManagerDailyWage = money.Values.Sum(x => x.Wage);
        }
        Records = records.Select(x => { money.TryGetValue(x.Id, out var line); return new RecordView(x.Id, x.Employee.Code, x.Employee.Name, x.Machine.Code, x.Machine.Name, x.MaterialSpecification?.Material.Code ?? "-", x.MaterialSpecification?.Material.Name ?? "-", x.MaterialSpecification?.Code ?? "-", x.MaterialSpecification?.Description ?? "旧记录未关联规格", x.Quantity, x.Note, line?.PieceRate, line?.AttainmentRate, line?.Wage); }).ToList();
    }

    public sealed record RecordView(int Id, string EmployeeCode, string EmployeeName, string MachineCode, string MachineName, string MaterialCode, string MaterialName, string SpecificationCode, string Specification, decimal Quantity, string? Note, decimal? PieceRate, decimal? AttainmentRate, decimal? Wage);
    public sealed class ProductionInput
    {
        [Range(1, int.MaxValue)] public int PeriodId { get; set; }
        [DataType(DataType.Date)] public DateTime WorkDate { get; set; }
        [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
        [Range(1, int.MaxValue)] public int MachineSpecificationId { get; set; }
        [Range(typeof(decimal), "0.001", "999999999")] public decimal Quantity { get; set; }
        [MaxLength(240)] public string? Note { get; set; }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

[Authorize(Roles = "Manager")]
public sealed class ReportsModel(
    AppDbContext db,
    WageCalculationService calculationService,
    ExcelService excelService,
    ApplicationPaths paths) : PageModel
{
    public IReadOnlyList<WagePeriod> Periods { get; private set; } = [];
    public WagePeriod? SelectedPeriod { get; private set; }
    public PeriodWageReport? Report { get; private set; }
    public IReadOnlyList<ExportSnapshot> Snapshots { get; private set; } = [];

    public async Task OnGetAsync(int? periodId) => await LoadAsync(periodId);

    public async Task<IActionResult> OnPostExportAsync(int periodId)
    {
        if (!User.IsInRole("Manager"))
        {
            return Forbid();
        }

        try
        {
            var package = await excelService.CreateReportAsync(periodId);
            var period = await db.WagePeriods.SingleAsync(x => x.Id == periodId);
            var version = (await db.ExportSnapshots.Where(x => x.WagePeriodId == periodId).MaxAsync(x => (int?)x.Version) ?? 0) + 1;
            var fileName = $"{period.Year}年{period.Month}月计件工资-v{version}.xlsx";
            var exportDirectory = Path.Combine(paths.DataDirectory, "exports");
            Directory.CreateDirectory(exportDirectory);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(exportDirectory, fileName), package.Content);

            db.ExportSnapshots.Add(new ExportSnapshot
            {
                WagePeriodId = periodId,
                Version = version,
                FileName = fileName,
                PieceworkTotal = package.Report.PieceworkTotal,
                AdjustmentTotal = package.Report.AdjustmentTotal,
                CreatedBy = User.Identity?.Name ?? "unknown"
            });
            period.ExportOutdated = false;
            await db.SaveChangesAsync();
            return File(package.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await LoadAsync(periodId);
            return Page();
        }
    }

    public async Task<IActionResult> OnGetDownloadAsync(int snapshotId)
    {
        var snapshot = await db.ExportSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == snapshotId);
        if (snapshot is null) return NotFound();
        var path = Path.Combine(paths.DataDirectory, "exports", snapshot.FileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return File(await System.IO.File.ReadAllBytesAsync(path), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", snapshot.FileName);
    }

    private async Task LoadAsync(int? periodId)
    {
        Periods = await db.WagePeriods.AsNoTracking().OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).ToListAsync();
        SelectedPeriod = periodId.HasValue ? Periods.SingleOrDefault(x => x.Id == periodId) : Periods.FirstOrDefault();
        if (SelectedPeriod is null) return;
        Report = await calculationService.CalculateAsync(SelectedPeriod.Id);
        Snapshots = await db.ExportSnapshots.AsNoTracking()
            .Where(x => x.WagePeriodId == SelectedPeriod.Id)
            .OrderByDescending(x => x.Version)
            .ToListAsync();
    }
}

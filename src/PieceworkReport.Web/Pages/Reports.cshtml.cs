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
    ApplicationPaths paths,
    OperationAuditService operationAuditService,
    ILogger<ReportsModel> logger) : PageModel
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
            Directory.CreateDirectory(paths.ExportDirectory);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(paths.ExportDirectory, fileName), package.Content);

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
            operationAuditService.Record("导出工资报表", User.Identity?.Name ?? "unknown", $"工资月份 {period.DisplayName}，版本 {version}");
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
        var path = GetSnapshotPath(snapshot.FileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return File(await System.IO.File.ReadAllBytesAsync(path), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", snapshot.FileName);
    }

    public async Task<IActionResult> OnPostDeleteSnapshotAsync(int snapshotId, int periodId)
    {
        var snapshot = await db.ExportSnapshots.SingleOrDefaultAsync(x => x.Id == snapshotId && x.WagePeriodId == periodId);
        if (snapshot is null) return NotFound();
        var path = GetSnapshotPath(snapshot.FileName);
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Failed to delete export snapshot file {FileName}.", snapshot.FileName);
            ModelState.AddModelError(string.Empty, "导出文件正在使用，无法删除。请关闭文件后重试。");
            await LoadAsync(periodId);
            return Page();
        }

        db.ExportSnapshots.Remove(snapshot);
        operationAuditService.Record("删除工资报表导出", User.Identity?.Name ?? "unknown", $"工资月份 ID {periodId}，导出版本 {snapshot.Version}");
        await db.SaveChangesAsync();
        return RedirectToPage(new { periodId });
    }

    private string GetSnapshotPath(string fileName)
    {
        var exportRoot = Path.GetFullPath(paths.ExportDirectory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(paths.ExportDirectory, fileName));
        if (!path.StartsWith(exportRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("导出文件路径无效。");
        return path;
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

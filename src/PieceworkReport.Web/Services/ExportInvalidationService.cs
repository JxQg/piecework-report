using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Web.Services;

public sealed class ExportInvalidationService(AppDbContext db)
{
    public async Task ForSpecificationsAsync(IEnumerable<int> specificationIds)
    {
        var ids = specificationIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var periodIds = await db.PricingRules
            .Where(x => x.MachineSpecificationId != null && ids.Contains(x.MachineSpecification!.MaterialSpecificationId))
            .Select(x => x.WagePeriodId)
            .Union(db.ProductionRecords.Where(x => x.MaterialSpecificationId != null && ids.Contains(x.MaterialSpecificationId.Value)).Select(x => x.WagePeriodId))
            .Distinct()
            .ToListAsync();
        await MarkPeriodsAsync(periodIds);
    }

    public async Task ForMachineAsync(int machineId)
    {
        var specificationIds = await db.MachineSpecifications
            .Where(x => x.MachineId == machineId)
            .Select(x => x.MaterialSpecificationId)
            .ToListAsync();
        await ForSpecificationsAsync(specificationIds);
    }

    public async Task ForMaterialAsync(int materialId)
    {
        var specificationIds = await db.MaterialSpecifications
            .Where(x => x.MaterialId == materialId)
            .Select(x => x.Id)
            .ToListAsync();
        await ForSpecificationsAsync(specificationIds);
    }

    public async Task MarkPeriodsAsync(IEnumerable<int> periodIds)
    {
        var ids = periodIds.Distinct().ToList();
        if (ids.Count == 0) return;
        await db.WagePeriods.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ExportOutdated, true)
            .SetProperty(x => x.UpdatedAt, DateTime.Now));
    }

    public async Task MarkAllPeriodsAsync() => await db.WagePeriods.ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.ExportOutdated, true)
        .SetProperty(x => x.UpdatedAt, DateTime.Now));
}

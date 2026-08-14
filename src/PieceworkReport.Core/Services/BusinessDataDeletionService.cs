using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Core.Services;

public sealed record DeletionResult(bool IsDeleted, string? ErrorMessage = null);

public sealed class BusinessDataDeletionService(AppDbContext db)
{
    public async Task<DeletionResult> DeleteEmployeeAsync(int id)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return new DeletionResult(false, "员工不存在或已删除。");
        if (await db.ProductionRecords.AnyAsync(x => x.EmployeeId == id) || await db.PayAdjustments.AnyAsync(x => x.EmployeeId == id) || await db.EmployeePricingOverrides.AnyAsync(x => x.EmployeeId == id))
            return new DeletionResult(false, "该员工已有计件、工资增项或达标覆盖记录，不能删除；请改为停用。");
        db.Employees.Remove(employee);
        return new DeletionResult(true);
    }

    public async Task<DeletionResult> DeleteMachineAsync(int id)
    {
        var machine = await db.Machines.SingleOrDefaultAsync(x => x.Id == id);
        if (machine is null) return new DeletionResult(false, "机器不存在或已删除。");
        if (await db.ProductionRecords.AnyAsync(x => x.MachineId == id) || await db.PricingRules.AnyAsync(x => x.MachineId == id))
            return new DeletionResult(false, "该机器已参与计件或计价，不能删除；请改为停用。");
        db.MachineSpecifications.RemoveRange(await db.MachineSpecifications.Where(x => x.MachineId == id).ToListAsync());
        db.Machines.Remove(machine);
        return new DeletionResult(true);
    }

    public async Task<DeletionResult> DeleteMaterialAsync(int id)
    {
        var material = await db.Materials.SingleOrDefaultAsync(x => x.Id == id);
        if (material is null) return new DeletionResult(false, "物料不存在或已删除。");
        if (await db.ProductionRecords.AnyAsync(x => x.MaterialId == id) || await db.PricingRules.AnyAsync(x => x.MaterialId == id))
            return new DeletionResult(false, "该物料已参与计件或计价，不能删除；请改为停用。");
        var specificationIds = await db.MaterialSpecifications.Where(x => x.MaterialId == id).Select(x => x.Id).ToListAsync();
        db.MachineSpecifications.RemoveRange(await db.MachineSpecifications.Where(x => specificationIds.Contains(x.MaterialSpecificationId)).ToListAsync());
        db.MaterialSpecifications.RemoveRange(await db.MaterialSpecifications.Where(x => x.MaterialId == id).ToListAsync());
        db.Materials.Remove(material);
        return new DeletionResult(true);
    }

    public async Task<DeletionResult> DeleteSpecificationAsync(int id)
    {
        var specification = await db.MaterialSpecifications.SingleOrDefaultAsync(x => x.Id == id);
        if (specification is null) return new DeletionResult(false, "物料规格不存在或已删除。");
        var links = await db.MachineSpecifications.Where(x => x.MaterialSpecificationId == id).ToListAsync();
        var linkIds = links.Select(x => x.Id).ToList();
        if (await db.ProductionRecords.AnyAsync(x => x.MaterialSpecificationId == id) || await db.PricingRules.AnyAsync(x => x.MachineSpecificationId.HasValue && linkIds.Contains(x.MachineSpecificationId.Value)))
            return new DeletionResult(false, "该规格已参与计件或计价，不能删除；请改为停用。");
        db.MachineSpecifications.RemoveRange(links);
        db.MaterialSpecifications.Remove(specification);
        return new DeletionResult(true);
    }

    public async Task<DeletionResult> DeleteWagePeriodAsync(int id)
    {
        var period = await db.WagePeriods.SingleOrDefaultAsync(x => x.Id == id);
        if (period is null) return new DeletionResult(false, "工资月份不存在或已删除。");
        if (await db.PricingRules.AnyAsync(x => x.WagePeriodId == id) || await db.ProductionRecords.AnyAsync(x => x.WagePeriodId == id) || await db.PayAdjustments.AnyAsync(x => x.WagePeriodId == id) || await db.ExportSnapshots.AnyAsync(x => x.WagePeriodId == id))
            return new DeletionResult(false, "该工资月份已有计价、计件、增项或导出历史，不能删除。");
        db.WagePeriods.Remove(period);
        return new DeletionResult(true);
    }

    public async Task<DeletionResult> DeletePricingRuleAsync(int id, int periodId)
    {
        var rule = await db.PricingRules.Include(x => x.MachineSpecification).SingleOrDefaultAsync(x => x.Id == id && x.WagePeriodId == periodId);
        if (rule is null) return new DeletionResult(false, "计价规则不存在或不属于当前工资月份。");
        var specificationId = rule.MachineSpecification?.MaterialSpecificationId;
        if (await db.ProductionRecords.AnyAsync(x => x.WagePeriodId == periodId && x.MachineId == rule.MachineId && x.MaterialId == rule.MaterialId &&
            (x.MaterialSpecificationId == specificationId || x.MaterialSpecificationId == null)))
            return new DeletionResult(false, "已有对应计件记录，不能删除计价规则；请修改规则或先删除相关计件记录。");
        db.PricingRules.Remove(rule);
        return new DeletionResult(true);
    }
}

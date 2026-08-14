using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

public sealed class SpecificationsModel(
    AppDbContext db,
    CodeGenerationService codes,
    ExportInvalidationService invalidation,
    BusinessDataDeletionService deletionService,
    OperationAuditService operationAuditService) : PageModel
{
    public IReadOnlyList<Material> Materials { get; private set; } = [];
    public IReadOnlyList<Machine> Machines { get; private set; } = [];
    public IReadOnlyList<MaterialSpecification> Specifications { get; private set; } = [];
    [BindProperty] public SpecificationInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? editId, int? materialId)
    {
        await LoadAsync(materialId);
        if (editId is null) { Input.MaterialId = materialId ?? Materials.FirstOrDefault()?.Id ?? 0; return; }
        var entity = await db.MaterialSpecifications.AsNoTracking()
            .Include(x => x.Machines)
            .SingleOrDefaultAsync(x => x.Id == editId);
        if (entity is null) return;
        Input = new SpecificationInput
        {
            Id = entity.Id,
            Code = entity.Code,
            MaterialId = entity.MaterialId,
            Description = entity.Description,
            BuckleCount = entity.BuckleCount,
            Note = entity.Note,
            IsActive = entity.IsActive,
            MachineIds = entity.Machines.Where(x => x.IsActive).Select(x => x.MachineId).ToList()
        };
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.Description = Input.Description.Trim();
        Input.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();
        var material = await db.Materials.SingleOrDefaultAsync(x => x.Id == Input.MaterialId);
        if (material is null) ModelState.AddModelError("Input.MaterialId", "请选择有效物料。");
        if (await db.MaterialSpecifications.AnyAsync(x => x.MaterialId == Input.MaterialId && x.Description == Input.Description && x.Id != Input.Id))
            ModelState.AddModelError("Input.Description", "该物料下已存在同名规格。");
        var activeMachineIds = await db.Machines.Where(x => Input.MachineIds.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync();
        if (activeMachineIds.Count != Input.MachineIds.Distinct().Count()) ModelState.AddModelError("Input.MachineIds", "选择的机器无效或已停用。");
        if (!ModelState.IsValid || material is null) { await LoadAsync(Input.MaterialId); return Page(); }

        await using var transaction = await db.Database.BeginTransactionAsync();
        MaterialSpecification entity;
        if (Input.Id == 0)
        {
            entity = new MaterialSpecification
            {
                Code = await codes.NextSpecificationCodeAsync(material),
                MaterialId = material.Id,
                Description = Input.Description,
                BuckleCount = Input.BuckleCount,
                Note = Input.Note,
                IsActive = Input.IsActive
            };
            db.MaterialSpecifications.Add(entity);
            await db.SaveChangesAsync();
        }
        else
        {
            entity = await db.MaterialSpecifications.Include(x => x.Machines).SingleAsync(x => x.Id == Input.Id);
            entity.Description = Input.Description;
            entity.BuckleCount = Input.BuckleCount;
            entity.Note = Input.Note;
            entity.IsActive = Input.IsActive;
            await invalidation.ForSpecificationsAsync([entity.Id]);
        }

        var links = await db.MachineSpecifications.Where(x => x.MaterialSpecificationId == entity.Id).ToListAsync();
        foreach (var link in links) link.IsActive = activeMachineIds.Contains(link.MachineId);
        foreach (var machineId in activeMachineIds.Where(id => links.All(x => x.MachineId != id)))
            db.MachineSpecifications.Add(new MachineSpecification { MachineId = machineId, MaterialSpecificationId = entity.Id, IsActive = true });
        operationAuditService.Record(Input.Id == 0 ? "新增物料规格" : "修改物料规格", User.Identity?.Name ?? "unknown", $"规格 {entity.Code} · {entity.Description}");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        FlashMessage = $"规格 {entity.Code} 已保存。";
        return RedirectToPage(new { editId = entity.Id, materialId = entity.MaterialId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, int? materialId)
    {
        var result = await deletionService.DeleteSpecificationAsync(id);
        if (!result.IsDeleted)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage!);
            await LoadAsync(materialId);
            return Page();
        }

        operationAuditService.Record("删除物料规格", User.Identity?.Name ?? "unknown", $"规格 ID {id}");
        await db.SaveChangesAsync();
        FlashMessage = "物料规格已删除。";
        return RedirectToPage(new { materialId });
    }

    private async Task LoadAsync(int? materialId)
    {
        Materials = await db.Materials.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        Machines = await db.Machines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        Specifications = await db.MaterialSpecifications.AsNoTracking()
            .Where(x => materialId == null || x.MaterialId == materialId)
            .Include(x => x.Material)
            .Include(x => x.Machines).ThenInclude(x => x.Machine)
            .OrderBy(x => x.Material.Code).ThenBy(x => x.Code)
            .ToListAsync();
    }

    public sealed class SpecificationInput
    {
        public int Id { get; set; }
        public string Code { get; set; } = "保存后自动生成";
        [Range(1, int.MaxValue)] public int MaterialId { get; set; }
        [Required, MaxLength(160)] public string Description { get; set; } = string.Empty;
        [Range(typeof(decimal), "0.001", "999999", ErrorMessage = "扣数必须大于 0")] public decimal BuckleCount { get; set; }
        [MaxLength(240)] public string? Note { get; set; }
        public bool IsActive { get; set; } = true;
        public List<int> MachineIds { get; set; } = [];
    }
}

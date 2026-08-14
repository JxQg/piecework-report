using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

public sealed class MaterialsModel(AppDbContext db, CodeGenerationService codes, ExportInvalidationService invalidation, BusinessDataDeletionService deletionService, OperationAuditService operationAuditService) : PageModel
{
    public IReadOnlyList<Material> Materials { get; private set; } = [];
    [BindProperty] public MaterialInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }
    public async Task OnGetAsync(int? editId)
    {
        await LoadAsync(); if (editId is null) return;
        var entity = Materials.SingleOrDefault(x => x.Id == editId);
        if (entity is not null) Input = new MaterialInput { Id = entity.Id, Code = entity.Code, Name = entity.Name, IsActive = entity.IsActive };
    }
    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.Name = Input.Name.Trim();
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        Material entity;
        if (Input.Id == 0) { entity = new Material { Code = await codes.NextMaterialCodeAsync(), Name = Input.Name, IsActive = Input.IsActive, LegacySpecification = "-", LegacyBuckleCount = 0 }; db.Materials.Add(entity); }
        else { entity = await db.Materials.SingleAsync(x => x.Id == Input.Id); entity.Name = Input.Name; entity.IsActive = Input.IsActive; await invalidation.ForMaterialAsync(entity.Id); }
        operationAuditService.Record(Input.Id == 0 ? "新增物料" : "修改物料", User.Identity?.Name ?? "unknown", $"物料 {entity.Code} · {entity.Name}");
        await db.SaveChangesAsync(); FlashMessage = $"物料 {entity.Code} 已保存。"; return RedirectToPage(new { editId = entity.Id });
    }
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await deletionService.DeleteMaterialAsync(id);
        if (!result.IsDeleted) { ModelState.AddModelError(string.Empty, result.ErrorMessage!); await LoadAsync(); return Page(); }
        operationAuditService.Record("删除物料", User.Identity?.Name ?? "unknown", $"物料 ID {id}");
        await db.SaveChangesAsync(); FlashMessage = "物料及未使用规格已删除。"; return RedirectToPage();
    }
    private async Task LoadAsync() => Materials = await db.Materials.AsNoTracking().Include(x => x.Specifications).OrderBy(x => x.Code).ToListAsync();
    public sealed class MaterialInput { public int Id { get; set; } public string Code { get; set; } = "保存后自动生成"; [Required, MaxLength(100)] public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
}

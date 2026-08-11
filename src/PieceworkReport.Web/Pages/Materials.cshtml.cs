using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

public sealed class MaterialsModel(AppDbContext db, CodeGenerationService codes, ExportInvalidationService invalidation) : PageModel
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
        await db.SaveChangesAsync(); FlashMessage = $"物料 {entity.Code} 已保存。"; return RedirectToPage(new { editId = entity.Id });
    }
    private async Task LoadAsync() => Materials = await db.Materials.AsNoTracking().Include(x => x.Specifications).OrderBy(x => x.Code).ToListAsync();
    public sealed class MaterialInput { public int Id { get; set; } public string Code { get; set; } = "保存后自动生成"; [Required, MaxLength(100)] public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
}

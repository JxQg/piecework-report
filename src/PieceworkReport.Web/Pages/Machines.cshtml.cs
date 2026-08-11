using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

public sealed class MachinesModel(AppDbContext db, CodeGenerationService codes, ExportInvalidationService invalidation) : PageModel
{
    public IReadOnlyList<Machine> Machines { get; private set; } = [];
    [BindProperty] public MachineInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }
    public async Task OnGetAsync(int? editId)
    {
        await LoadAsync();
        if (editId is null) return;
        var entity = Machines.SingleOrDefault(x => x.Id == editId);
        if (entity is not null) Input = new MachineInput { Id = entity.Id, Code = entity.Code, Name = entity.Name, IsActive = entity.IsActive };
    }
    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.Name = Input.Name.Trim();
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        Machine entity;
        if (Input.Id == 0) { entity = new Machine { Code = await codes.NextMachineCodeAsync(), Name = Input.Name, IsActive = Input.IsActive }; db.Machines.Add(entity); }
        else { entity = await db.Machines.SingleAsync(x => x.Id == Input.Id); entity.Name = Input.Name; entity.IsActive = Input.IsActive; await invalidation.ForMachineAsync(entity.Id); }
        await db.SaveChangesAsync();
        FlashMessage = $"机器 {entity.Code} 已保存。";
        return RedirectToPage(new { editId = entity.Id });
    }
    private async Task LoadAsync() => Machines = await db.Machines.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
    public sealed class MachineInput { public int Id { get; set; } public string Code { get; set; } = "保存后自动生成"; [Required, MaxLength(80)] public string Name { get; set; } = string.Empty; public bool IsActive { get; set; } = true; }
}

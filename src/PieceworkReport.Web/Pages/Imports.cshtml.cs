using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Web.Pages;

[RequestSizeLimit(20 * 1024 * 1024)]
public sealed class ImportsModel(ExcelService excelService, ApplicationPaths paths) : PageModel
{
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string? Token { get; set; }
    [BindProperty] public string Kind { get; set; } = "production";
    public ImportPreviewResult? ProductionPreview { get; private set; }
    public SpecificationImportPreviewResult? SpecificationPreview { get; private set; }
    public EmployeeImportPreviewResult? EmployeePreview { get; private set; }
    [TempData] public string? FlashMessage { get; set; }

    public void OnGet(string? kind) => Kind = NormalizeKind(kind);

    public async Task<IActionResult> OnGetTemplateAsync(string? kind)
    {
        var normalized = NormalizeKind(kind);
        var content = normalized switch
        {
            "specification" => await excelService.CreateSpecificationTemplateAsync(),
            "employee" => await excelService.CreateEmployeeTemplateAsync(),
            _ => await excelService.CreateImportTemplateAsync()
        };
        var fileName = normalized switch { "specification" => "物料规格导入模板.xlsx", "employee" => "员工导入模板.xlsx", _ => "计件导入模板.xlsx" };
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        Kind = NormalizeKind(Kind);
        if (Upload is null || Upload.Length == 0) { ModelState.AddModelError(string.Empty, "请选择需要导入的 Excel 文件。"); return Page(); }
        if (!string.Equals(Path.GetExtension(Upload.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) { ModelState.AddModelError(string.Empty, "仅支持 .xlsx 文件。"); return Page(); }
        Token = Guid.NewGuid().ToString("N"); var path = ImportPath(Token);
        await using (var stream = System.IO.File.Create(path)) await Upload.CopyToAsync(stream);
        try
        {
            if (Kind == "specification") SpecificationPreview = await excelService.PreviewSpecificationImportAsync(path);
            else if (Kind == "employee") EmployeePreview = await excelService.PreviewEmployeeImportAsync(path);
            else ProductionPreview = await excelService.PreviewImportAsync(path);
        }
        catch (Exception exception)
        {
            System.IO.File.Delete(path); Token = null; ModelState.AddModelError(string.Empty, $"无法读取 Excel：{exception.Message}");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        Kind = NormalizeKind(Kind);
        if (!Guid.TryParseExact(Token, "N", out _)) { ModelState.AddModelError(string.Empty, "导入预览已失效，请重新选择文件。"); return Page(); }
        var path = ImportPath(Token!); if (!System.IO.File.Exists(path)) { ModelState.AddModelError(string.Empty, "导入预览已失效，请重新选择文件。"); return Page(); }
        try
        {
            var count = Kind switch
            {
                "specification" => await excelService.CommitSpecificationImportAsync(path),
                "employee" => await excelService.CommitEmployeeImportAsync(path),
                _ => await excelService.CommitImportAsync(path, User.Identity?.Name ?? "unknown")
            };
            FlashMessage = Kind switch { "specification" => $"已导入 {count} 个物料规格。", "employee" => $"已处理 {count} 名员工。", _ => $"已导入 {count} 条计件记录。" };
            return RedirectToPage(new { kind = Kind });
        }
        catch (InvalidOperationException exception)
        {
            if (Kind == "specification") SpecificationPreview = await excelService.PreviewSpecificationImportAsync(path);
            else if (Kind == "employee") EmployeePreview = await excelService.PreviewEmployeeImportAsync(path);
            else ProductionPreview = await excelService.PreviewImportAsync(path);
            ModelState.AddModelError(string.Empty, exception.Message); return Page();
        }
        finally { System.IO.File.Delete(path); }
    }

    private string ImportPath(string token) { var directory = Path.Combine(paths.DataDirectory, "imports"); Directory.CreateDirectory(directory); return Path.Combine(directory, $"{token}.xlsx"); }
    private static string NormalizeKind(string? kind) => kind?.ToLowerInvariant() switch { "specification" => "specification", "employee" => "employee", _ => "production" };
}

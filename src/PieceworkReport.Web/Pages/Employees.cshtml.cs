using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Web.Pages;

public sealed class EmployeesModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<Employee> Employees { get; private set; } = [];
    [BindProperty] public EmployeeInput Input { get; set; } = new();
    [TempData] public string? FlashMessage { get; set; }

    public async Task OnGetAsync(int? editId)
    {
        await LoadAsync();
        if (editId is null) return;
        var employee = Employees.SingleOrDefault(x => x.Id == editId);
        if (employee is not null) Input = new EmployeeInput { Id = employee.Id, Code = employee.Code, Name = employee.Name, IsActive = employee.IsActive };
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.Code = Input.Code.Trim().ToUpperInvariant();
        Input.Name = Input.Name.Trim();
        if (await db.Employees.AnyAsync(x => x.Code == Input.Code && x.Id != Input.Id)) ModelState.AddModelError("Input.Code", "员工编码已存在。");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        Employee employee;
        if (Input.Id == 0)
        {
            employee = new Employee { Code = Input.Code, Name = Input.Name, IsActive = Input.IsActive };
            db.Employees.Add(employee);
        }
        else
        {
            employee = await db.Employees.SingleAsync(x => x.Id == Input.Id);
            employee.Name = Input.Name;
            employee.IsActive = Input.IsActive;
        }
        await db.SaveChangesAsync();
        FlashMessage = $"员工 {employee.Name} 已保存。";
        return RedirectToPage(new { editId = employee.Id });
    }

    private async Task LoadAsync() => Employees = await db.Employees.AsNoTracking().OrderBy(x => x.Code).ToListAsync();

    public sealed class EmployeeInput
    {
        public int Id { get; set; }
        [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
        [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}

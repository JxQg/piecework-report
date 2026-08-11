using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PieceworkReport.Web.Pages;

public sealed class SetupModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage(User.IsInRole("Manager") ? "/Pricing" : "/Employees");
}

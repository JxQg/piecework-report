using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Web.Pages;

[Authorize(Roles = "Manager")]
public sealed class OperationLogsModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<SecurityAuditEntry> Entries { get; private set; } = [];

    public async Task OnGetAsync() =>
        Entries = await db.SecurityAuditEntries.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(200).ToListAsync();
}

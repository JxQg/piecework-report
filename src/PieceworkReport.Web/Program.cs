using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var configuredDataDirectory = builder.Configuration["DataDirectory"];
var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Path.Combine(builder.Environment.ContentRootPath, "data")
    : Path.GetFullPath(configuredDataDirectory, builder.Environment.ContentRootPath);
var applicationPaths = new ApplicationPaths(dataDirectory);
applicationPaths.EnsureDirectories();

builder.Services.AddSingleton(applicationPaths);
builder.Services.AddScoped<OperationAuditService>();
builder.Services.AddScoped<BusinessDataDeletionService>();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(ProductInformation.ProductName)
    .PersistKeysToFileSystem(new DirectoryInfo(applicationPaths.KeyDirectory));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(applicationPaths.ConnectionString));
builder.Services.AddScoped<WageCalculationService>();
builder.Services.AddScoped<ExcelService>();
builder.Services.AddScoped<CodeGenerationService>();
builder.Services.AddScoped<ExportInvalidationService>();
builder.Services.AddSingleton(new DatabaseBackupService(applicationPaths.ConnectionString, applicationPaths.DataDirectory));
builder.Services.AddHostedService<DailyBackupService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(10);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(AccountClaims.SecurityStamp);
        if (!int.TryParse(idText, out var id) || string.IsNullOrWhiteSpace(stamp))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var valid = await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive && x.SecurityStamp == stamp);
        if (!valid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/ready", () => Results.Json(new
{
    status = "ready"
})).AllowAnonymous();

var launcherToken = Environment.GetEnvironmentVariable("PIECEWORK_LAUNCHER_TOKEN");
if (!string.IsNullOrWhiteSpace(launcherToken))
{
    app.MapPost("/internal/launcher/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
    {
        if (context.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress)) return Results.NotFound();
        var supplied = context.Request.Headers["X-Launcher-Token"].ToString();
        if (!SecureEquals(launcherToken, supplied)) return Results.Unauthorized();
        lifetime.StopApplication();
        return Results.Accepted();
    }).AllowAnonymous();
}

app.MapRazorPages();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SchemaMigrator.UpgradeAsync(db, applicationPaths.DatabasePath, applicationPaths.BackupDirectory);
}

app.Run();

static bool SecureEquals(string expected, string supplied)
{
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
}

public partial class Program;

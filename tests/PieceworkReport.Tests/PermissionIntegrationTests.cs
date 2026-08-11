using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Tests;

public sealed class PermissionIntegrationTests
{
    [Fact]
    public async Task ReadyHealthCheck_OnlyExposesRunningState()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/health/ready");

        Assert.Equal("{\"status\":\"ready\"}", json);
    }

    [Fact]
    public async Task ClerkCannotReachMoneyPagesOrReceiveMoneyHtml()
    {
        await using var factory = new TestAppFactory(); var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var periodId = await factory.SeedPricedPeriodAsync(); client.DefaultRequestHeaders.Add("X-Test-Role", "Clerk");
        foreach (var path in new[] { "/Periods", "/Pricing", "/Adjustments", "/Reports", "/Reports?handler=Download&snapshotId=1" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/Periods?handler=Save", new FormUrlEncodedContent([]))).StatusCode);
        foreach (var path in new[] { "/Employees", "/Machines", "/Materials", "/Specifications", "/Imports" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        var home = await client.GetStringAsync($"/?periodId={periodId}");
        Assert.DoesNotContain("¥", home); Assert.DoesNotContain("上级预算", home); Assert.DoesNotContain("实际计件工资", home); Assert.DoesNotContain("预算偏差", home);
        var production = await client.GetStringAsync($"/Production?periodId={periodId}&workDate=2026-07-01");
        Assert.DoesNotContain("每件单价", production); Assert.DoesNotContain("当日计件工资", production); Assert.DoesNotContain("当日达标率", production);
    }

    [Fact]
    public async Task ManagerCanReachAllMoneyPages()
    {
        await using var factory = new TestAppFactory(); var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }); client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        foreach (var path in new[] { "/Periods", "/Pricing", "/Adjustments", "/Reports" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }
}

internal sealed class TestAppFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "piecework-web-tests", Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory); builder.UseSetting("DataDirectory", _directory);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
            services.PostConfigure<AuthenticationOptions>(options => { options.DefaultAuthenticateScheme = "Test"; options.DefaultChallengeScheme = "Test"; options.DefaultForbidScheme = "Test"; });
        });
    }
    public async Task<int> SeedPricedPeriodAsync()
    {
        using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = await db.Employees.SingleOrDefaultAsync();
        if (employee is null)
        {
            employee = new Employee { Code = "E001", Name = "测试员工" };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
        }
        var period = new WagePeriod { Year = 2026, Month = 7, Budget = 88_000m, PlannedWorkdays = 1, PlannedHeadcount = 16 };
        var machine = new Machine { Code = "M0001", Name = "一号机" }; var material = new Material { Code = "P000001", Name = "测试物料", LegacySpecification = "-" };
        db.AddRange(period, machine, material); await db.SaveChangesAsync(); var specification = new MaterialSpecification { Code = "P000001-S0001", MaterialId = material.Id, Description = "四扣", BuckleCount = 4m }; db.Add(specification); await db.SaveChangesAsync();
        var link = new MachineSpecification { MachineId = machine.Id, MaterialSpecificationId = specification.Id }; db.Add(link); db.WagePeriodWorkdays.Add(new WagePeriodWorkday { WagePeriodId = period.Id, WorkDate = new DateTime(2026, 7, 1) }); await db.SaveChangesAsync();
        db.PricingRules.Add(new PricingRule { WagePeriodId = period.Id, MachineSpecificationId = link.Id, MachineId = machine.Id, MaterialId = material.Id, Mode = PricingMode.AttainmentBased, TargetDailyWage = 260m, DefaultTargetBuckleCount = 20_000m });
        db.ProductionRecords.Add(new ProductionRecord { WagePeriodId = period.Id, WorkDate = new DateTime(2026, 7, 1), EmployeeId = employee.Id, MachineId = machine.Id, MaterialId = material.Id, MaterialSpecificationId = specification.Id, Quantity = 100m, UpdatedBy = "test" }); await db.SaveChangesAsync(); return period.Id;
    }
    public override async ValueTask DisposeAsync() { await base.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}

internal sealed class HeaderAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new[] { new Claim(ClaimTypes.Name, "test-user"), new Claim(ClaimTypes.Role, role.ToString()) };
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}

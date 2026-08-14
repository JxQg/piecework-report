using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

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
        foreach (var path in new[] { "/Periods", "/Pricing", "/Adjustments", "/Reports", "/OperationLogs", "/Reports?handler=Download&snapshotId=1" })
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
        foreach (var path in new[] { "/Periods", "/Pricing", "/Adjustments", "/Reports", "/OperationLogs" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task PricingSave_PersistsRuleAndOperationAudit()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        var periodId = await factory.SeedPricedPeriodAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PricingRules.SingleAsync(x => x.WagePeriodId == periodId);
        var token = await GetAntiforgeryTokenAsync(client, periodId);

        var response = await client.PostAsync("/Pricing?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Id"] = rule.Id.ToString(),
            ["Input.PeriodId"] = periodId.ToString(),
            ["Input.MachineSpecificationId"] = rule.MachineSpecificationId!.Value.ToString(),
            ["Input.Mode"] = ((int)PricingMode.AttainmentBased).ToString(),
            ["Input.TargetDailyWage"] = "265",
            ["Input.DefaultTargetBuckleCount"] = "20000",
            ["Input.DirectPieceRate"] = string.Empty,
            ["Input.Note"] = "已调整"
        }));

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, responseBody);
        db.ChangeTracker.Clear();
        Assert.Equal(265m, (await db.PricingRules.SingleAsync(x => x.Id == rule.Id)).TargetDailyWage);
        Assert.Contains(await db.SecurityAuditEntries.ToListAsync(), x => x.EventType == "修改计价规则" && x.Username == "test-user");
    }

    [Fact]
    public async Task PricingSave_WhenInvalid_ShowsValidationError()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        var periodId = await factory.SeedPricedPeriodAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PricingRules.SingleAsync(x => x.WagePeriodId == periodId);
        var token = await GetAntiforgeryTokenAsync(client, periodId);

        var response = await client.PostAsync("/Pricing?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Id"] = rule.Id.ToString(),
            ["Input.PeriodId"] = periodId.ToString(),
            ["Input.MachineSpecificationId"] = rule.MachineSpecificationId!.Value.ToString(),
            ["Input.Mode"] = ((int)PricingMode.AttainmentBased).ToString(),
            ["Input.TargetDailyWage"] = "265",
            ["Input.DefaultTargetBuckleCount"] = "-1",
            ["Input.DirectPieceRate"] = string.Empty,
            ["Input.Note"] = string.Empty
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("默认达标扣数必须大于 0。", System.Net.WebUtility.HtmlDecode(responseBody));
    }

    [Fact]
    public async Task PricingSave_DirectModeKeepsReferenceTargetAndBuckleRateVisible()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        var periodId = await factory.SeedPricedPeriodAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PricingRules.SingleAsync(x => x.WagePeriodId == periodId);
        var token = await GetAntiforgeryTokenAsync(client, periodId);

        var response = await client.PostAsync("/Pricing?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Id"] = rule.Id.ToString(),
            ["Input.PeriodId"] = periodId.ToString(),
            ["Input.MachineSpecificationId"] = rule.MachineSpecificationId!.Value.ToString(),
            ["Input.Mode"] = ((int)PricingMode.DirectPieceRate).ToString(),
            ["Input.TargetDailyWage"] = "265",
            ["Input.DefaultTargetBuckleCount"] = "20000",
            ["Input.DirectPieceRate"] = "0.5",
            ["Input.Note"] = string.Empty
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(265m, (await db.PricingRules.SingleAsync(x => x.Id == rule.Id)).TargetDailyWage);
        var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/Pricing?periodId={periodId}"));
        Assert.Contains("265.00", page);
        Assert.Contains("0.013250", page);
    }

    [Fact]
    public async Task DeletionService_DeletesUnusedDataAndBlocksPayrollHistory()
    {
        await using var factory = new TestAppFactory();
        var periodId = await factory.SeedPricedPeriodAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deletionService = scope.ServiceProvider.GetRequiredService<BusinessDataDeletionService>();
        var unused = new Employee { Code = "E900", Name = "待删除员工" };
        db.Employees.Add(unused);
        await db.SaveChangesAsync();

        Assert.True((await deletionService.DeleteEmployeeAsync(unused.Id)).IsDeleted);
        await db.SaveChangesAsync();
        Assert.False(await db.Employees.AnyAsync(x => x.Id == unused.Id));

        var usedEmployee = await db.ProductionRecords.Where(x => x.WagePeriodId == periodId).Select(x => x.EmployeeId).SingleAsync();
        var rule = await db.PricingRules.SingleAsync(x => x.WagePeriodId == periodId);
        Assert.False((await deletionService.DeleteEmployeeAsync(usedEmployee)).IsDeleted);
        Assert.False((await deletionService.DeleteWagePeriodAsync(periodId)).IsDeleted);
        Assert.False((await deletionService.DeletePricingRuleAsync(rule.Id, periodId)).IsDeleted);
    }

    [Fact]
    public async Task ProductionEdit_PersistsAndAuditsExplicitUpdate()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Clerk");
        var periodId = await factory.SeedPricedPeriodAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.ProductionRecords.SingleAsync(x => x.WagePeriodId == periodId);
        var linkId = await db.MachineSpecifications.Where(x => x.MachineId == record.MachineId && x.MaterialSpecificationId == record.MaterialSpecificationId).Select(x => x.Id).SingleAsync();
        var token = await GetAntiforgeryTokenAsync(client, $"/Production?periodId={periodId}&workDate=2026-07-01");

        var response = await client.PostAsync("/Production?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Id"] = record.Id.ToString(),
            ["Input.PeriodId"] = periodId.ToString(),
            ["Input.WorkDate"] = "2026-07-01",
            ["Input.EmployeeId"] = record.EmployeeId.ToString(),
            ["Input.MachineSpecificationId"] = linkId.ToString(),
            ["Input.Quantity"] = "125.500",
            ["Input.Note"] = "已复核"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        db.ChangeTracker.Clear();
        var updated = await db.ProductionRecords.SingleAsync(x => x.Id == record.Id);
        Assert.Equal(125.5m, updated.Quantity);
        Assert.Equal("已复核", updated.Note);
        Assert.Contains(await db.SecurityAuditEntries.ToListAsync(), x => x.EventType == "修改计件记录" && x.Username == "test-user");
    }

    [Fact]
    public async Task AdjustmentEdit_PersistsAndManagerWriteActionsAreProtected()
    {
        await using var factory = new TestAppFactory();
        var manager = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        manager.DefaultRequestHeaders.Add("X-Test-Role", "Manager");
        var periodId = await factory.SeedPricedPeriodAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employeeId = await db.ProductionRecords.Where(x => x.WagePeriodId == periodId).Select(x => x.EmployeeId).SingleAsync();
        var adjustment = new PayAdjustment { WagePeriodId = periodId, EmployeeId = employeeId, AdjustmentDate = new DateTime(2026, 7, 1), Category = "补贴", Amount = 20m, UpdatedBy = "test" };
        db.PayAdjustments.Add(adjustment);
        await db.SaveChangesAsync();
        var token = await GetAntiforgeryTokenAsync(manager, $"/Adjustments?periodId={periodId}");

        var response = await manager.PostAsync("/Adjustments?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Id"] = adjustment.Id.ToString(),
            ["Input.PeriodId"] = periodId.ToString(),
            ["Input.EmployeeId"] = employeeId.ToString(),
            ["Input.AdjustmentDate"] = "2026-07-01",
            ["Input.Category"] = "夜班补贴",
            ["Input.Amount"] = "35.00",
            ["Input.Note"] = "已核准"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        db.ChangeTracker.Clear();
        var updated = await db.PayAdjustments.SingleAsync(x => x.Id == adjustment.Id);
        Assert.Equal("夜班补贴", updated.Category);
        Assert.Equal(35m, updated.Amount);
        Assert.Contains(await db.SecurityAuditEntries.ToListAsync(), x => x.EventType == "修改工资增项" && x.Username == "test-user");

        var clerk = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        clerk.DefaultRequestHeaders.Add("X-Test-Role", "Clerk");
        foreach (var path in new[] { "/Periods?handler=Delete", "/Pricing?handler=Delete", "/Adjustments?handler=Save", "/Reports?handler=DeleteSnapshot" })
            Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, int periodId)
    {
        return await GetAntiforgeryTokenAsync(client, $"/Pricing?periodId={periodId}");
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var page = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, page);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "Pricing page did not render an antiforgery token.");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}

internal sealed class TestAppFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "piecework-web-tests", Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory); builder.UseSetting("DataDirectory", _directory); builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
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

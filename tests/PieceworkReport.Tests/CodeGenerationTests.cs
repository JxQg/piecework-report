using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Tests;

public sealed class CodeGenerationTests
{
    [Fact]
    public async Task Codes_StartAboveExistingValuesAndAreNeverReused()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Db.Machines.Add(new Machine { Code = "M0042", Name = "旧机器" });
        var material = new Material { Code = "P000007", Name = "旧物料", LegacySpecification = "-" };
        database.Db.Materials.Add(material); await database.Db.SaveChangesAsync();
        database.Db.MaterialSpecifications.Add(new MaterialSpecification { Code = "P000007-S0003", MaterialId = material.Id, Description = "旧规格", BuckleCount = 2m }); await database.Db.SaveChangesAsync();
        var service = new CodeGenerationService(database.Db);
        Assert.Equal("M0043", await service.NextMachineCodeAsync());
        Assert.Equal("P000008", await service.NextMaterialCodeAsync());
        Assert.Equal("P000007-S0004", await service.NextSpecificationCodeAsync(material));
        database.Db.Machines.RemoveRange(database.Db.Machines); await database.Db.SaveChangesAsync();
        Assert.Equal("M0044", await service.NextMachineCodeAsync());
    }

    [Fact]
    public async Task ConcurrentContextsAllocateUniqueMachineCodes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var tasks = Enumerable.Range(1, 10).Select(async index =>
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(database.ConnectionString).Options);
            var code = await new CodeGenerationService(db).NextMachineCodeAsync();
            db.Machines.Add(new Machine { Code = code, Name = $"机器{index}" }); await db.SaveChangesAsync(); return code;
        });
        var codes = await Task.WhenAll(tasks);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 10).Select(x => $"M{x:0000}"), codes.OrderBy(x => x));
    }
}

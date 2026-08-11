using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Web.Services;

public sealed class CodeGenerationService(AppDbContext db)
{
    public async Task<string> NextMachineCodeAsync()
    {
        var next = NextNumericSuffix(await db.Machines.Select(x => x.Code).ToListAsync(), "M");
        return $"M{await AllocateAsync("Machine", next):0000}";
    }

    public async Task<string> NextMaterialCodeAsync()
    {
        var next = NextNumericSuffix(await db.Materials.Select(x => x.Code).ToListAsync(), "P");
        return $"P{await AllocateAsync("Material", next):000000}";
    }

    public async Task<string> NextSpecificationCodeAsync(Material material)
    {
        var prefix = $"{material.Code}-S";
        var codes = await db.MaterialSpecifications
            .Where(x => x.MaterialId == material.Id)
            .Select(x => x.Code)
            .ToListAsync();
        var next = NextNumericSuffix(codes, prefix);
        return $"{prefix}{await AllocateAsync($"Specification:{material.Id}", next):0000}";
    }

    private async Task<int> AllocateAsync(string name, int minimumNextValue)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CodeSequences (Name, NextValue)
                VALUES ($name, $minimum + 1)
                ON CONFLICT(Name) DO UPDATE SET NextValue = MAX(CodeSequences.NextValue, $minimum) + 1
                RETURNING NextValue - 1;
                """;
            AddParameter(command, "$name", name);
            AddParameter(command, "$minimum", minimumNextValue);
            var currentTransaction = db.Database.CurrentTransaction;
            if (currentTransaction is not null) command.Transaction = currentTransaction.GetDbTransaction();
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static int NextNumericSuffix(IEnumerable<string> codes, string prefix)
    {
        var maximum = 0;
        foreach (var code in codes)
        {
            if (!code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = code[prefix.Length..];
            if (int.TryParse(suffix, out var value) && value > maximum) maximum = value;
        }

        return maximum + 1;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

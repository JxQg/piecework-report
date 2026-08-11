using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Tests;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task FreshDatabase_HasNoBusinessDataOrDefaultAccounts()
    {
        await using var database = await TestDatabase.CreateAsync();
        Assert.Empty(await database.Db.Users.ToListAsync());
        Assert.Empty(await database.Db.Employees.ToListAsync());
        Assert.Empty(await database.Db.Machines.ToListAsync());
        Assert.Empty(await database.Db.Materials.ToListAsync());
    }

    [Fact]
    public async Task InitializeChangeAndReset_EnforcePolicyAndRotateSecurityStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new AccountService(database.Db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeAsync("short", "Clerk123"));
        await service.InitializeAsync("Manager123", "Clerk123");
        var initialManager = await database.Db.Users.SingleAsync(x => x.Username == AccountService.ManagerUsername);
        var initialManagerStamp = initialManager.SecurityStamp;
        Assert.True(await service.VerifyManagerAsync("Manager123"));

        await service.ChangeManagerPasswordAsync("Manager123", "Manager456");
        database.Db.ChangeTracker.Clear();
        var changedManager = await database.Db.Users.SingleAsync(x => x.Username == AccountService.ManagerUsername);
        Assert.NotEqual(initialManagerStamp, changedManager.SecurityStamp);
        Assert.False(await service.VerifyManagerAsync("Manager123"));
        Assert.True(await service.VerifyManagerAsync("Manager456"));

        var clerkBefore = await database.Db.Users.AsNoTracking().SingleAsync(x => x.Username == AccountService.ClerkUsername);
        await service.ResetClerkPasswordAsync("Manager456", "Clerk456");
        database.Db.ChangeTracker.Clear();
        var clerkAfter = await database.Db.Users.AsNoTracking().SingleAsync(x => x.Username == AccountService.ClerkUsername);
        Assert.NotEqual(clerkBefore.SecurityStamp, clerkAfter.SecurityStamp);
        Assert.Contains(await database.Db.SecurityAuditEntries.ToListAsync(), x => x.EventType == "clerk-password-reset");
    }

    [Fact]
    public async Task LegacyDefaultPasswords_MustBeReplacedTogether()
    {
        await using var database = await TestDatabase.CreateAsync();
        var hasher = new PasswordHasher<AppUser>();
        var manager = new AppUser { Username = "manager", Role = UserRole.Manager, PasswordHash = string.Empty, SecurityStamp = Guid.NewGuid().ToString("N") };
        manager.PasswordHash = hasher.HashPassword(manager, "Manager@123");
        var clerk = new AppUser { Username = "clerk", Role = UserRole.Clerk, PasswordHash = string.Empty, SecurityStamp = Guid.NewGuid().ToString("N") };
        clerk.PasswordHash = hasher.HashPassword(clerk, "Clerk@123");
        database.Db.Users.AddRange(manager, clerk); await database.Db.SaveChangesAsync();
        var service = new AccountService(database.Db);
        Assert.True((await service.GetStateAsync()).RequiresCredentialUpgrade);
        await service.UpgradeLegacyPasswordsAsync("Manager@123", "Manager789", "Clerk789");
        Assert.False((await service.GetStateAsync()).RequiresCredentialUpgrade);
    }
}

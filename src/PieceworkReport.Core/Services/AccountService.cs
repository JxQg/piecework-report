using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Core.Services;

public static class AccountClaims
{
    public const string SecurityStamp = "piecework:security_stamp";
}

public sealed record AccountState(bool IsInitialized, bool ManagerUsesLegacyPassword, bool ClerkUsesLegacyPassword)
{
    public bool RequiresCredentialUpgrade => ManagerUsesLegacyPassword || ClerkUsesLegacyPassword;
}

public static class PasswordPolicy
{
    public static string? Validate(string password)
    {
        if (password.Length < 8) return "密码至少需要 8 位。";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "密码必须同时包含字母和数字。";
        return null;
    }
}

public sealed class AccountService(AppDbContext db)
{
    public const string ManagerUsername = "manager";
    public const string ClerkUsername = "clerk";
    private const string LegacyManagerPassword = "Manager@123";
    private const string LegacyClerkPassword = "Clerk@123";
    private readonly PasswordHasher<AppUser> _hasher = new();

    public async Task<AccountState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking().ToListAsync(cancellationToken);
        var manager = users.SingleOrDefault(x => x.Username == ManagerUsername && x.Role == UserRole.Manager);
        var clerk = users.SingleOrDefault(x => x.Username == ClerkUsername && x.Role == UserRole.Clerk);
        var initialized = manager is not null && clerk is not null;
        return new AccountState(
            initialized,
            manager is not null && Verify(manager, LegacyManagerPassword),
            clerk is not null && Verify(clerk, LegacyClerkPassword));
    }

    public async Task InitializeAsync(string managerPassword, string clerkPassword, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(managerPassword);
        ValidateNewPassword(clerkPassword);
        if (await db.Users.AnyAsync(cancellationToken)) throw new InvalidOperationException("数据库中已存在登录账号，不能重复初始化。");
        var manager = NewUser(ManagerUsername, UserRole.Manager, managerPassword);
        var clerk = NewUser(ClerkUsername, UserRole.Clerk, clerkPassword);
        db.Users.AddRange(manager, clerk);
        AddAudit("accounts-initialized", ManagerUsername, "首次建立经理和文员账号");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> VerifyManagerAsync(string password, CancellationToken cancellationToken = default)
    {
        var manager = await FindActiveAsync(ManagerUsername, cancellationToken);
        return manager is not null && Verify(manager, password);
    }

    public async Task ChangeManagerPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(newPassword);
        var manager = await FindActiveAsync(ManagerUsername, cancellationToken) ?? throw new InvalidOperationException("经理账号不存在或已停用。");
        if (!Verify(manager, currentPassword)) throw new InvalidOperationException("当前经理密码不正确。");
        SetPassword(manager, newPassword);
        AddAudit("manager-password-changed", ManagerUsername, "经理修改密码");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetClerkPasswordAsync(string managerPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(newPassword);
        if (!await VerifyManagerAsync(managerPassword, cancellationToken)) throw new InvalidOperationException("经理密码不正确。");
        var clerk = await FindActiveAsync(ClerkUsername, cancellationToken) ?? throw new InvalidOperationException("文员账号不存在或已停用。");
        SetPassword(clerk, newPassword);
        AddAudit("clerk-password-reset", ManagerUsername, "经理重置文员密码");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpgradeLegacyPasswordsAsync(string currentManagerPassword, string newManagerPassword, string newClerkPassword, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(newManagerPassword);
        ValidateNewPassword(newClerkPassword);
        var manager = await FindActiveAsync(ManagerUsername, cancellationToken) ?? throw new InvalidOperationException("经理账号不存在或已停用。");
        var clerk = await FindActiveAsync(ClerkUsername, cancellationToken) ?? throw new InvalidOperationException("文员账号不存在或已停用。");
        if (!Verify(manager, currentManagerPassword)) throw new InvalidOperationException("当前经理密码不正确。");
        var state = await GetStateAsync(cancellationToken);
        if (!state.RequiresCredentialUpgrade) throw new InvalidOperationException("账号不再使用旧版默认密码。");
        SetPassword(manager, newManagerPassword);
        SetPassword(clerk, newClerkPassword);
        AddAudit("legacy-passwords-upgraded", ManagerUsername, "替换旧版默认密码");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecoverManagerPasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(newPassword);
        var manager = await FindActiveAsync(ManagerUsername, cancellationToken) ?? throw new InvalidOperationException("经理账号不存在或已停用。");
        SetPassword(manager, newPassword);
        AddAudit("manager-password-recovered", "windows-administrator", "Windows 管理员通过本机恢复入口重置经理密码");
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AppUser?> FindActiveAsync(string username, CancellationToken cancellationToken) =>
        await db.Users.SingleOrDefaultAsync(x => x.Username == username && x.IsActive, cancellationToken);

    private AppUser NewUser(string username, UserRole role, string password)
    {
        var user = new AppUser { Username = username, Role = role, PasswordHash = string.Empty, SecurityStamp = NewSecurityStamp() };
        user.PasswordHash = _hasher.HashPassword(user, password);
        return user;
    }

    private bool Verify(AppUser user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    private void SetPassword(AppUser user, string password)
    {
        user.SecurityStamp = NewSecurityStamp();
        user.PasswordHash = _hasher.HashPassword(user, password);
    }

    private void AddAudit(string eventType, string username, string detail) =>
        db.SecurityAuditEntries.Add(new SecurityAuditEntry { EventType = eventType, Username = username, Detail = detail });

    private static void ValidateNewPassword(string password)
    {
        var error = PasswordPolicy.Validate(password);
        if (error is not null) throw new InvalidOperationException(error);
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}

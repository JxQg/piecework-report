using System.Security.Principal;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Launcher.Infrastructure;
using PieceworkReport.Launcher.Ui;

namespace PieceworkReport.Launcher;

internal static class Program
{
    private const string MutexName = @"Local\PieceworkReport.Launcher";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var paths = LauncherPaths.Create(args);
            if (args.Contains("--recover-manager", StringComparer.OrdinalIgnoreCase))
            {
                RunManagerRecovery(paths);
                return;
            }

            using var mutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("计件工资启动器已经在运行，请查看任务栏或系统托盘。", "计件工资管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PrepareDataDirectory(paths);
            paths.EnsureDirectories();
            InitializeDatabase(paths);
            EnsureAccounts(paths);

            var settingsStore = new LauncherSettingsStore(paths.SettingsPath);
            var settings = settingsStore.Load();
            var schemaVersion = ReadSchemaVersion(paths);
            var autostart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
            using var main = new MainForm(paths, settingsStore, settings, schemaVersion, autostart);
            Application.Run(main);
            main.DisposeManagerAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"启动器无法继续：{exception.Message}", "计件工资管理", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void PrepareDataDirectory(LauncherPaths paths)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        if (File.Exists(paths.CorePaths.DatabasePath)) return;
        var choice = MessageBox.Show(
            "尚未发现正式数据库。\n\n选择“是”可复制旧版 data 目录；选择“否”创建空业务库；选择“取消”退出。",
            "正式数据来源",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) throw new OperationCanceledException("已取消首次设置。");
        if (choice != DialogResult.Yes) return;
        using var folder = new FolderBrowserDialog { Description = "选择包含 piecework-report.db 的旧版 data 目录", UseDescriptionForTitle = true };
        if (folder.ShowDialog() != DialogResult.OK) throw new OperationCanceledException("已取消旧数据导入。");
        LegacyDataImporter.ImportAsync(folder.SelectedPath, paths).GetAwaiter().GetResult();
    }

    private static void InitializeDatabase(LauncherPaths paths)
    {
        using var db = paths.CorePaths.CreateDbContext();
        SchemaMigrator.UpgradeAsync(db, paths.CorePaths.DatabasePath, paths.CorePaths.BackupDirectory).GetAwaiter().GetResult();
    }

    private static int ReadSchemaVersion(LauncherPaths paths)
    {
        using var db = paths.CorePaths.CreateDbContext();
        return SchemaMigrator.GetCurrentVersionAsync(db).GetAwaiter().GetResult();
    }

    private static void EnsureAccounts(LauncherPaths paths)
    {
        using var db = paths.CorePaths.CreateDbContext();
        var accounts = new AccountService(db);
        var state = accounts.GetStateAsync().GetAwaiter().GetResult();
        if (!state.IsInitialized)
        {
            if (db.Users.Any()) throw new InvalidOperationException("登录账号数据不完整，需由 Windows 管理员恢复后再启动。");
            using var setup = new PasswordSetupForm(PasswordSetupMode.Fresh);
            if (setup.ShowDialog() != DialogResult.OK) throw new OperationCanceledException("必须先建立经理和文员账号。");
            accounts.InitializeAsync(setup.ManagerPassword, setup.ClerkPassword).GetAwaiter().GetResult();
            return;
        }
        if (!state.RequiresCredentialUpgrade) return;
        using var upgrade = new PasswordSetupForm(PasswordSetupMode.UpgradeLegacy);
        if (upgrade.ShowDialog() != DialogResult.OK) throw new OperationCanceledException("旧版默认密码必须替换后才能开放服务。");
        accounts.UpgradeLegacyPasswordsAsync(upgrade.CurrentManagerPassword, upgrade.ManagerPassword, upgrade.ClerkPassword).GetAwaiter().GetResult();
    }

    private static void RunManagerRecovery(LauncherPaths paths)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("经理密码恢复必须以 Windows 管理员身份运行。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        paths.EnsureDirectories();
        InitializeDatabase(paths);
        using var form = new PasswordRecoveryForm();
        if (form.ShowDialog() != DialogResult.OK) return;
        using var db = paths.CorePaths.CreateDbContext();
        new AccountService(db).RecoverManagerPasswordAsync(form.NewPassword).GetAwaiter().GetResult();
        MessageBox.Show("经理密码已重置，已有 Web 登录会话将在下次请求时失效。", "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

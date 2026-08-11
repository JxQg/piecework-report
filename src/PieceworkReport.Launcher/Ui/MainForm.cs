using System.Diagnostics;
using System.Net.NetworkInformation;
using PieceworkReport.Core.Services;
using PieceworkReport.Launcher.Infrastructure;

namespace PieceworkReport.Launcher.Ui;

internal sealed class MainForm : Form
{
    private readonly LauncherPaths _paths;
    private readonly LauncherSettingsStore _settingsStore;
    private readonly LauncherSettings _settings;
    private readonly AutoStartService _autoStart = new();
    private readonly WebProcessManager _web;
    private readonly bool _autostartLaunch;
    private readonly Label _stateLabel = new();
    private readonly Label _errorLabel = new();
    private readonly TextBox _localAddress = new();
    private readonly ComboBox _lanAddresses = new();
    private readonly NumericUpDown _port = new();
    private readonly CheckBox _autoStartCheck = new();
    private readonly Button _startButton = UiStyle.PrimaryButton("启动服务");
    private readonly Button _stopButton = UiStyle.SecondaryButton("停止服务");
    private readonly Button _openButton = UiStyle.SecondaryButton("打开本地页面");
    private readonly Button _copyButton = UiStyle.SecondaryButton("复制局域网链接");
    private readonly Button _qrButton = UiStyle.SecondaryButton("二维码分享");
    private readonly TextBox _managerCurrent = UiStyle.PasswordBox();
    private readonly TextBox _managerNew = UiStyle.PasswordBox();
    private readonly TextBox _managerConfirm = UiStyle.PasswordBox();
    private readonly TextBox _clerkManager = UiStyle.PasswordBox();
    private readonly TextBox _clerkNew = UiStyle.PasswordBox();
    private readonly TextBox _clerkConfirm = UiStyle.PasswordBox();
    private readonly Label _candidateVersion = new();
    private readonly Label _candidateHash = new();
    private readonly NotifyIcon _notifyIcon;
    private UpdatePackage? _updatePackage;
    private bool _allowExit;
    private bool _trayNoticeShown;

    public MainForm(LauncherPaths paths, LauncherSettingsStore settingsStore, LauncherSettings settings, int schemaVersion, bool autostartLaunch)
    {
        _paths = paths;
        _settingsStore = settingsStore;
        _settings = settings;
        _autostartLaunch = autostartLaunch;
        _web = new WebProcessManager(paths, settings.Port);
        _web.StateChanged += (_, _) => RunOnUi(UpdateServiceState);

        Text = "计件工资管理启动器";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiStyle.Background;
        ForeColor = UiStyle.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 650);
        ClientSize = new Size(980, 720);

        Controls.Add(BuildTabs(schemaVersion));
        Controls.Add(BuildHeader());
        _notifyIcon = BuildNotifyIcon();
        _port.Value = settings.Port;
        _autoStartCheck.Checked = _autoStart.IsEnabled();
        RefreshAddresses();
        UpdateServiceState();

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        Shown += async (_, _) =>
        {
            if (_autostartLaunch) BeginInvoke(HideToTray);
            await StartServiceAsync();
        };
        FormClosing += OnFormClosing;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = UiStyle.Surface, Padding = new Padding(28, 18, 28, 14) };
        var title = new Label { Text = "计件工资管理", AutoSize = true, Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = UiStyle.Ink, Location = new Point(28, 16) };
        var subtitle = new Label { Text = $"启动器 {ProductInformation.Version} · 本机局域网服务", AutoSize = true, ForeColor = UiStyle.Muted, Location = new Point(30, 54) };
        _stateLabel.AutoSize = true; _stateLabel.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold); _stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right; _stateLabel.Location = new Point(ClientSize.Width - 150, 33);
        header.Resize += (_, _) => _stateLabel.Location = new Point(header.ClientSize.Width - _stateLabel.Width - 30, 34);
        header.Controls.AddRange([title, subtitle, _stateLabel]);
        return header;
    }

    private Control BuildTabs(int schemaVersion)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8), Font = new Font(Font.FontFamily, 10F) };
        tabs.TabPages.Add(BuildServiceTab());
        tabs.TabPages.Add(BuildAccountTab());
        tabs.TabPages.Add(BuildVersionTab(schemaVersion));
        return tabs;
    }

    private TabPage BuildServiceTab()
    {
        var page = NewTab("服务与访问");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "本机访问", _localAddress);
        _localAddress.ReadOnly = true; _localAddress.Dock = DockStyle.Fill;
        _lanAddresses.DropDownStyle = ComboBoxStyle.DropDownList; _lanAddresses.Dock = DockStyle.Fill; _lanAddresses.SelectedIndexChanged += (_, _) => SaveSelectedAddress();
        AddRow(layout, 1, "局域网地址", _lanAddresses);
        _port.Minimum = 1024; _port.Maximum = 65535; _port.Width = 160;
        AddRow(layout, 2, "服务端口", _port);
        _autoStartCheck.Text = "Windows 登录后进入托盘并自动启动服务"; _autoStartCheck.AutoSize = true;
        AddRow(layout, 3, "开机启动", _autoStartCheck);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 14, 0, 8) };
        actions.Controls.AddRange([_startButton, _stopButton, _openButton, _copyButton, _qrButton]);
        layout.Controls.Add(actions, 0, 4); layout.SetColumnSpan(actions, 2);
        var save = UiStyle.PrimaryButton("保存端口与启动设置"); save.Click += async (_, _) => await SaveServiceSettingsAsync();
        var settingsActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) }; settingsActions.Controls.Add(save);
        layout.Controls.Add(settingsActions, 0, 5); layout.SetColumnSpan(settingsActions, 2);
        _errorLabel.AutoSize = true; _errorLabel.MaximumSize = new Size(800, 0); _errorLabel.ForeColor = UiStyle.Danger; _errorLabel.Margin = new Padding(0, 14, 0, 0);
        layout.Controls.Add(_errorLabel, 0, 6); layout.SetColumnSpan(_errorLabel, 2);
        _startButton.Click += async (_, _) => await StartServiceAsync();
        _stopButton.Click += async (_, _) => await StopServiceWithAuthorizationAsync();
        _openButton.Click += async (_, _) => await OpenLocalAsync();
        _copyButton.Click += (_, _) => CopyLanUrl();
        _qrButton.Click += (_, _) => ShowQr();
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildAccountTab()
    {
        var page = NewTab("账号管理");
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 2 };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.Controls.Add(BuildPasswordGroup("修改经理密码", [
            ("当前经理密码", _managerCurrent), ("新经理密码", _managerNew), ("确认新密码", _managerConfirm)
        ], "修改经理密码", ChangeManagerPasswordAsync), 0, 0);
        columns.Controls.Add(BuildPasswordGroup("重置文员密码", [
            ("经理密码", _clerkManager), ("文员新密码", _clerkNew), ("确认新密码", _clerkConfirm)
        ], "重置文员密码", ResetClerkPasswordAsync), 1, 0);
        var recovery = UiStyle.SecondaryButton("忘记经理密码：Windows 管理员恢复"); recovery.Click += async (_, _) => await StartRecoveryAsync();
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(26, 10, 26, 10) }; footer.Controls.Add(recovery);
        page.Controls.Add(columns); page.Controls.Add(footer);
        return page;
    }

    private TabPage BuildVersionTab(int schemaVersion)
    {
        var page = NewTab("版本与升级");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(30), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "当前程序版本", UiStyle.Label(ProductInformation.Version));
        AddRow(layout, 1, "数据库结构版本", UiStyle.Label(schemaVersion.ToString()));
        AddRow(layout, 2, "构建时间 (UTC)", UiStyle.Label(ProductInformation.BuildDateUtc));
        _candidateVersion.Text = "尚未选择安装包"; _candidateVersion.AutoSize = true;
        AddRow(layout, 3, "候选版本", _candidateVersion);
        _candidateHash.Text = "-"; _candidateHash.AutoSize = true; _candidateHash.MaximumSize = new Size(650, 0);
        AddRow(layout, 4, "SHA-256", _candidateHash);
        var choose = UiStyle.SecondaryButton("选择安装包"); choose.Click += async (_, _) => await SelectUpdatePackageAsync();
        var upgrade = UiStyle.PrimaryButton("备份并升级"); upgrade.Click += async (_, _) => await UpgradeAsync();
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 18, 0, 0) }; actions.Controls.AddRange([choose, upgrade]);
        layout.Controls.Add(actions, 0, 5); layout.SetColumnSpan(actions, 2);
        var note = UiStyle.Label("升级仅替换 Program Files 中的程序文件，C:\\ProgramData\\PieceworkReport\\data 不会被安装包覆盖。", true); note.MaximumSize = new Size(720, 0);
        layout.Controls.Add(note, 0, 6); layout.SetColumnSpan(note, 2);
        page.Controls.Add(layout);
        return page;
    }

    private GroupBox BuildPasswordGroup(string title, IReadOnlyList<(string Label, TextBox Input)> rows, string buttonText, Func<Task> action)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(20), Margin = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        foreach (var row in rows) { layout.Controls.Add(UiStyle.Label(row.Label)); row.Input.Dock = DockStyle.Top; layout.Controls.Add(row.Input); }
        var button = UiStyle.PrimaryButton(buttonText); button.Margin = new Padding(0, 20, 0, 0); button.Click += async (_, _) => await action();
        layout.Controls.Add(button); group.Controls.Add(layout); return group;
    }

    private async Task StartServiceAsync()
    {
        if (_web.IsRunning) return;
        await _web.StartAsync();
    }

    private async Task StopServiceWithAuthorizationAsync()
    {
        if (!await AuthorizeManagerAsync("停止局域网 Web 服务")) return;
        await _web.StopAsync();
    }

    private async Task OpenLocalAsync()
    {
        if (!await EnsureRunningAsync()) return;
        Process.Start(new ProcessStartInfo(LocalUrl) { UseShellExecute = true });
    }

    private async Task<bool> EnsureRunningAsync()
    {
        if (_web.IsRunning) return true;
        return await _web.StartAsync();
    }

    private async Task SaveServiceSettingsAsync()
    {
        if (!await AuthorizeManagerAsync("修改端口或开机启动设置")) return;
        var oldPort = _settings.Port;
        var newPort = decimal.ToInt32(_port.Value);
        var wasRunning = _web.IsRunning;
        if (newPort != oldPort)
        {
            if (wasRunning) await _web.StopAsync();
            if (!PortProbe.IsAvailable(newPort))
            {
                MessageBox.Show(this, $"端口 {newPort} 已被占用，设置未保存。", "端口冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (wasRunning) await _web.StartAsync();
                return;
            }
            _settings.Port = newPort; _settingsStore.Save(_settings); _web.UpdatePort(newPort); RefreshUrls();
            if (wasRunning && !await _web.StartAsync())
            {
                _settings.Port = oldPort; _settingsStore.Save(_settings); _web.UpdatePort(oldPort); _port.Value = oldPort; RefreshUrls();
                await _web.StartAsync();
                MessageBox.Show(this, "新端口启动失败，已恢复原端口。", "已回滚", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        _autoStart.SetEnabled(_autoStartCheck.Checked);
        MessageBox.Show(this, "服务设置已保存。", "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ChangeManagerPasswordAsync()
    {
        if (_managerNew.Text != _managerConfirm.Text) { Warn("两次输入的新经理密码不一致。"); return; }
        try
        {
            await using var db = _paths.CorePaths.CreateDbContext();
            await new AccountService(db).ChangeManagerPasswordAsync(_managerCurrent.Text, _managerNew.Text);
            Clear([_managerCurrent, _managerNew, _managerConfirm]);
            MessageBox.Show(this, "经理密码已修改，已有经理登录会话将立即失效。", "修改完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidOperationException exception) { Warn(exception.Message); }
    }

    private async Task ResetClerkPasswordAsync()
    {
        if (_clerkNew.Text != _clerkConfirm.Text) { Warn("两次输入的文员新密码不一致。"); return; }
        try
        {
            await using var db = _paths.CorePaths.CreateDbContext();
            await new AccountService(db).ResetClerkPasswordAsync(_clerkManager.Text, _clerkNew.Text);
            Clear([_clerkManager, _clerkNew, _clerkConfirm]);
            MessageBox.Show(this, "文员密码已重置，已有文员登录会话将立即失效。", "重置完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (InvalidOperationException exception) { Warn(exception.Message); }
    }

    private async Task StartRecoveryAsync()
    {
        if (MessageBox.Show(this, "恢复入口会停止 Web 服务并请求 Windows 管理员权限。是否继续？", "恢复经理密码", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await _web.StopAsync();
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--recover-manager"); start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(_paths.RootDirectory);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动密码恢复窗口。");
            await process.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223) { }
        catch (Exception exception) { Warn(exception.Message); }
    }

    private async Task SelectUpdatePackageAsync()
    {
        using var dialog = new OpenFileDialog { Title = "选择计件工资管理安装包", Filter = "安装程序 (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _updatePackage = await UpdatePackageInspector.InspectAsync(dialog.FileName);
            _candidateVersion.Text = _updatePackage.IsSameVersion ? $"{_updatePackage.Version}（修复安装）" : _updatePackage.Version.ToString();
            _candidateHash.Text = _updatePackage.Sha256;
        }
        catch (Exception exception) { _updatePackage = null; _candidateVersion.Text = "安装包无效"; _candidateHash.Text = "-"; Warn(exception.Message); }
    }

    private async Task UpgradeAsync()
    {
        if (_updatePackage is null) { Warn("请先选择有效的升级安装包。"); return; }
        if (_updatePackage.IsSameVersion && MessageBox.Show(this, "所选安装包与当前版本相同，将执行修复安装。是否继续？", "修复安装", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!await AuthorizeManagerAsync("备份正式数据库并启动程序升级")) return;
        try
        {
            await new DatabaseBackupService(_paths.CorePaths.ConnectionString, _paths.DataDirectory).CreateBackupAsync("before-upgrade");
            await _web.StopAsync();
            var start = new ProcessStartInfo(_updatePackage.Path) { UseShellExecute = true, Verb = "runas" };
            Process.Start(start);
            _allowExit = true; _notifyIcon.Visible = false; Close();
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223) { Warn("已取消 Windows 管理员授权，升级未开始。"); }
        catch (Exception exception) { Warn(exception.Message); }
    }

    private async Task<bool> AuthorizeManagerAsync(string action)
    {
        using var dialog = new ManagerAuthorizationDialog(action);
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        await using var db = _paths.CorePaths.CreateDbContext();
        if (await new AccountService(db).VerifyManagerAsync(dialog.Password)) return true;
        Warn("经理密码不正确。");
        return false;
    }

    private void RefreshAddresses()
    {
        var selected = _settings.SelectedLanAddress;
        var addresses = NetworkAddressService.GetLanAddresses();
        _lanAddresses.BeginUpdate(); _lanAddresses.Items.Clear();
        foreach (var address in addresses) _lanAddresses.Items.Add(address);
        _lanAddresses.EndUpdate();
        if (_lanAddresses.Items.Count > 0)
        {
            _lanAddresses.SelectedItem = _lanAddresses.Items.Cast<LanAddress>().FirstOrDefault(x => x.Address == selected) ?? _lanAddresses.Items[0];
        }
        RefreshUrls();
    }

    private void SaveSelectedAddress()
    {
        if (_lanAddresses.SelectedItem is not LanAddress address) return;
        _settings.SelectedLanAddress = address.Address;
        try { _settingsStore.Save(_settings); } catch (IOException) { }
        RefreshUrls();
    }

    private void RefreshUrls()
    {
        _localAddress.Text = LocalUrl;
        var available = _lanAddresses.SelectedItem is LanAddress;
        _copyButton.Enabled = available; _qrButton.Enabled = available;
    }

    private string LocalUrl => $"http://127.0.0.1:{_settings.Port}";
    private string? LanUrl => _lanAddresses.SelectedItem is LanAddress address ? $"http://{address.Address}:{_settings.Port}" : null;

    private void CopyLanUrl()
    {
        if (LanUrl is not { } url) { Warn("当前没有可用的局域网 IPv4 地址。"); return; }
        Clipboard.SetText(url);
        _notifyIcon.ShowBalloonTip(2500, "局域网链接已复制", url, ToolTipIcon.Info);
    }

    private void ShowQr()
    {
        if (LanUrl is not { } url) { Warn("当前没有可用的局域网 IPv4 地址。"); return; }
        Clipboard.SetText(url);
        using var qr = new QrShareForm(url); qr.ShowDialog(this);
    }

    private void UpdateServiceState()
    {
        var (text, color) = _web.State switch
        {
            WebServiceState.Stopped => ("已停止", UiStyle.Muted),
            WebServiceState.Starting => ("启动中", UiStyle.Warning),
            WebServiceState.Running => ("运行中", UiStyle.Accent),
            WebServiceState.Stopping => ("停止中", UiStyle.Warning),
            WebServiceState.PortConflict => ("端口冲突", UiStyle.Danger),
            _ => ("运行异常", UiStyle.Danger)
        };
        _stateLabel.Text = text; _stateLabel.ForeColor = color;
        _errorLabel.Text = _web.LastError ?? string.Empty;
        _startButton.Enabled = _web.State is WebServiceState.Stopped or WebServiceState.PortConflict or WebServiceState.Faulted;
        _stopButton.Enabled = _web.State == WebServiceState.Running;
        _openButton.Enabled = _web.State == WebServiceState.Running;
        _port.Enabled = _web.State is not WebServiceState.Starting and not WebServiceState.Stopping;
    }

    private NotifyIcon BuildNotifyIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示启动器", null, (_, _) => ShowFromTray());
        menu.Items.Add("打开计件工资系统", null, async (_, _) => await OpenLocalAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("启动服务", null, async (_, _) => await StartServiceAsync());
        menu.Items.Add("停止服务", null, async (_, _) => { ShowFromTray(); await StopServiceWithAuthorizationAsync(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, async (_, _) => await ExitWithAuthorizationAsync());
        var icon = new NotifyIcon { Text = "计件工资管理", Icon = Icon, Visible = true, ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private void HideToTray()
    {
        Hide(); ShowInTaskbar = false;
        if (_trayNoticeShown) return;
        _trayNoticeShown = true;
        _notifyIcon.ShowBalloonTip(2500, "计件工资管理仍在运行", "双击托盘图标可重新打开启动器。", ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate();
    }

    private async Task ExitWithAuthorizationAsync()
    {
        ShowFromTray();
        if (!await AuthorizeManagerAsync("退出启动器并停止 Web 服务")) return;
        await _web.StopAsync(); _allowExit = true; _notifyIcon.Visible = false; Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (_allowExit) return;
        if (args.CloseReason == CloseReason.UserClosing)
        {
            args.Cancel = true; HideToTray(); return;
        }
        if (args.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _web.StopAsync().GetAwaiter().GetResult();
            _allowExit = true;
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs) => RunOnUi(RefreshAddresses);
    private void RunOnUi(Action action) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(action); else action(); }
    private static void Clear(IEnumerable<TextBox> boxes) { foreach (var box in boxes) box.Clear(); }
    private void Warn(string message) => MessageBox.Show(this, message, "计件工资管理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private static TabPage NewTab(string text) => new(text) { BackColor = UiStyle.Background, Padding = new Padding(8) };
    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control) { layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(UiStyle.Label(label), 0, row); control.Margin = new Padding(0, 5, 0, 8); layout.Controls.Add(control, 1, row); }

    public async ValueTask DisposeManagerAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _notifyIcon.Visible = false; _notifyIcon.Dispose();
        await _web.DisposeAsync();
    }
}

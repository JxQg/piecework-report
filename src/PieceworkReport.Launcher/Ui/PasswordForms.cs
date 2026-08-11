using PieceworkReport.Core.Services;

namespace PieceworkReport.Launcher.Ui;

internal enum PasswordSetupMode
{
    Fresh,
    UpgradeLegacy
}

internal sealed class PasswordSetupForm : Form
{
    private readonly TextBox _currentManager = UiStyle.PasswordBox();
    private readonly TextBox _manager = UiStyle.PasswordBox();
    private readonly TextBox _managerConfirm = UiStyle.PasswordBox();
    private readonly TextBox _clerk = UiStyle.PasswordBox();
    private readonly TextBox _clerkConfirm = UiStyle.PasswordBox();

    public PasswordSetupForm(PasswordSetupMode mode)
    {
        Text = mode == PasswordSetupMode.Fresh ? "首次账户设置" : "替换旧版默认密码";
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = UiStyle.Background;
        ForeColor = UiStyle.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, mode == PasswordSetupMode.Fresh ? 440 : 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(38, 28, 38, 24), ColumnCount = 1, AutoScroll = true };
        panel.Controls.Add(new Label { Text = Text, AutoSize = true, Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), ForeColor = UiStyle.Ink, Margin = new Padding(0, 0, 0, 8) });
        panel.Controls.Add(new Label { Text = "密码至少 8 位，并同时包含字母和数字。正式服务会在设置完成后启动。", AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = UiStyle.Muted, Margin = new Padding(0, 0, 0, 18) });
        if (mode == PasswordSetupMode.UpgradeLegacy)
        {
            panel.Controls.Add(UiStyle.Label("当前经理密码")); panel.Controls.Add(_currentManager);
        }
        panel.Controls.Add(UiStyle.Label("新经理密码")); panel.Controls.Add(_manager);
        panel.Controls.Add(UiStyle.Label("确认新经理密码")); panel.Controls.Add(_managerConfirm);
        panel.Controls.Add(UiStyle.Label("新文员密码")); panel.Controls.Add(_clerk);
        panel.Controls.Add(UiStyle.Label("确认新文员密码")); panel.Controls.Add(_clerkConfirm);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 22, 0, 0) };
        var save = UiStyle.PrimaryButton("保存并继续"); save.DialogResult = DialogResult.OK; save.Click += ValidateBeforeClose;
        var cancel = UiStyle.SecondaryButton("取消"); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); panel.Controls.Add(buttons);
        Controls.Add(panel);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public string CurrentManagerPassword => _currentManager.Text;
    public string ManagerPassword => _manager.Text;
    public string ClerkPassword => _clerk.Text;

    private void ValidateBeforeClose(object? sender, EventArgs eventArgs)
    {
        var error = PasswordPolicy.Validate(_manager.Text) ?? PasswordPolicy.Validate(_clerk.Text);
        if (error is null && _manager.Text != _managerConfirm.Text) error = "两次输入的经理密码不一致。";
        if (error is null && _clerk.Text != _clerkConfirm.Text) error = "两次输入的文员密码不一致。";
        if (error is null) return;
        DialogResult = DialogResult.None;
        MessageBox.Show(this, error, "密码不符合要求", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

internal sealed class ManagerAuthorizationDialog : Form
{
    private readonly TextBox _password = UiStyle.PasswordBox();

    public ManagerAuthorizationDialog(string action)
    {
        Text = "经理验证";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Background;
        ClientSize = new Size(440, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(34, 28, 34, 24), ColumnCount = 1 };
        panel.Controls.Add(new Label { Text = action, AutoSize = true, Font = new Font(Font.FontFamily, 13F, FontStyle.Bold), ForeColor = UiStyle.Ink, Margin = new Padding(0, 0, 0, 12) });
        panel.Controls.Add(UiStyle.Label("请输入经理密码")); panel.Controls.Add(_password);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 20, 0, 0) };
        var ok = UiStyle.PrimaryButton("验证"); ok.DialogResult = DialogResult.OK;
        var cancel = UiStyle.SecondaryButton("取消"); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); panel.Controls.Add(buttons); Controls.Add(panel);
        AcceptButton = ok; CancelButton = cancel;
    }

    public string Password => _password.Text;
}

internal sealed class PasswordRecoveryForm : Form
{
    private readonly TextBox _password = UiStyle.PasswordBox();
    private readonly TextBox _confirm = UiStyle.PasswordBox();

    public PasswordRecoveryForm()
    {
        Text = "恢复经理密码";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiStyle.Background;
        ClientSize = new Size(480, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 28, 36, 24), ColumnCount = 1 };
        panel.Controls.Add(new Label { Text = "Windows 管理员恢复", AutoSize = true, Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), ForeColor = UiStyle.Ink });
        panel.Controls.Add(new Label { Text = "此操作会重置 manager 密码并使已有登录会话失效，记录会写入安全审计。", AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = UiStyle.Muted, Margin = new Padding(0, 8, 0, 12) });
        panel.Controls.Add(UiStyle.Label("新经理密码")); panel.Controls.Add(_password);
        panel.Controls.Add(UiStyle.Label("确认新经理密码")); panel.Controls.Add(_confirm);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 18, 0, 0) };
        var ok = UiStyle.PrimaryButton("重置密码"); ok.DialogResult = DialogResult.OK; ok.Click += (_, _) =>
        {
            var error = PasswordPolicy.Validate(_password.Text);
            if (error is null && _password.Text != _confirm.Text) error = "两次输入的密码不一致。";
            if (error is null) return;
            DialogResult = DialogResult.None;
            MessageBox.Show(this, error, "密码不符合要求", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        var cancel = UiStyle.SecondaryButton("取消"); cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); panel.Controls.Add(buttons); Controls.Add(panel);
        AcceptButton = ok; CancelButton = cancel;
    }

    public string NewPassword => _password.Text;
}

using System.Drawing;

namespace PieceworkReport.Launcher.Ui;

internal static class UiStyle
{
    public static readonly Padding PagePadding = new(28, 24, 28, 28);
    public static readonly Padding FieldMargin = new(0, 4, 0, 12);
    public static readonly Padding SectionMargin = new(0, 16, 0, 4);
    public static readonly Color Background = Color.FromArgb(244, 246, 243);
    public static readonly Color Surface = Color.White;
    public static readonly Color Ink = Color.FromArgb(30, 39, 34);
    public static readonly Color Muted = Color.FromArgb(97, 108, 101);
    public static readonly Color Accent = Color.FromArgb(31, 111, 78);
    public static readonly Color AccentSoft = Color.FromArgb(218, 235, 224);
    public static readonly Color Warning = Color.FromArgb(181, 106, 22);
    public static readonly Color Danger = Color.FromArgb(170, 52, 52);

    public static Button PrimaryButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(104, 38),
        BackColor = Accent,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(12, 0, 12, 0),
        UseVisualStyleBackColor = false
    };

    public static Button SecondaryButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(104, 38),
        BackColor = Surface,
        ForeColor = Ink,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(12, 0, 12, 0),
        UseVisualStyleBackColor = false
    };

    public static Label Label(string text, bool muted = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = muted ? Muted : Ink,
        Margin = new Padding(0, 0, 16, 0)
    };

    public static TextBox PasswordBox() => new() { UseSystemPasswordChar = true, Width = 300 };
}

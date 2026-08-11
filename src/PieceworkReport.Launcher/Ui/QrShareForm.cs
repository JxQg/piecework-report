using QRCoder;

namespace PieceworkReport.Launcher.Ui;

internal sealed class QrShareForm : Form
{
    public QrShareForm(string url)
    {
        Text = "局域网访问二维码";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;
        ClientSize = new Size(390, 450);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(10);
        using var stream = new MemoryStream(png);
        var image = new Bitmap(Image.FromStream(stream));
        var picture = new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(24) };
        var address = new TextBox { Text = url, ReadOnly = true, Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.None, BackColor = Color.White };
        var close = UiStyle.PrimaryButton("关闭"); close.DialogResult = DialogResult.OK;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true }; buttons.Controls.Add(close);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(picture, 0, 0); layout.Controls.Add(address, 0, 1); layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
        AcceptButton = close;
        FormClosed += (_, _) => image.Dispose();
    }
}

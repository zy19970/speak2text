namespace Speak2Text;

public sealed class LongAudioWarningDialog : Form
{
    private const int DefaultCountdownSeconds = 10;

    private readonly Button _cpuButton = new()
    {
        AutoSize = true,
        Height = 38,
        DialogResult = DialogResult.Yes
    };

    private readonly Button _continueButton = new()
    {
        Text = "继续当前 GPU / Auto",
        AutoSize = true,
        Height = 38,
        DialogResult = DialogResult.No
    };

    private readonly Button _cancelButton = new()
    {
        Text = "停止队列",
        AutoSize = true,
        Height = 38,
        DialogResult = DialogResult.Cancel
    };

    private readonly Label _countdownLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText
    };

    private readonly System.Windows.Forms.Timer _timer = new()
    {
        Interval = 1000
    };

    private int _remainingSeconds = DefaultCountdownSeconds;

    public LongAudioWarningDialog(string fileName, string durationText, string backendText)
    {
        Text = "长录音预警";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var title = new Label
        {
            Text = "检测到长录音",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Text =
                $"文件：{fileName}\r\n" +
                $"时长：{durationText}\r\n" +
                $"当前后端：{backendText}\r\n\r\n" +
                "长录音在 Vulkan 上可能因为 KV cache 或大缓冲区分配导致显存/设备内存不足。\r\n" +
                "程序会对 GPU 长录音自动分段；如果分段内仍发生明确的 Vulkan 显存错误，也会自动回退 CPU。"
        };

        _countdownLabel.Margin = new Padding(0, 14, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(_cpuButton);
        buttons.Controls.Add(_continueButton);
        buttons.Controls.Add(_cancelButton);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Dock = DockStyle.Fill
        };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(info, 0, 1);
        root.Controls.Add(_countdownLabel, 0, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);

        AcceptButton = _cpuButton;
        CancelButton = _cancelButton;

        _timer.Tick += (_, _) => TickCountdown();
        Shown += (_, _) =>
        {
            UpdateCountdownText();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Stop();

        UpdateCountdownText();
    }

    private void TickCountdown()
    {
        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            DialogResult = DialogResult.Yes;
            Close();
            return;
        }

        UpdateCountdownText();
    }

    private void UpdateCountdownText()
    {
        _cpuButton.Text = $"使用 CPU（{_remainingSeconds}）";
        _countdownLabel.Text =
            $"若 {_remainingSeconds} 秒内不选择，将自动使用 CPU 处理当前文件。";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();

        base.Dispose(disposing);
    }
}

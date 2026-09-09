using System.Diagnostics;
using Speak2Text.Models;
using Speak2Text.Services;
using Speak2Text.Utilities;

namespace Speak2Text;

public sealed class MainForm : Form
{
    private readonly TextBox _audioPath = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _modelPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputPath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _backend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };

    private readonly CheckBox _markdown = new() { Text = "Markdown", Checked = true, AutoSize = true };
    private readonly CheckBox _text = new() { Text = "TXT", Checked = true, AutoSize = true };
    private readonly CheckBox _srt = new() { Text = "SRT", Checked = true, AutoSize = true };
    private readonly CheckBox _json = new() { Text = "JSON", Checked = true, AutoSize = true };

    private readonly Label _phaseLabel = new() { Text = "阶段：等待开始", AutoSize = true };
    private readonly Label _progressPercentLabel = new() { Text = "--", AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _mediaPositionLabel = new() { Text = "处理位置：--", AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _elapsedLabel = new() { Text = "已耗时：00:00", AutoSize = true };
    private readonly Label _remainingLabel = new() { Text = "预计剩余：--", AutoSize = true };
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Fill,
        Minimum = 0,
        Maximum = 1000,
        Value = 0,
        Height = 20,
        Style = ProgressBarStyle.Blocks
    };

    private readonly Label _cpuUsageLabel = new() { Text = "CPU 0%", AutoSize = true };
    private readonly Label _gpuUsageLabel = new() { Text = "GPU --", AutoSize = true };
    private readonly ProgressBar _cpuUsageBar = new() { Minimum = 0, Maximum = 100, Value = 0, Width = 240, Height = 18 };
    private readonly ProgressBar _gpuUsageBar = new() { Minimum = 0, Maximum = 100, Value = 0, Width = 240, Height = 18 };

    private readonly CheckBox _limitCpu = new() { Text = "限制 CPU", Checked = true, AutoSize = true };
    private readonly NumericUpDown _cpuLimit = new()
    {
        Minimum = 10,
        Maximum = 100,
        Increment = 5,
        Value = 70,
        Width = 64,
        TextAlign = HorizontalAlignment.Right
    };
    private readonly Label _cpuLimitHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly CheckBox _limitGpu = new() { Text = "限制 GPU", Checked = true, AutoSize = true };
    private readonly NumericUpDown _gpuLimit = new()
    {
        Minimum = 10,
        Maximum = 100,
        Increment = 5,
        Value = 70,
        Width = 64,
        TextAlign = HorizontalAlignment.Right
    };
    private readonly Label _gpuLimitHint = new()
    {
        Text = "近似节流目标",
        AutoSize = true,
        ForeColor = SystemColors.GrayText
    };

    private readonly Button _startButton = new() { Text = "开始转写", AutoSize = true, Height = 36 };
    private readonly Button _cancelButton = new() { Text = "取消", AutoSize = true, Height = 36, Enabled = false };
    private readonly Button _openOutputButton = new() { Text = "打开输出目录", AutoSize = true, Height = 36, Enabled = false };
    private readonly Label _status = new() { Text = "就绪", AutoSize = true };

    private readonly Panel _dropPanel = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        AllowDrop = true,
        MinimumSize = new Size(0, 88)
    };
    private readonly Label _dropLabel = new()
    {
        Text = "将录音拖到这里\r\n支持 m4a / mp3 / wav / aac / flac 等 FFmpeg 可读取格式",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
        Font = new Font("Microsoft YaHei UI", 10F),
        AllowDrop = true
    };

    private readonly TranscriptionPipeline _pipeline = new();
    private readonly SystemResourceMonitor _resourceMonitor = new();
    private readonly System.Windows.Forms.Timer _usageTimer = new() { Interval = 1000 };

    private readonly Stopwatch _taskStopwatch = new();
    private readonly Stopwatch _phaseStopwatch = new();

    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<string> _lastExportedFiles = [];
    private string _currentPhaseKey = string.Empty;
    private double? _currentPhasePercent;
    private bool _currentPhaseEstimate;

    public MainForm()
    {
        Text = "Speak2Text - 离线录音转文字";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 700);
        Size = new Size(1020, 780);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;

        _backend.Items.AddRange(["自动（auto）", "CPU", "Vulkan"]);
        _backend.SelectedIndex = 0;
        _language.Items.AddRange(["自动识别", "中文（zh）", "英文（en）"]);
        _language.SelectedIndex = 0;

        _modelPath.Text = AppPaths.DefaultModelPath;
        _outputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Speak2Text");

        BuildLayout();
        WireEvents();
        UpdateCpuLimitHint();
        UpdateRuntimeStatus();
        _usageTimer.Start();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 9,
            AutoScroll = true
        };

        for (var i = 0; i < 9; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "离线录音转写",
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(title, 0, 0);

        _dropPanel.Controls.Add(_dropLabel);
        root.Controls.Add(_dropPanel, 0, 1);

        root.Controls.Add(BuildPathRow("录音文件", _audioPath, "选择…", BrowseAudio), 0, 2);
        root.Controls.Add(BuildPathRow("模型文件", _modelPath, "选择…", BrowseModel), 0, 3);
        root.Controls.Add(BuildPathRow("输出目录", _outputPath, "选择…", BrowseOutputDirectory), 0, 4);
        root.Controls.Add(BuildOptionsRow(), 0, 5);
        root.Controls.Add(BuildProgressGroup(), 0, 6);
        root.Controls.Add(BuildResourceGroup(), 0, 7);
        root.Controls.Add(BuildActionRow(), 0, 8);

        Controls.Add(root);
    }

    private Control BuildPathRow(string labelText, TextBox textBox, string buttonText, EventHandler onClick)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 8, 0, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label { Text = labelText, Anchor = AnchorStyles.Left, AutoSize = true };
        var button = new Button { Text = buttonText, AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        button.Click += onClick;

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(textBox, 1, 0);
        row.Controls.Add(button, 2, 0);
        return row;
    }

    private Control BuildOptionsRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.Controls.Add(new Label { Text = "后端", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        panel.Controls.Add(_backend);
        panel.Controls.Add(new Label { Text = "语言", AutoSize = true, Margin = new Padding(18, 7, 4, 0) });
        panel.Controls.Add(_language);
        panel.Controls.Add(new Label { Text = "输出", AutoSize = true, Margin = new Padding(18, 7, 4, 0) });
        panel.Controls.Add(_markdown);
        panel.Controls.Add(_text);
        panel.Controls.Add(_srt);
        panel.Controls.Add(_json);
        return panel;
    }

    private Control BuildProgressGroup()
    {
        var group = new GroupBox
        {
            Text = "处理进度",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            Margin = new Padding(0, 14, 0, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _progressPercentLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        _progressPercentLabel.Margin = new Padding(10, 0, 0, 0);

        layout.Controls.Add(_phaseLabel, 0, 0);
        layout.Controls.Add(_progressPercentLabel, 1, 0);
        layout.Controls.Add(_progress, 0, 1);
        layout.SetColumnSpan(_progress, 2);

        layout.Controls.Add(_mediaPositionLabel, 0, 2);
        layout.SetColumnSpan(_mediaPositionLabel, 2);

        var times = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        _elapsedLabel.Margin = new Padding(0, 0, 24, 0);
        times.Controls.Add(_elapsedLabel);
        times.Controls.Add(_remainingLabel);
        layout.Controls.Add(times, 0, 3);
        layout.SetColumnSpan(times, 2);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildResourceGroup()
    {
        var group = new GroupBox
        {
            Text = "资源占用",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            Margin = new Padding(0, 14, 0, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        layout.Controls.Add(BuildUsagePanel(_cpuUsageLabel, _cpuUsageBar), 0, 0);
        layout.Controls.Add(BuildUsagePanel(_gpuUsageLabel, _gpuUsageBar), 1, 0);
        layout.Controls.Add(BuildLimitPanel(_limitCpu, _cpuLimit, "%", _cpuLimitHint), 0, 1);
        layout.Controls.Add(BuildLimitPanel(_limitGpu, _gpuLimit, "%", _gpuLimitHint), 1, 1);

        var note = new Label
        {
            Text = "CPU 上限通过线程数和低优先级控制；GPU 上限采用进程占空比节流，属于近似控制。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 0)
        };

        var wrapper = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        wrapper.Controls.Add(layout, 0, 0);
        wrapper.Controls.Add(note, 0, 1);
        group.Controls.Add(wrapper);
        return group;
    }

    private static Control BuildUsagePanel(Label label, ProgressBar bar)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 3, 16, 3)
        };
        label.Width = 72;
        label.Margin = new Padding(0, 2, 8, 0);
        panel.Controls.Add(label);
        panel.Controls.Add(bar);
        return panel;
    }

    private static Control BuildLimitPanel(CheckBox checkBox, NumericUpDown numeric, string suffix, Label hint)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 16, 3)
        };

        panel.Controls.Add(checkBox);
        panel.Controls.Add(numeric);
        panel.Controls.Add(new Label { Text = suffix, AutoSize = true, Margin = new Padding(2, 6, 6, 0) });
        hint.Margin = new Padding(4, 6, 0, 0);
        panel.Controls.Add(hint);
        return panel;
    }

    private Control BuildActionRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 14, 0, 10)
        };

        panel.Controls.Add(_startButton);
        panel.Controls.Add(_cancelButton);
        panel.Controls.Add(_openOutputButton);
        _status.Margin = new Padding(16, 10, 0, 0);
        panel.Controls.Add(_status);
        return panel;
    }

    private void WireEvents()
    {
        _startButton.Click += StartTranscriptionAsync;
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();

        _usageTimer.Tick += (_, _) =>
        {
            UpdateResourceUsage();
            UpdateElapsedAndRemaining();
        };

        _cpuLimit.ValueChanged += (_, _) => UpdateCpuLimitHint();
        _limitCpu.CheckedChanged += (_, _) => _cpuLimit.Enabled = _limitCpu.Checked;
        _limitGpu.CheckedChanged += (_, _) => _gpuLimit.Enabled = _limitGpu.Checked;

        FormClosed += (_, _) =>
        {
            _usageTimer.Stop();
            _resourceMonitor.Dispose();
            _cancellation?.Cancel();
        };

        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
        _dropPanel.DragEnter += HandleDragEnter;
        _dropPanel.DragDrop += HandleDragDrop;
        _dropLabel.DragEnter += HandleDragEnter;
        _dropLabel.DragDrop += HandleDragDrop;
    }

    private void HandlePipelineProgress(PipelineMessage message)
    {
        var phaseKey = $"{message.Stage}:{message.Phase}";
        if (!string.Equals(phaseKey, _currentPhaseKey, StringComparison.Ordinal))
        {
            _currentPhaseKey = phaseKey;
            _phaseStopwatch.Restart();
        }

        _status.Text = message.Message;
        _phaseLabel.Text = $"阶段：{GetPhaseDisplayName(message.Phase)}";
        _currentPhasePercent = message.Percent;
        _currentPhaseEstimate = message.IsEstimate;

        if (message.Percent is double percent)
        {
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = Math.Clamp((int)Math.Round(percent * 10d), 0, 1000);
            _progressPercentLabel.Text = $"{(message.IsEstimate ? "约 " : string.Empty)}{percent:0.0}%";
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progressPercentLabel.Text = "--";
        }

        if (message.PositionMilliseconds is long position &&
            message.DurationMilliseconds is long duration &&
            duration > 0)
        {
            _mediaPositionLabel.Text =
                $"处理位置：{FormatDuration(TimeSpan.FromMilliseconds(position))} / {FormatDuration(TimeSpan.FromMilliseconds(duration))}";
        }
        else
        {
            _mediaPositionLabel.Text = message.Phase == "MOSS_LOAD"
                ? "处理位置：正在加载模型，首次进度事件将在编码开始后出现"
                : "处理位置：--";
        }

        if (message.Stage == PipelineStage.Completed)
        {
            _taskStopwatch.Stop();
            _phaseStopwatch.Stop();
            _remainingLabel.Text = "预计剩余：00:00";
        }

        UpdateElapsedAndRemaining();
    }

    private static string GetPhaseDisplayName(string phase)
        => phase switch
        {
            "FFMPEG" => "音频转换",
            "MOSS_LOAD" => "MOSS 模型加载",
            "MOSS_START" => "MOSS 初始化",
            "MOSS_ENCODE" => "MOSS 音频编码",
            "MOSS_ADAPTOR" => "MOSS 特征适配",
            "MOSS_PREFILL" => "MOSS 解码预填充",
            "MOSS_DECODE" => "MOSS 转写生成",
            "MOSS_DONE" => "MOSS 识别完成",
            "EXPORT" => "结果文件生成",
            "DONE" => "全部完成",
            _ => phase
        };

    private void UpdateElapsedAndRemaining()
    {
        if (_taskStopwatch.IsRunning || _taskStopwatch.Elapsed > TimeSpan.Zero)
            _elapsedLabel.Text = $"已耗时：{FormatDuration(_taskStopwatch.Elapsed)}";

        if (!_phaseStopwatch.IsRunning ||
            _currentPhasePercent is not double percent ||
            percent <= 0.5 ||
            percent >= 99.9 ||
            _phaseStopwatch.Elapsed.TotalSeconds < 2)
        {
            if (_currentPhasePercent is not >= 99.9)
                _remainingLabel.Text = "预计剩余：--";
            return;
        }

        var remainingSeconds =
            _phaseStopwatch.Elapsed.TotalSeconds * (100d - percent) / percent;

        if (!double.IsFinite(remainingSeconds) || remainingSeconds < 0)
        {
            _remainingLabel.Text = "预计剩余：--";
            return;
        }

        var remaining = TimeSpan.FromSeconds(Math.Min(remainingSeconds, TimeSpan.FromDays(7).TotalSeconds));
        _remainingLabel.Text =
            $"预计剩余（当前阶段）：{(_currentPhaseEstimate ? "约 " : string.Empty)}{FormatDuration(remaining)}";
    }

    private static string FormatDuration(TimeSpan value)
    {
        value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private void UpdateResourceUsage()
    {
        try
        {
            var sample = _resourceMonitor.Sample();
            var cpu = Math.Clamp((int)Math.Round(sample.CpuPercent), 0, 100);
            _cpuUsageLabel.Text = $"CPU {cpu}%";
            _cpuUsageBar.Value = cpu;

            if (sample.GpuPercent is double gpuValue)
            {
                var gpu = Math.Clamp((int)Math.Round(gpuValue), 0, 100);
                _gpuUsageLabel.Text = $"GPU {gpu}%";
                _gpuUsageBar.Value = gpu;
            }
            else
            {
                _gpuUsageLabel.Text = "GPU --";
                _gpuUsageBar.Value = 0;
            }
        }
        catch
        {
            _gpuUsageLabel.Text = "GPU --";
        }
    }

    private void UpdateCpuLimitHint()
    {
        var logical = Math.Max(1, Environment.ProcessorCount);
        var threads = Math.Max(1, (int)Math.Floor(logical * (double)_cpuLimit.Value / 100d));
        _cpuLimitHint.Text = $"≈ {threads}/{logical} 线程";
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        SetAudioFile(files[0]);
    }

    private void BrowseAudio(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择录音文件",
            Filter = "音频文件|*.m4a;*.mp3;*.wav;*.aac;*.flac;*.ogg;*.wma;*.mp4;*.mov|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetAudioFile(dialog.FileName);
    }

    private void BrowseModel(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 MOSS GGUF 模型",
            Filter = "GGUF 模型|*.gguf|所有文件|*.*",
            FileName = Path.GetFileName(_modelPath.Text),
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(_modelPath.Text))
                ? Path.GetDirectoryName(_modelPath.Text)
                : AppPaths.ModelsDirectory
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _modelPath.Text = dialog.FileName;
    }

    private void BrowseOutputDirectory(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择转写结果输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_outputPath.Text) ? _outputPath.Text : string.Empty,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputPath.Text = dialog.SelectedPath;
    }

    private void SetAudioFile(string path)
    {
        if (!File.Exists(path))
            return;

        _audioPath.Text = path;
        _dropLabel.Text = Path.GetFileName(path) + "\r\n已选择，点击“开始转写”即可处理";

        var sourceDirectory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
            _outputPath.Text = Path.Combine(sourceDirectory, "transcripts");
    }

    private async void StartTranscriptionAsync(object? sender, EventArgs e)
    {
        if (_cancellation is not null)
            return;

        try
        {
            var options = BuildOptions();
            ValidateRuntimeFiles(options);

            SetBusy(true);
            ResetProgressUi();

            _lastExportedFiles = [];
            _cancellation = new CancellationTokenSource();
            _taskStopwatch.Restart();

            var progress = new Progress<PipelineMessage>(HandlePipelineProgress);
            var result = await _pipeline.RunAsync(options, progress, _cancellation.Token);

            _lastExportedFiles = result.ExportedFiles;
            _status.Text = $"完成：{result.Transcript.Segments.Count} 个片段，{result.Transcript.SpeakerIds.Count()} 位说话人";
            _openOutputButton.Enabled = true;
        }
        catch (OperationCanceledException)
        {
            _taskStopwatch.Stop();
            _phaseStopwatch.Stop();
            _status.Text = "已取消";
            _phaseLabel.Text = "阶段：已取消";
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 0;
            _progressPercentLabel.Text = "--";
            _remainingLabel.Text = "预计剩余：--";
        }
        catch (Exception ex)
        {
            _taskStopwatch.Stop();
            _phaseStopwatch.Stop();
            _status.Text = "处理失败";
            _phaseLabel.Text = "阶段：处理失败";
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 0;
            _progressPercentLabel.Text = "--";
            _remainingLabel.Text = "预计剩余：--";
            MessageBox.Show(this, ex.Message, "Speak2Text", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
            UpdateElapsedAndRemaining();
        }
    }

    private void ResetProgressUi()
    {
        _currentPhaseKey = string.Empty;
        _currentPhasePercent = null;
        _currentPhaseEstimate = false;
        _taskStopwatch.Reset();
        _phaseStopwatch.Reset();

        _phaseLabel.Text = "阶段：准备开始";
        _progress.Style = ProgressBarStyle.Blocks;
        _progress.Value = 0;
        _progressPercentLabel.Text = "0.0%";
        _mediaPositionLabel.Text = "处理位置：--";
        _elapsedLabel.Text = "已耗时：00:00";
        _remainingLabel.Text = "预计剩余：--";
    }

    private TranscriptionOptions BuildOptions()
    {
        var backend = _backend.SelectedIndex switch
        {
            1 => "cpu",
            2 => "vulkan",
            _ => "auto"
        };

        var language = _language.SelectedIndex switch
        {
            1 => "zh",
            2 => "en",
            _ => "auto"
        };

        return new TranscriptionOptions
        {
            AudioPath = _audioPath.Text.Trim(),
            ModelPath = _modelPath.Text.Trim(),
            OutputDirectory = _outputPath.Text.Trim(),
            Backend = backend,
            Language = language,
            ExportMarkdown = _markdown.Checked,
            ExportText = _text.Checked,
            ExportSrt = _srt.Checked,
            ExportJson = _json.Checked,
            LimitCpu = _limitCpu.Checked,
            MaxCpuPercent = (int)_cpuLimit.Value,
            LimitGpu = _limitGpu.Checked,
            MaxGpuPercent = (int)_gpuLimit.Value
        };
    }

    private void ValidateRuntimeFiles(TranscriptionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AudioPath))
            throw new InvalidOperationException("请先选择一个录音文件。");
        if (!File.Exists(AppPaths.FfmpegPath))
            throw new FileNotFoundException("缺少 engine\\ffmpeg.exe。", AppPaths.FfmpegPath);
        if (!File.Exists(AppPaths.FfprobePath))
            throw new FileNotFoundException("缺少 engine\\ffprobe.exe。请重新下载最新 Action 便携包。", AppPaths.FfprobePath);
        if (!File.Exists(AppPaths.TranscribeCliPath))
            throw new FileNotFoundException("缺少 engine\\transcribe-cli.exe。", AppPaths.TranscribeCliPath);
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("缺少 MOSS GGUF 模型。默认应放在 models\\MOSS-Transcribe-Diarize-Q8_0.gguf。", options.ModelPath);
        if (!_markdown.Checked && !_text.Checked && !_srt.Checked && !_json.Checked)
            throw new InvalidOperationException("请至少选择一种输出格式。");
    }

    private void SetBusy(bool busy)
    {
        _startButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        _audioPath.Enabled = !busy;
        _modelPath.Enabled = !busy;
        _outputPath.Enabled = !busy;
        _backend.Enabled = !busy;
        _language.Enabled = !busy;
        _markdown.Enabled = !busy;
        _text.Enabled = !busy;
        _srt.Enabled = !busy;
        _json.Enabled = !busy;
        _limitCpu.Enabled = !busy;
        _limitGpu.Enabled = !busy;
        _cpuLimit.Enabled = !busy && _limitCpu.Checked;
        _gpuLimit.Enabled = !busy && _limitGpu.Checked;
        _dropPanel.AllowDrop = !busy;
        _dropLabel.AllowDrop = !busy;
        AllowDrop = !busy;

        if (busy)
            _openOutputButton.Enabled = false;
    }

    private void OpenOutputDirectory()
    {
        var directory = _lastExportedFiles.Count > 0
            ? Path.GetDirectoryName(_lastExportedFiles[0])
            : _outputPath.Text;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { directory },
            UseShellExecute = true
        });
    }

    private void UpdateRuntimeStatus()
    {
        var missing = new List<string>();
        if (!File.Exists(AppPaths.FfmpegPath)) missing.Add("ffmpeg.exe");
        if (!File.Exists(AppPaths.FfprobePath)) missing.Add("ffprobe.exe");
        if (!File.Exists(AppPaths.TranscribeCliPath)) missing.Add("transcribe-cli.exe");
        if (!File.Exists(AppPaths.DefaultModelPath)) missing.Add("MOSS Q8 模型");

        if (missing.Count > 0)
            _status.Text = "待配置运行环境：" + string.Join("、", missing);
    }
}

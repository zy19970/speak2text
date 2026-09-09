using System.Diagnostics;
using Speak2Text.Models;
using Speak2Text.Services;
using Speak2Text.Utilities;

namespace Speak2Text;

public sealed class MainForm : Form
{
    private readonly List<TranscriptionQueueItem> _queue = [];

    private readonly DataGridView _queueGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        ReadOnly = true,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        AllowDrop = true,
        MinimumSize = new Size(0, 190)
    };

    private readonly Button _addFilesButton = new() { Text = "添加文件…", AutoSize = true };
    private readonly Button _removeFilesButton = new() { Text = "删除选中", AutoSize = true };
    private readonly Button _clearQueueButton = new() { Text = "清空队列", AutoSize = true };
    private readonly Button _moveUpButton = new() { Text = "上移", AutoSize = true };
    private readonly Button _moveDownButton = new() { Text = "下移", AutoSize = true };
    private readonly Label _queueSummaryLabel = new() { Text = "队列为空，可一次选择或拖入多个录音文件。", AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly TextBox _modelPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputPath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _backend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };

    private readonly CheckBox _markdown = new() { Text = "Markdown", Checked = true, AutoSize = true };
    private readonly CheckBox _text = new() { Text = "TXT", Checked = true, AutoSize = true };
    private readonly CheckBox _srt = new() { Text = "SRT", Checked = true, AutoSize = true };
    private readonly CheckBox _json = new() { Text = "JSON", Checked = true, AutoSize = true };

    private readonly Label _currentFileLabel = new() { Text = "当前文件：--", AutoSize = true };
    private readonly Label _phaseLabel = new() { Text = "阶段：等待开始", AutoSize = true };
    private readonly Label _progressPercentLabel = new() { Text = "--", AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _mediaPositionLabel = new() { Text = "处理位置：--", AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _elapsedLabel = new() { Text = "当前文件已耗时：00:00", AutoSize = true };
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
    private readonly ProgressBar _cpuUsageBar = new() { Minimum = 0, Maximum = 100, Value = 0, Width = 220, Height = 18 };
    private readonly ProgressBar _gpuUsageBar = new() { Minimum = 0, Maximum = 100, Value = 0, Width = 220, Height = 18 };

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

    private readonly Button _startButton = new() { Text = "开始队列", AutoSize = true, Height = 36 };
    private readonly Button _cancelButton = new() { Text = "停止队列", AutoSize = true, Height = 36, Enabled = false };
    private readonly Button _openOutputButton = new() { Text = "打开输出目录", AutoSize = true, Height = 36 };
    private readonly Button _openTempButton = new() { Text = "打开临时目录", AutoSize = true, Height = 36 };
    private readonly Label _status = new() { Text = "就绪", AutoSize = true };

    private readonly TranscriptionPipeline _pipeline = new();
    private readonly SystemResourceMonitor _resourceMonitor = new();
    private readonly System.Windows.Forms.Timer _usageTimer = new() { Interval = 1000 };

    private readonly Stopwatch _taskStopwatch = new();
    private readonly Stopwatch _phaseStopwatch = new();

    private CancellationTokenSource? _queueCancellation;
    private TranscriptionQueueItem? _currentQueueItem;
    private IReadOnlyList<string> _lastExportedFiles = [];
    private string _currentPhaseKey = string.Empty;
    private double? _currentPhasePercent;
    private bool _currentPhaseEstimate;
    private bool _queueRunning;

    public MainForm()
    {
        Text = "Speak2Text - 离线录音批量转写";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 820);
        Size = new Size(1120, 900);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;

        _backend.Items.AddRange(["自动（auto）", "CPU", "Vulkan"]);
        _backend.SelectedIndex = 0;
        _language.Items.AddRange(["自动识别", "中文（zh）", "英文（en）"]);
        _language.SelectedIndex = 0;

        _modelPath.Text = AppPaths.DefaultModelPath;
        _outputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Speak2Text");

        ConfigureQueueGrid();
        BuildLayout();
        WireEvents();
        UpdateCpuLimitHint();
        UpdateQueueSummary();
        UpdateRuntimeStatus();
        _usageTimer.Start();
    }

    private void ConfigureQueueGrid()
    {
        _queueGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Index",
            HeaderText = "#",
            Width = 42,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _queueGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            HeaderText = "文件",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 45,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _queueGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "状态",
            Width = 86,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _queueGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Phase",
            HeaderText = "当前阶段",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 27,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _queueGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Progress",
            HeaderText = "进度",
            Width = 82,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _queueGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Elapsed",
            HeaderText = "耗时",
            Width = 84,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _queueGrid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
        _queueGrid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        _queueGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 8,
            AutoScroll = true
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "离线录音批量转写",
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        root.Controls.Add(title, 0, 0);

        root.Controls.Add(BuildQueueGroup(), 0, 1);
        root.Controls.Add(BuildPathRow("模型文件", _modelPath, "选择…", BrowseModel), 0, 2);
        root.Controls.Add(BuildPathRow("输出目录", _outputPath, "选择…", BrowseOutputDirectory), 0, 3);
        root.Controls.Add(BuildOptionsRow(), 0, 4);
        root.Controls.Add(BuildProgressGroup(), 0, 5);
        root.Controls.Add(BuildResourceGroup(), 0, 6);
        root.Controls.Add(BuildActionRow(), 0, 7);

        Controls.Add(root);
    }

    private Control BuildQueueGroup()
    {
        var group = new GroupBox
        {
            Text = "转写队列",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 0, 4),
            AllowDrop = true
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 7)
        };
        toolbar.Controls.Add(_addFilesButton);
        toolbar.Controls.Add(_removeFilesButton);
        toolbar.Controls.Add(_clearQueueButton);
        toolbar.Controls.Add(_moveUpButton);
        toolbar.Controls.Add(_moveDownButton);

        var dragHint = new Label
        {
            Text = "也可以直接把多个录音文件拖到队列中",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(12, 6, 0, 0)
        };
        toolbar.Controls.Add(dragHint);

        _queueSummaryLabel.Margin = new Padding(0, 7, 0, 0);

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_queueGrid, 0, 1);
        layout.Controls.Add(_queueSummaryLabel, 0, 2);
        group.Controls.Add(layout);
        return group;
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
            Text = "当前文件进度",
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
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _progressPercentLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        _progressPercentLabel.Margin = new Padding(10, 0, 0, 0);

        layout.Controls.Add(_currentFileLabel, 0, 0);
        layout.SetColumnSpan(_currentFileLabel, 2);
        layout.Controls.Add(_phaseLabel, 0, 1);
        layout.Controls.Add(_progressPercentLabel, 1, 1);
        layout.Controls.Add(_progress, 0, 2);
        layout.SetColumnSpan(_progress, 2);
        layout.Controls.Add(_mediaPositionLabel, 0, 3);
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
        layout.Controls.Add(times, 0, 4);
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
            Text = "队列严格串行处理；CPU 上限通过线程数和低优先级控制，GPU 上限采用近似占空比节流。",
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
        panel.Controls.Add(_openTempButton);
        _status.Margin = new Padding(16, 10, 0, 0);
        panel.Controls.Add(_status);
        return panel;
    }

    private void WireEvents()
    {
        _addFilesButton.Click += BrowseAudioFiles;
        _removeFilesButton.Click += (_, _) => RemoveSelectedQueueItems();
        _clearQueueButton.Click += (_, _) => ClearQueue();
        _moveUpButton.Click += (_, _) => MoveSelectedQueueItem(-1);
        _moveDownButton.Click += (_, _) => MoveSelectedQueueItem(1);

        _startButton.Click += StartQueueAsync;
        _cancelButton.Click += (_, _) => _queueCancellation?.Cancel();
        _openOutputButton.Click += (_, _) => OpenOutputDirectory();
        _openTempButton.Click += (_, _) => OpenTemporaryDirectory();

        _usageTimer.Tick += (_, _) =>
        {
            UpdateResourceUsage();
            UpdateElapsedAndRemaining();

            if (_currentQueueItem is not null && _taskStopwatch.IsRunning)
            {
                _currentQueueItem.Elapsed = _taskStopwatch.Elapsed;
                UpdateQueueRow(_currentQueueItem);
            }
        };

        _cpuLimit.ValueChanged += (_, _) => UpdateCpuLimitHint();
        _limitCpu.CheckedChanged += (_, _) => _cpuLimit.Enabled = _limitCpu.Checked;
        _limitGpu.CheckedChanged += (_, _) => _gpuLimit.Enabled = _limitGpu.Checked;

        FormClosed += (_, _) =>
        {
            _usageTimer.Stop();
            _resourceMonitor.Dispose();
            _queueCancellation?.Cancel();
        };

        DragEnter += HandleDragEnter;
        DragDrop += HandleDragDrop;
        _queueGrid.DragEnter += HandleDragEnter;
        _queueGrid.DragDrop += HandleDragDrop;
    }

    private void BrowseAudioFiles(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择一个或多个录音文件",
            Multiselect = true,
            Filter = "音频/视频文件|*.m4a;*.mp3;*.wav;*.aac;*.flac;*.ogg;*.wma;*.mp4;*.mov|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddFiles(dialog.FileNames);
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

        AddFiles(files.Where(File.Exists));
    }

    private void AddFiles(IEnumerable<string> files)
    {
        if (_queueRunning)
            return;

        var normalizedExisting = _queue
            .Select(x => Path.GetFullPath(x.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        string? firstAdded = null;

        foreach (var file in files)
        {
            if (!File.Exists(file))
                continue;

            var fullPath = Path.GetFullPath(file);
            if (!normalizedExisting.Add(fullPath))
                continue;

            _queue.Add(new TranscriptionQueueItem { FilePath = fullPath });
            firstAdded ??= fullPath;
            added++;
        }

        if (added == 0)
            return;

        if (_queue.Count == added && firstAdded is not null)
        {
            var sourceDirectory = Path.GetDirectoryName(firstAdded);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
                _outputPath.Text = Path.Combine(sourceDirectory, "transcripts");
        }

        RebuildQueueGrid();
        UpdateQueueSummary();
        _status.Text = $"已加入 {added} 个文件，队列共 {_queue.Count} 个。";
    }

    private void RemoveSelectedQueueItems()
    {
        if (_queueRunning || _queueGrid.SelectedRows.Count == 0)
            return;

        var selectedIds = _queueGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .Where(row => row.Tag is Guid)
            .Select(row => (Guid)row.Tag!)
            .ToHashSet();

        _queue.RemoveAll(item => selectedIds.Contains(item.Id));
        RebuildQueueGrid();
        UpdateQueueSummary();
    }

    private void ClearQueue()
    {
        if (_queueRunning)
            return;

        _queue.Clear();
        _lastExportedFiles = [];
        RebuildQueueGrid();
        UpdateQueueSummary();
        ResetProgressUi();
        _status.Text = "队列已清空";
    }

    private void MoveSelectedQueueItem(int offset)
    {
        if (_queueRunning || _queueGrid.SelectedRows.Count != 1)
            return;

        var row = _queueGrid.SelectedRows[0];
        if (row.Tag is not Guid id)
            return;

        var index = _queue.FindIndex(item => item.Id == id);
        if (index < 0)
            return;

        var newIndex = index + offset;
        if (newIndex < 0 || newIndex >= _queue.Count)
            return;

        var item = _queue[index];
        _queue.RemoveAt(index);
        _queue.Insert(newIndex, item);

        RebuildQueueGrid();
        foreach (DataGridViewRow candidate in _queueGrid.Rows)
        {
            if (candidate.Tag is Guid candidateId && candidateId == item.Id)
            {
                candidate.Selected = true;
                _queueGrid.CurrentCell = candidate.Cells[1];
                break;
            }
        }
    }

    private async void StartQueueAsync(object? sender, EventArgs e)
    {
        if (_queueRunning)
            return;

        var pendingItems = _queue.Where(item => item.Status == TranscriptionQueueStatus.Pending).ToList();
        if (pendingItems.Count == 0)
        {
            MessageBox.Show(this, "队列中没有等待处理的文件。", "Speak2Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ValidateRuntimeFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Speak2Text", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _queueRunning = true;
        _queueCancellation = new CancellationTokenSource();
        SetBusy(true);

        var cancelled = false;

        try
        {
            foreach (var item in _queue)
            {
                if (item.Status != TranscriptionQueueStatus.Pending)
                    continue;

                if (_queueCancellation.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                _currentQueueItem = item;
                PrepareQueueItemForRun(item);
                ResetProgressUi();
                _currentFileLabel.Text = $"当前文件：{item.FileName}";
                _taskStopwatch.Restart();

                var options = BuildOptions(item.FilePath);
                var progress = new Progress<PipelineMessage>(message => HandlePipelineProgress(item, message));

                try
                {
                    var result = await _pipeline.RunAsync(options, progress, _queueCancellation.Token);

                    _taskStopwatch.Stop();
                    item.Elapsed = _taskStopwatch.Elapsed;
                    item.Status = TranscriptionQueueStatus.Completed;
                    item.Phase = "已完成";
                    item.Percent = 100;
                    item.IsEstimate = false;
                    item.ExportedFiles = result.ExportedFiles;
                    _lastExportedFiles = result.ExportedFiles;
                    UpdateQueueRow(item);
                }
                catch (OperationCanceledException)
                {
                    _taskStopwatch.Stop();
                    item.Elapsed = _taskStopwatch.Elapsed;
                    item.Status = TranscriptionQueueStatus.Cancelled;
                    item.Phase = "已取消";
                    item.Percent = null;
                    item.IsEstimate = false;
                    UpdateQueueRow(item);
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    _taskStopwatch.Stop();
                    item.Elapsed = _taskStopwatch.Elapsed;
                    item.Status = TranscriptionQueueStatus.Failed;
                    item.Phase = "失败";
                    item.Percent = null;
                    item.ErrorMessage = ex.Message;
                    UpdateQueueRow(item);
                }

                UpdateQueueSummary();
            }
        }
        finally
        {
            _taskStopwatch.Stop();
            _phaseStopwatch.Stop();
            _currentQueueItem = null;

            _queueCancellation.Dispose();
            _queueCancellation = null;
            _queueRunning = false;
            SetBusy(false);
            UpdateQueueSummary();

            var completed = _queue.Count(x => x.Status == TranscriptionQueueStatus.Completed);
            var failed = _queue.Count(x => x.Status == TranscriptionQueueStatus.Failed);
            var pending = _queue.Count(x => x.Status == TranscriptionQueueStatus.Pending);

            if (cancelled)
            {
                _status.Text = $"队列已停止：完成 {completed}，失败 {failed}，待处理 {pending}。再次点击“开始队列”可继续等待项。";
                _phaseLabel.Text = "阶段：队列已停止";
                _remainingLabel.Text = "预计剩余：--";
            }
            else
            {
                _status.Text = failed > 0
                    ? $"队列处理结束：完成 {completed}，失败 {failed}。"
                    : $"队列处理完成：共完成 {completed} 个文件。";
                _phaseLabel.Text = "阶段：队列完成";
                _remainingLabel.Text = "预计剩余：00:00";
            }
        }
    }

    private void PrepareQueueItemForRun(TranscriptionQueueItem item)
    {
        item.Status = TranscriptionQueueStatus.Running;
        item.Phase = "准备开始";
        item.Percent = 0;
        item.IsEstimate = false;
        item.Elapsed = TimeSpan.Zero;
        item.ErrorMessage = null;
        item.ExportedFiles = [];
        UpdateQueueRow(item);
        UpdateQueueSummary();
    }

    private void HandlePipelineProgress(TranscriptionQueueItem item, PipelineMessage message)
    {
        var phaseKey = $"{message.Stage}:{message.Phase}";
        if (!string.Equals(phaseKey, _currentPhaseKey, StringComparison.Ordinal))
        {
            _currentPhaseKey = phaseKey;
            _phaseStopwatch.Restart();
        }

        _status.Text = $"[{GetQueuePosition(item)}] {message.Message}";
        _phaseLabel.Text = $"阶段：{GetPhaseDisplayName(message.Phase)}";
        _currentPhasePercent = message.Percent;
        _currentPhaseEstimate = message.IsEstimate;

        item.Phase = GetPhaseDisplayName(message.Phase);
        item.Percent = message.Percent;
        item.IsEstimate = message.IsEstimate;
        item.Elapsed = _taskStopwatch.Elapsed;
        UpdateQueueRow(item);

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

        UpdateElapsedAndRemaining();
    }

    private string GetQueuePosition(TranscriptionQueueItem item)
    {
        var index = _queue.FindIndex(x => x.Id == item.Id);
        return index >= 0 ? $"{index + 1}/{_queue.Count}" : $"?/{_queue.Count}";
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
            "MOSS_GPU_FALLBACK" => "GPU失败，切换CPU",
            "MOSS_CPU_FALLBACK" => "CPU回退处理中",
            "EXPORT" => "结果文件生成",
            "DONE" => "全部完成",
            _ => phase
        };

    private void RebuildQueueGrid()
    {
        _queueGrid.Rows.Clear();

        for (var i = 0; i < _queue.Count; i++)
        {
            var item = _queue[i];
            var rowIndex = _queueGrid.Rows.Add(
                i + 1,
                item.FileName,
                GetStatusText(item.Status),
                item.Phase,
                GetProgressText(item),
                FormatDuration(item.Elapsed));

            var row = _queueGrid.Rows[rowIndex];
            row.Tag = item.Id;
            row.Cells["FileName"].ToolTipText = item.FilePath;

            if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
                row.Cells["Phase"].ToolTipText = item.ErrorMessage;
        }
    }

    private void UpdateQueueRow(TranscriptionQueueItem item)
    {
        foreach (DataGridViewRow row in _queueGrid.Rows)
        {
            if (row.Tag is not Guid id || id != item.Id)
                continue;

            row.Cells["Status"].Value = GetStatusText(item.Status);
            row.Cells["Phase"].Value = item.Phase;
            row.Cells["Progress"].Value = GetProgressText(item);
            row.Cells["Elapsed"].Value = FormatDuration(item.Elapsed);
            row.Cells["Phase"].ToolTipText = item.ErrorMessage ?? string.Empty;
            return;
        }
    }

    private static string GetStatusText(TranscriptionQueueStatus status)
        => status switch
        {
            TranscriptionQueueStatus.Pending => "等待中",
            TranscriptionQueueStatus.Running => "处理中",
            TranscriptionQueueStatus.Completed => "已完成",
            TranscriptionQueueStatus.Failed => "失败",
            TranscriptionQueueStatus.Cancelled => "已取消",
            _ => status.ToString()
        };

    private static string GetProgressText(TranscriptionQueueItem item)
    {
        if (item.Status == TranscriptionQueueStatus.Completed)
            return "100%";

        if (item.Percent is not double percent)
            return item.Status == TranscriptionQueueStatus.Running ? "--" : string.Empty;

        return $"{(item.IsEstimate ? "约 " : string.Empty)}{percent:0.0}%";
    }

    private void UpdateQueueSummary()
    {
        if (_queue.Count == 0)
        {
            _queueSummaryLabel.Text = "队列为空，可一次选择或拖入多个录音文件。";
            return;
        }

        var pending = _queue.Count(x => x.Status == TranscriptionQueueStatus.Pending);
        var running = _queue.Count(x => x.Status == TranscriptionQueueStatus.Running);
        var completed = _queue.Count(x => x.Status == TranscriptionQueueStatus.Completed);
        var failed = _queue.Count(x => x.Status == TranscriptionQueueStatus.Failed);
        var cancelled = _queue.Count(x => x.Status == TranscriptionQueueStatus.Cancelled);

        _queueSummaryLabel.Text =
            $"共 {_queue.Count} 个文件｜等待 {pending}｜处理中 {running}｜完成 {completed}｜失败 {failed}｜取消 {cancelled}";
    }

    private void UpdateElapsedAndRemaining()
    {
        if (_taskStopwatch.IsRunning || _taskStopwatch.Elapsed > TimeSpan.Zero)
            _elapsedLabel.Text = $"当前文件已耗时：{FormatDuration(_taskStopwatch.Elapsed)}";

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

    private void ResetProgressUi()
    {
        _currentPhaseKey = string.Empty;
        _currentPhasePercent = null;
        _currentPhaseEstimate = false;
        _taskStopwatch.Reset();
        _phaseStopwatch.Reset();

        _currentFileLabel.Text = "当前文件：--";
        _phaseLabel.Text = "阶段：准备开始";
        _progress.Style = ProgressBarStyle.Blocks;
        _progress.Value = 0;
        _progressPercentLabel.Text = "0.0%";
        _mediaPositionLabel.Text = "处理位置：--";
        _elapsedLabel.Text = "当前文件已耗时：00:00";
        _remainingLabel.Text = "预计剩余：--";
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
            Description = "选择整个队列的结果输出目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_outputPath.Text) ? _outputPath.Text : string.Empty,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputPath.Text = dialog.SelectedPath;
    }

    private TranscriptionOptions BuildOptions(string audioPath)
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
            AudioPath = audioPath,
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

    private void ValidateRuntimeFiles()
    {
        if (_queue.Count == 0)
            throw new InvalidOperationException("请先向队列中添加录音文件。");
        if (!File.Exists(AppPaths.FfmpegPath))
            throw new FileNotFoundException("缺少 engine\\ffmpeg.exe。", AppPaths.FfmpegPath);
        if (!File.Exists(AppPaths.FfprobePath))
            throw new FileNotFoundException("缺少 engine\\ffprobe.exe。请重新下载最新 Action 便携包。", AppPaths.FfprobePath);
        if (!File.Exists(AppPaths.TranscribeCliPath))
            throw new FileNotFoundException("缺少 engine\\transcribe-cli.exe。", AppPaths.TranscribeCliPath);
        if (!File.Exists(_modelPath.Text.Trim()))
            throw new FileNotFoundException("缺少 MOSS GGUF 模型。默认应放在 models\\MOSS-Transcribe-Diarize-Q8_0.gguf。", _modelPath.Text.Trim());
        if (string.IsNullOrWhiteSpace(_outputPath.Text))
            throw new InvalidOperationException("请选择输出目录。");
        if (!_markdown.Checked && !_text.Checked && !_srt.Checked && !_json.Checked)
            throw new InvalidOperationException("请至少选择一种输出格式。");
    }

    private void SetBusy(bool busy)
    {
        _addFilesButton.Enabled = !busy;
        _removeFilesButton.Enabled = !busy;
        _clearQueueButton.Enabled = !busy;
        _moveUpButton.Enabled = !busy;
        _moveDownButton.Enabled = !busy;

        _startButton.Enabled = !busy;
        _cancelButton.Enabled = busy;

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

        _queueGrid.AllowDrop = !busy;
        AllowDrop = !busy;
    }

    private void OpenOutputDirectory()
    {
        var directory = _lastExportedFiles.Count > 0
            ? Path.GetDirectoryName(_lastExportedFiles[0])
            : _outputPath.Text;

        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        OpenDirectory(directory);
    }

    private void OpenTemporaryDirectory()
    {
        Directory.CreateDirectory(AppPaths.TemporaryDirectory);
        OpenDirectory(AppPaths.TemporaryDirectory);
    }

    private static void OpenDirectory(string directory)
    {
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

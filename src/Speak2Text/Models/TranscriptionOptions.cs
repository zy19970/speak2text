namespace Speak2Text.Models;

public sealed class TranscriptionOptions
{
    public required string AudioPath { get; init; }
    public required string ModelPath { get; init; }
    public required string OutputDirectory { get; init; }
    public string Backend { get; init; } = "auto";
    public string Language { get; init; } = "auto";
    public bool ExportMarkdown { get; init; } = true;
    public bool ExportText { get; init; } = true;
    public bool ExportSrt { get; init; } = true;
    public bool ExportJson { get; init; } = true;

    public bool LimitCpu { get; init; } = true;
    public int MaxCpuPercent { get; init; } = 70;
    public bool LimitGpu { get; init; } = true;
    public int MaxGpuPercent { get; init; } = 70;

    public int CpuThreadLimit
    {
        get
        {
            if (!LimitCpu)
                return 0;

            var logicalProcessors = Math.Max(1, Environment.ProcessorCount);
            var percent = Math.Clamp(MaxCpuPercent, 10, 100);
            return Math.Max(1, (int)Math.Floor(logicalProcessors * percent / 100d));
        }
    }
}

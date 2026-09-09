namespace Speak2Text.Models;

public enum TranscriptionQueueStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class TranscriptionQueueItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string FilePath { get; init; }

    public string FileName => Path.GetFileName(FilePath);
    public TranscriptionQueueStatus Status { get; set; } = TranscriptionQueueStatus.Pending;
    public string Phase { get; set; } = "等待中";
    public double? Percent { get; set; }
    public bool IsEstimate { get; set; }
    public TimeSpan Elapsed { get; set; }
    public long? DurationMilliseconds { get; set; }

    // Per-file override selected by the long-audio warning.
    public string? BackendOverride { get; set; }
    public string? EffectiveBackend { get; set; }
    public bool LongAudioWarningAcknowledged { get; set; }

    // Note is intentionally short and visible in the queue. ErrorMessage keeps
    // the complete diagnostic text for the tooltip.
    public string? Note { get; set; }
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<string> ExportedFiles { get; set; } = [];
}

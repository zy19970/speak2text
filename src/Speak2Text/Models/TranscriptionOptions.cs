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
}

namespace Speak2Text.Models;

public sealed class TranscriptionResult
{
    public required string SourceAudioPath { get; init; }
    public required string ModelPath { get; init; }
    public required string Backend { get; init; }
    public required string Language { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }
    public string FullText { get; init; } = string.Empty;

    public IEnumerable<int> SpeakerIds => Segments
        .Where(x => x.SpeakerId > 0)
        .Select(x => x.SpeakerId)
        .Distinct()
        .OrderBy(x => x);
}

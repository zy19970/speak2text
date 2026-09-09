namespace Speak2Text.Models;

public sealed record TranscriptSegment(
    long StartMilliseconds,
    long EndMilliseconds,
    int SpeakerId,
    string Text)
{
    public string SpeakerLabel => SpeakerId > 0 ? $"Speaker {SpeakerId:00}" : "Speaker ??";
}

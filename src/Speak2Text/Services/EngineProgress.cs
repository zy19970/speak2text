namespace Speak2Text.Services;

public sealed record EngineProgress(
    string Phase,
    string Message,
    double? Percent = null,
    long? PositionMilliseconds = null,
    long? DurationMilliseconds = null,
    bool IsEstimate = false,
    string? DiagnosticDetail = null);

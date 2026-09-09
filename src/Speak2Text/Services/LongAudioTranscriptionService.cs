using Speak2Text.Models;

namespace Speak2Text.Services;

public sealed class LongAudioTranscriptionService
{
    private const long CpuChunkMilliseconds = 45L * 60 * 1000;
    private const long GpuChunkMilliseconds = 20L * 60 * 1000;
    private const long OverlapMilliseconds = 60L * 1000;

    private readonly FfmpegService _ffmpeg;
    private readonly TranscribeCliService _transcriber;

    public LongAudioTranscriptionService(FfmpegService ffmpeg, TranscribeCliService transcriber)
    {
        _ffmpeg = ffmpeg;
        _transcriber = transcriber;
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string wavPath,
        TranscriptionOptions options,
        string workDirectory,
        Action<EngineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var durationMs = await _ffmpeg.ProbeDurationMillisecondsAsync(wavPath, cancellationToken);
        var normalizedBackend = NormalizeBackend(options.Backend);
        var chunkMs = normalizedBackend == "cpu"
            ? CpuChunkMilliseconds
            : GpuChunkMilliseconds;

        if (durationMs <= chunkMs)
        {
            return await _transcriber.TranscribeAsync(
                wavPath,
                options,
                workDirectory,
                onProgress,
                cancellationToken);
        }

        var chunks = BuildChunks(durationMs, chunkMs, OverlapMilliseconds);
        var merged = new List<TranscriptSegment>();
        var backends = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextGlobalSpeaker = 1;
        long previousChunkEnd = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = chunks[i];
            var chunkPath = await _ffmpeg.ExtractWavSegmentAsync(
                wavPath,
                workDirectory,
                i + 1,
                chunk.StartMilliseconds,
                chunk.DurationMilliseconds,
                options,
                cancellationToken);

            try
            {
                double maxChunkProgress = 0;

                void ForwardProgress(EngineProgress progress)
                {
                    var weighted = WeightPhase(progress);
                    if (weighted is double value)
                        maxChunkProgress = Math.Max(maxChunkProgress, value);

                    var overall = ((i + maxChunkProgress / 100d) / chunks.Count) * 100d;

                    long? absolutePosition = null;
                    if (progress.PositionMilliseconds is long localPosition)
                    {
                        absolutePosition = Math.Min(
                            durationMs,
                            chunk.StartMilliseconds + localPosition);
                    }

                    // Preserve fallback phases so the WinForms queue can record
                    // the actual Vulkan failure reason in its visible Note column.
                    if (progress.Phase is "MOSS_GPU_FALLBACK" or "MOSS_CPU_FALLBACK")
                    {
                        onProgress?.Invoke(new EngineProgress(
                            progress.Phase,
                            $"分段 {i + 1}/{chunks.Count} · {progress.Message}",
                            overall,
                            absolutePosition,
                            durationMs,
                            progress.IsEstimate,
                            progress.DiagnosticDetail));
                        return;
                    }

                    onProgress?.Invoke(new EngineProgress(
                        "MOSS_LONG",
                        $"分段 {i + 1}/{chunks.Count} · {progress.Message}",
                        overall,
                        absolutePosition,
                        durationMs,
                        progress.IsEstimate));
                }

                var chunkResult = await _transcriber.TranscribeAsync(
                    chunkPath,
                    options,
                    workDirectory,
                    ForwardProgress,
                    cancellationToken);

                backends.Add(chunkResult.Backend);

                var absoluteSegments = chunkResult.Segments
                    .Select(segment => new TranscriptSegment(
                        segment.StartMilliseconds + chunk.StartMilliseconds,
                        segment.EndMilliseconds + chunk.StartMilliseconds,
                        segment.SpeakerId,
                        segment.Text))
                    .ToList();

                var mapped = MapSpeakers(
                    merged,
                    absoluteSegments,
                    chunk.StartMilliseconds,
                    previousChunkEnd,
                    ref nextGlobalSpeaker);

                if (i == 0)
                {
                    merged.AddRange(mapped);
                }
                else
                {
                    var handoff = chunk.StartMilliseconds + OverlapMilliseconds / 2;
                    merged.RemoveAll(segment => Midpoint(segment) >= handoff);
                    merged.AddRange(mapped.Where(segment => Midpoint(segment) >= handoff));
                }

                merged.Sort((a, b) => a.StartMilliseconds.CompareTo(b.StartMilliseconds));
                previousChunkEnd = chunk.StartMilliseconds + chunk.DurationMilliseconds;

                onProgress?.Invoke(new EngineProgress(
                    "MOSS_LONG",
                    $"分段 {i + 1}/{chunks.Count} 识别完成",
                    ((i + 1d) / chunks.Count) * 100d,
                    Math.Min(previousChunkEnd, durationMs),
                    durationMs));
            }
            finally
            {
                TryDeleteFile(chunkPath);
            }
        }

        var backend = backends.Count switch
        {
            0 => normalizedBackend,
            1 => backends.First(),
            _ => "mixed"
        };

        return new TranscriptionResult
        {
            SourceAudioPath = options.AudioPath,
            ModelPath = options.ModelPath,
            Backend = backend,
            Language = options.Language,
            GeneratedAt = DateTimeOffset.Now,
            Segments = merged,
            FullText = string.Join(" ", merged.Select(x => x.Text))
        };
    }

    private static List<AudioChunk> BuildChunks(long durationMs, long chunkMs, long overlapMs)
    {
        var chunks = new List<AudioChunk>();
        var step = chunkMs - overlapMs;
        var start = 0L;

        while (start < durationMs)
        {
            var remaining = durationMs - start;
            var duration = Math.Min(chunkMs, remaining);
            chunks.Add(new AudioChunk(start, duration));

            if (start + duration >= durationMs)
                break;

            start += step;
        }

        return chunks;
    }

    private static double? WeightPhase(EngineProgress progress)
    {
        var percent = progress.Percent ?? 0d;
        return progress.Phase switch
        {
            "MOSS_START" => 0,
            "MOSS_ENCODE" => 0 + percent * 0.20,
            "MOSS_ADAPTOR" => 20 + percent * 0.05,
            "MOSS_PREFILL" => 25 + percent * 0.10,
            "MOSS_DECODE" => 35 + percent * 0.65,
            "MOSS_DONE" => 100,
            "MOSS_GPU_FALLBACK" => null,
            "MOSS_CPU_FALLBACK" => null,
            _ => null
        };
    }

    private static List<TranscriptSegment> MapSpeakers(
        IReadOnlyList<TranscriptSegment> existing,
        IReadOnlyList<TranscriptSegment> incoming,
        long overlapStart,
        long previousChunkEnd,
        ref int nextGlobalSpeaker)
    {
        var mapping = new Dictionary<int, int>();
        var localSpeakers = incoming
            .Where(x => x.SpeakerId > 0)
            .Select(x => x.SpeakerId)
            .Distinct()
            .ToList();

        if (existing.Count == 0)
        {
            foreach (var local in localSpeakers)
                mapping[local] = nextGlobalSpeaker++;
        }
        else
        {
            var overlapEnd = previousChunkEnd;
            var scores = new List<(int Local, int Global, long Score)>();

            foreach (var local in localSpeakers)
            {
                var localSegments = incoming
                    .Where(x => x.SpeakerId == local && x.EndMilliseconds > overlapStart && x.StartMilliseconds < overlapEnd)
                    .ToList();

                foreach (var global in existing
                             .Where(x => x.SpeakerId > 0 && x.EndMilliseconds > overlapStart && x.StartMilliseconds < overlapEnd)
                             .Select(x => x.SpeakerId)
                             .Distinct())
                {
                    long score = 0;
                    foreach (var a in localSegments)
                    {
                        foreach (var b in existing.Where(x => x.SpeakerId == global))
                        {
                            var start = Math.Max(a.StartMilliseconds, b.StartMilliseconds);
                            var end = Math.Min(a.EndMilliseconds, b.EndMilliseconds);
                            if (end > start)
                                score += end - start;
                        }
                    }

                    if (score > 0)
                        scores.Add((local, global, score));
                }
            }

            var usedLocal = new HashSet<int>();
            var usedGlobal = new HashSet<int>();

            foreach (var candidate in scores.OrderByDescending(x => x.Score))
            {
                if (candidate.Score < 250)
                    continue;
                if (!usedLocal.Add(candidate.Local))
                    continue;
                if (!usedGlobal.Add(candidate.Global))
                {
                    usedLocal.Remove(candidate.Local);
                    continue;
                }

                mapping[candidate.Local] = candidate.Global;
            }

            foreach (var local in localSpeakers)
            {
                if (!mapping.ContainsKey(local))
                    mapping[local] = nextGlobalSpeaker++;
            }
        }

        return incoming
            .Select(segment => new TranscriptSegment(
                segment.StartMilliseconds,
                segment.EndMilliseconds,
                segment.SpeakerId > 0 && mapping.TryGetValue(segment.SpeakerId, out var global)
                    ? global
                    : 0,
                segment.Text))
            .ToList();
    }

    private static long Midpoint(TranscriptSegment segment)
        => segment.StartMilliseconds +
           Math.Max(0, segment.EndMilliseconds - segment.StartMilliseconds) / 2;

    private static string NormalizeBackend(string backend)
    {
        var value = backend.Trim().ToLowerInvariant();
        return value is "cpu" or "vulkan" ? value : "auto";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The task-level temp directory cleanup will retry later.
        }
    }

    private sealed record AudioChunk(long StartMilliseconds, long DurationMilliseconds);
}

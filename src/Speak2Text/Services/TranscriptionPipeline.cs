using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class TranscriptionPipeline
{
    private readonly FfmpegService _ffmpeg;
    private readonly LongAudioTranscriptionService _longAudioTranscriber;
    private readonly TranscriptExporter _exporter;

    public TranscriptionPipeline()
    {
        var processRunner = new ProcessRunner();
        _ffmpeg = new FfmpegService(processRunner);
        var transcriber = new TranscribeCliService(processRunner);
        _longAudioTranscriber = new LongAudioTranscriptionService(_ffmpeg, transcriber);
        _exporter = new TranscriptExporter();
    }

    public async Task<PipelineResult> RunAsync(
        TranscriptionOptions options,
        IProgress<PipelineMessage>? progress,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);
        var workDirectory = AppPaths.CreateTemporaryWorkDirectory();

        try
        {
            progress?.Report(new PipelineMessage(
                PipelineStage.Converting,
                "FFMPEG",
                "正在读取音频信息并准备转换…"));

            var wavPath = await _ffmpeg.ConvertToMono16KhzWavAsync(
                options.AudioPath,
                workDirectory,
                options,
                engine => progress?.Report(PipelineMessage.FromEngine(PipelineStage.Converting, engine)),
                cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Transcribing,
                "MOSS_LOAD",
                "正在加载 MOSS Q8 模型…"));

            var result = await _longAudioTranscriber.TranscribeAsync(
                wavPath,
                options,
                workDirectory,
                engine => progress?.Report(PipelineMessage.FromEngine(PipelineStage.Transcribing, engine)),
                cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Exporting,
                "EXPORT",
                "正在生成 Markdown / TXT / SRT / JSON…"));

            var files = await _exporter.ExportAsync(result, options, cancellationToken);

            progress?.Report(new PipelineMessage(
                PipelineStage.Completed,
                "DONE",
                "转写完成。",
                100));

            return new PipelineResult(result, files);
        }
        finally
        {
            await CleanupWorkDirectoryAsync(workDirectory);
        }
    }

    private static void ValidateOptions(TranscriptionOptions options)
    {
        if (!File.Exists(options.AudioPath))
            throw new FileNotFoundException("录音文件不存在。", options.AudioPath);
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("模型文件不存在。", options.ModelPath);
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("请选择输出目录。", nameof(options.OutputDirectory));
    }

    private static async Task CleanupWorkDirectoryAsync(string directory)
    {
        const int attempts = 5;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                Directory.Delete(directory, recursive: true);
                return;
            }
            catch when (attempt < attempts)
            {
                await Task.Delay(250);
            }
            catch
            {
                return;
            }
        }
    }
}

public enum PipelineStage
{
    Converting,
    Transcribing,
    Exporting,
    Completed
}

public sealed record PipelineMessage(
    PipelineStage Stage,
    string Phase,
    string Message,
    double? Percent = null,
    long? PositionMilliseconds = null,
    long? DurationMilliseconds = null,
    bool IsEstimate = false)
{
    public static PipelineMessage FromEngine(PipelineStage stage, EngineProgress engine)
        => new(
            stage,
            engine.Phase,
            engine.Message,
            engine.Percent,
            engine.PositionMilliseconds,
            engine.DurationMilliseconds,
            engine.IsEstimate);
}

public sealed record PipelineResult(TranscriptionResult Transcript, IReadOnlyList<string> ExportedFiles);

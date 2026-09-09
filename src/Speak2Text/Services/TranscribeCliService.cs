using System.Text;
using System.Text.Json;
using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class TranscribeCliService(ProcessRunner processRunner)
{
    public async Task<TranscriptionResult> TranscribeAsync(
        string wavPath,
        TranscriptionOptions options,
        string workDirectory,
        Action<string>? onLog,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.TranscribeCliPath))
            throw new FileNotFoundException(
                "未找到 transcribe-cli.exe。请将 transcribe.cpp 的 Windows CLI 及其依赖文件放到程序目录的 engine 文件夹中。",
                AppPaths.TranscribeCliPath);

        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("未找到 MOSS GGUF 模型文件。", options.ModelPath);

        var batchFile = Path.Combine(workDirectory, "batch.txt");
        await File.WriteAllTextAsync(batchFile, wavPath + Environment.NewLine, new UTF8Encoding(false), cancellationToken);

        var backend = NormalizeBackend(options.Backend);
        var args = new List<string>
        {
            "-q",
            "-m", options.ModelPath,
            "--diarize",
            "--timestamps", "segment",
            "--backend", backend,
            "--batch", batchFile,
            "--batch-jsonl"
        };

        if (options.CpuThreadLimit > 0)
        {
            args.Add("--threads");
            args.Add(options.CpuThreadLimit.ToString());
        }

        if (!string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-l");
            args.Add(options.Language);
        }

        int? dutyCycle = null;
        if (options.LimitGpu && backend != "cpu" && options.MaxGpuPercent < 100)
            dutyCycle = Math.Clamp(options.MaxGpuPercent, 10, 99);

        var result = await processRunner.RunAsync(
            AppPaths.TranscribeCliPath,
            args,
            AppPaths.EngineDirectory,
            onLog,
            cancellationToken,
            new ProcessRunOptions(
                LowPriority: options.LimitCpu || options.LimitGpu,
                DutyCyclePercent: dutyCycle));

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"transcribe-cli 执行失败，退出代码 {result.ExitCode}。\r\n{result.StandardError}".Trim());

        return ParseJsonLines(result.StandardOutput, options);
    }

    private static TranscriptionResult ParseJsonLines(string jsonLines, TranscriptionOptions options)
    {
        string fullText = string.Empty;
        var segments = new List<TranscriptSegment>();
        string? reportedError = null;

        foreach (var rawLine in jsonLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(rawLine);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var errorNode) && errorNode.ValueKind == JsonValueKind.String)
                    reportedError = errorNode.GetString();

                if (root.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
                    fullText = textNode.GetString() ?? fullText;

                if (!root.TryGetProperty("segments", out var segmentArray) || segmentArray.ValueKind != JsonValueKind.Array)
                    continue;

                segments.Clear();
                foreach (var segment in segmentArray.EnumerateArray())
                {
                    var text = GetString(segment, "text");
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    var t0 = GetInt64(segment, "t0_ms");
                    var t1 = GetInt64(segment, "t1_ms");
                    var speaker = GetInt32(segment, "speaker_id");
                    segments.Add(new TranscriptSegment(t0, t1, speaker, text.Trim()));
                }
            }
            catch (JsonException)
            {
                // Quiet mode should keep stdout JSON-only. Ignore any incidental non-JSON lines defensively.
            }
        }

        if (!string.IsNullOrWhiteSpace(reportedError))
            throw new InvalidOperationException($"模型转写失败：{reportedError}");

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
            segments.Add(new TranscriptSegment(0, 0, 0, fullText.Trim()));

        if (segments.Count == 0)
        {
            var preview = jsonLines.Length > 1200 ? jsonLines[..1200] + "…" : jsonLines;
            throw new InvalidOperationException($"未能从 transcribe-cli 输出中解析到转写结果。\r\n\r\nCLI 输出：\r\n{preview}");
        }

        if (string.IsNullOrWhiteSpace(fullText))
            fullText = string.Join(" ", segments.Select(x => x.Text));

        return new TranscriptionResult
        {
            SourceAudioPath = options.AudioPath,
            ModelPath = options.ModelPath,
            Backend = NormalizeBackend(options.Backend),
            Language = options.Language,
            GeneratedAt = DateTimeOffset.Now,
            Segments = segments,
            FullText = fullText
        };
    }

    private static string NormalizeBackend(string backend)
    {
        var value = backend.Trim().ToLowerInvariant();
        return value is "cpu" or "vulkan" ? value : "auto";
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString() ?? string.Empty
            : string.Empty;

    private static long GetInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var node) && node.TryGetInt64(out var value) ? value : 0;

    private static int GetInt32(JsonElement element, string property)
        => element.TryGetProperty(property, out var node) && node.TryGetInt32(out var value) ? value : 0;
}

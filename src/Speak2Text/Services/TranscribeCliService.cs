using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Speak2Text.Models;
using Speak2Text.Utilities;

namespace Speak2Text.Services;

public sealed class TranscribeCliService(ProcessRunner processRunner)
{
    public async Task<TranscriptionResult> TranscribeAsync(
        string wavPath,
        TranscriptionOptions options,
        string workDirectory,
        Action<EngineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.TranscribeCliPath))
            throw new FileNotFoundException(
                "未找到 transcribe-cli.exe。请将 Speak2Text Action 构建的 patched CLI 放到 engine 文件夹中。",
                AppPaths.TranscribeCliPath);

        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("未找到 MOSS GGUF 模型文件。", options.ModelPath);

        using var nativePaths = NativePathBridge.Create(options.ModelPath, workDirectory);

        var batchFile = Path.Combine(workDirectory, "batch.txt");
        var nativeWavPath = nativePaths.MapWorkPath(wavPath);
        var nativeBatchFile = nativePaths.MapWorkPath(batchFile);

        await File.WriteAllTextAsync(
            batchFile,
            nativeWavPath + Environment.NewLine,
            new UTF8Encoding(false),
            cancellationToken);

        var requestedBackend = NormalizeBackend(options.Backend);

        var firstAttempt = await RunBackendAsync(
            requestedBackend,
            nativePaths.ModelPath,
            nativeBatchFile,
            options,
            onProgress,
            cancellationToken);

        if (firstAttempt.ExitCode == 0)
            return ParseJsonLines(firstAttempt.StandardOutput, options, requestedBackend);

        if (requestedBackend == "cpu")
        {
            throw new InvalidOperationException(
                $"transcribe-cli CPU 执行失败，退出代码 {firstAttempt.ExitCode}。\r\n{firstAttempt.StandardError}".Trim());
        }

        cancellationToken.ThrowIfCancellationRequested();

        var vulkanReason = DescribeVulkanFailure(firstAttempt.StandardError);
        if (vulkanReason is null)
        {
            throw new InvalidOperationException(
                $"transcribe-cli {requestedBackend} 执行失败，但未检测到 Vulkan/显存类故障，因此不自动切换 CPU。\r\n" +
                TrimError(firstAttempt.StandardError));
        }

        onProgress?.Invoke(new EngineProgress(
            "MOSS_GPU_FALLBACK",
            $"Vulkan 失败：{vulkanReason}；正在使用同一临时 WAV 自动切换 CPU 重试…",
            DiagnosticDetail: TrimError(firstAttempt.StandardError)));

        var cpuAttempt = await RunBackendAsync(
            "cpu",
            nativePaths.ModelPath,
            nativeBatchFile,
            options,
            onProgress,
            cancellationToken);

        if (cpuAttempt.ExitCode == 0)
        {
            onProgress?.Invoke(new EngineProgress(
                "MOSS_CPU_FALLBACK",
                $"已自动切换 CPU 并继续处理。原 Vulkan 错误：{vulkanReason}",
                DiagnosticDetail: TrimError(firstAttempt.StandardError)));

            return ParseJsonLines(cpuAttempt.StandardOutput, options, "cpu");
        }

        throw new InvalidOperationException(
            $"Vulkan 失败（{vulkanReason}），自动切换 CPU 后仍然失败。\r\n\r\n" +
            $"Vulkan 原始错误（退出代码 {firstAttempt.ExitCode}）：\r\n{TrimError(firstAttempt.StandardError)}\r\n\r\n" +
            $"CPU 错误（退出代码 {cpuAttempt.ExitCode}）：\r\n{TrimError(cpuAttempt.StandardError)}");
    }

    private async Task<ProcessResult> RunBackendAsync(
        string backend,
        string modelPath,
        string batchFile,
        TranscriptionOptions options,
        Action<EngineProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "-q",
            "-m", modelPath,
            "--diarize",
            "--timestamps", "segment",
            "--backend", backend
        };

        // Long-form MOSS decoding grows its KV cache. Explicit F16 KV on
        // GPU/Vulkan cuts this part of device memory roughly in half versus F32.
        // CPU keeps AUTO because system RAM is much less constrained.
        if (backend != "cpu")
        {
            args.Add("--kv-type");
            args.Add("f16");
        }

        args.Add("--batch");
        args.Add(batchFile);
        args.Add("--batch-jsonl");

        if (options.CpuThreadLimit > 0)
        {
            args.Add("--threads");
            args.Add(options.CpuThreadLimit.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-l");
            args.Add(options.Language);
        }

        int? dutyCycle = null;
        if (options.LimitGpu && backend != "cpu" && options.MaxGpuPercent < 100)
            dutyCycle = Math.Clamp(options.MaxGpuPercent, 10, 99);

        void HandleEngineLine(string line)
        {
            var parsed = ParseMossProgress(line);
            if (parsed is not null)
                onProgress?.Invoke(parsed);
        }

        return await processRunner.RunAsync(
            AppPaths.TranscribeCliPath,
            args,
            AppPaths.EngineDirectory,
            HandleEngineLine,
            cancellationToken,
            new ProcessRunOptions(
                LowPriority: options.LimitCpu || (backend != "cpu" && options.LimitGpu),
                DutyCyclePercent: dutyCycle));
    }

    private static EngineProgress? ParseMossProgress(string line)
    {
        if (!line.StartsWith("S2T_PROGRESS|MOSS|", StringComparison.Ordinal))
            return null;

        var parts = line.Split('|');
        if (parts.Length != 7)
            return null;

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var done) ||
            !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
            !long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var coveredMs) ||
            !long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var audioMs))
        {
            return null;
        }

        var phase = parts[2].Trim().ToUpperInvariant();
        double? percent = null;
        var isEstimate = false;
        string message;

        switch (phase)
        {
            case "START":
                message = "MOSS 模型已加载，准备开始识别";
                percent = 0;
                break;

            case "ENCODE":
                message = $"MOSS 音频编码 {done}/{Math.Max(total, 1)}";
                percent = Fraction(done, total);
                break;

            case "ADAPTOR":
                message = "MOSS 音频特征适配";
                percent = Fraction(done, total);
                break;

            case "PREFILL":
                message = $"MOSS 解码预填充 {done}/{Math.Max(total, 1)}";
                percent = Fraction(done, total);
                break;

            case "DECODE":
                if (audioMs > 0 && coveredMs > 0)
                {
                    percent = Math.Clamp(coveredMs * 100d / audioMs, 0d, 100d);
                    message = $"MOSS 正在生成转写，已覆盖 {FormatMediaTime(coveredMs)}";
                }
                else
                {
                    percent = Fraction(done, total);
                    isEstimate = true;
                    message = $"MOSS 正在生成转写，已生成 {done} token";
                }
                break;

            case "DONE":
                message = "MOSS 识别完成";
                percent = 100;
                coveredMs = audioMs;
                break;

            default:
                return null;
        }

        return new EngineProgress(
            $"MOSS_{phase}",
            message,
            percent,
            coveredMs > 0 ? coveredMs : null,
            audioMs > 0 ? audioMs : null,
            isEstimate);
    }

    private static double? Fraction(int done, int total)
    {
        if (total <= 0)
            return null;

        return Math.Clamp(done * 100d / total, 0d, 100d);
    }

    private static string FormatMediaTime(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
    }

    private static TranscriptionResult ParseJsonLines(
        string jsonLines,
        TranscriptionOptions options,
        string effectiveBackend)
    {
        string fullText = string.Empty;
        var segments = new List<TranscriptSegment>();
        string? reportedError = null;

        foreach (var rawLine in jsonLines.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(rawLine);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var errorNode) &&
                    errorNode.ValueKind == JsonValueKind.String)
                {
                    reportedError = errorNode.GetString();
                }

                if (root.TryGetProperty("text", out var textNode) &&
                    textNode.ValueKind == JsonValueKind.String)
                {
                    fullText = textNode.GetString() ?? fullText;
                }

                if (!root.TryGetProperty("segments", out var segmentArray) ||
                    segmentArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

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
                // Quiet mode keeps stdout JSON-only; ignore incidental lines defensively.
            }
        }

        if (!string.IsNullOrWhiteSpace(reportedError))
            throw new InvalidOperationException($"模型转写失败：{reportedError}");

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
            segments.Add(new TranscriptSegment(0, 0, 0, fullText.Trim()));

        if (segments.Count == 0)
        {
            var preview = jsonLines.Length > 1200
                ? jsonLines[..1200] + "…"
                : jsonLines;

            throw new InvalidOperationException(
                $"未能从 transcribe-cli 输出中解析到转写结果。\r\n\r\nCLI 输出：\r\n{preview}");
        }

        if (string.IsNullOrWhiteSpace(fullText))
            fullText = string.Join(" ", segments.Select(x => x.Text));

        return new TranscriptionResult
        {
            SourceAudioPath = options.AudioPath,
            ModelPath = options.ModelPath,
            Backend = effectiveBackend,
            Language = options.Language,
            GeneratedAt = DateTimeOffset.Now,
            Segments = segments,
            FullText = fullText
        };
    }

    private static string? DescribeVulkanFailure(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return null;

        var text = stderr.ToLowerInvariant();

        var hasVulkanSignature =
            text.Contains("ggml_vulkan") ||
            text.Contains("vulkan0") ||
            text.Contains("vk_error_") ||
            text.Contains("erroroutofdevicememory") ||
            text.Contains("device memory allocation") ||
            text.Contains("failed to allocate vulkan");

        if (!hasVulkanSignature)
            return null;

        var requestedBytes = TryExtractRequestedBytes(stderr);

        if (text.Contains("erroroutofdevicememory") ||
            text.Contains("out_of_device_memory") ||
            text.Contains("device memory allocation") ||
            text.Contains("kv cache allocation failed") ||
            text.Contains("buffer alloc failed"))
        {
            var size = requestedBytes is long bytes
                ? $"，单次申请约 {FormatBytes(bytes)}"
                : string.Empty;

            return $"Vulkan 显存/设备内存不足或 KV cache 分配失败{size}";
        }

        if (text.Contains("buffer size limit") ||
            text.Contains("exceeds device buffer size limit"))
        {
            var size = requestedBytes is long bytes
                ? $"（请求约 {FormatBytes(bytes)}）"
                : string.Empty;

            return $"请求的 Vulkan 缓冲区超过设备/驱动单次 buffer 限制{size}";
        }

        if (text.Contains("device_lost") || text.Contains("device lost"))
            return "Vulkan 设备丢失或显卡驱动发生重置";

        if (text.Contains("failed to create") || text.Contains("init") || text.Contains("initializ"))
            return "Vulkan 后端初始化失败";

        return "Vulkan 后端执行失败";
    }

    private static long? TryExtractRequestedBytes(string stderr)
    {
        var patterns = new[]
        {
            @"device memory allocation of size\s+(\d+)\s+failed",
            @"vulkan\d* buffer of size\s+(\d+)",
            @"buffer of size\s+(\d+)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(stderr, pattern, RegexOptions.IgnoreCase);
            if (match.Success &&
                long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) &&
                bytes > 0)
            {
                return bytes;
            }
        }

        return null;
    }

    private static string FormatBytes(long bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double mib = 1024d * 1024d;

        if (bytes >= gib)
            return $"{bytes / gib:0.0} GiB";

        return $"{bytes / mib:0} MiB";
    }

    private static string TrimError(string value)
    {
        value = value.Trim();
        if (value.Length <= 4000)
            return value;

        return "…" + value[^4000..];
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
        => element.TryGetProperty(property, out var node) && node.TryGetInt64(out var value)
            ? value
            : 0;

    private static int GetInt32(JsonElement element, string property)
        => element.TryGetProperty(property, out var node) && node.TryGetInt32(out var value)
            ? value
            : 0;
}

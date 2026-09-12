using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Speak2Text.Services;

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        Action<string>? onStandardError,
        CancellationToken cancellationToken,
        ProcessRunOptions? options = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
                : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrWhiteSpace(options?.PathPrepend))
        {
            startInfo.Environment.TryGetValue("PATH", out var currentPath);
            startInfo.Environment["PATH"] = string.IsNullOrWhiteSpace(currentPath)
                ? options.PathPrepend
                : options.PathPrepend + Path.PathSeparator + currentPath;
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException($"无法启动进程：{executable}");

        ApplyPriority(process, options);

        using var throttleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var throttleTask = StartThrottleIfNeeded(process, options, throttleCts.Token);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = ReadStandardErrorAsync(process, onStandardError, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            throttleCts.Cancel();
            try
            {
                await throttleTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Throttling is best-effort and must never hide the transcription result.
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static void ApplyPriority(Process process, ProcessRunOptions? options)
    {
        if (options?.LowPriority != true)
            return;

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Priority reduction is advisory. Continue if Windows denies it.
        }
    }

    private static Task StartThrottleIfNeeded(
        Process process,
        ProcessRunOptions? options,
        CancellationToken cancellationToken)
    {
        if (options?.DutyCyclePercent is not int duty || duty >= 100)
            return Task.CompletedTask;

        duty = Math.Clamp(duty, 10, 99);
        return ThrottleProcessAsync(process, duty, cancellationToken);
    }

    private static async Task ThrottleProcessAsync(
        Process process,
        int dutyCyclePercent,
        CancellationToken cancellationToken)
    {
        const int periodMilliseconds = 500;
        var runMilliseconds = Math.Max(50, periodMilliseconds * dutyCyclePercent / 100);
        var pauseMilliseconds = Math.Max(20, periodMilliseconds - runMilliseconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (HasExited(process))
                return;

            await Task.Delay(runMilliseconds, cancellationToken);

            if (HasExited(process))
                return;

            using var suspended = SuspendedThreadSet.TrySuspend(process);
            if (suspended.Count == 0)
                continue;

            try
            {
                await Task.Delay(pauseMilliseconds, cancellationToken);
            }
            finally
            {
                suspended.ResumeAll();
            }
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<string> ReadStandardErrorAsync(
        Process process,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line is null)
                break;

            buffer.AppendLine(line);
            onLine?.Invoke(line);
        }

        return buffer.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cancellation only.
        }
    }

    private sealed class SuspendedThreadSet : IDisposable
    {
        private const uint ThreadSuspendResume = 0x0002;
        private const uint SuspendFailed = uint.MaxValue;
        private readonly List<IntPtr> _threadHandles = [];
        private bool _resumed;

        public int Count => _threadHandles.Count;

        public static SuspendedThreadSet TrySuspend(Process process)
        {
            var result = new SuspendedThreadSet();

            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    var handle = OpenThread(ThreadSuspendResume, false, (uint)thread.Id);
                    if (handle == IntPtr.Zero)
                        continue;

                    var previousCount = SuspendThread(handle);
                    if (previousCount == SuspendFailed)
                    {
                        CloseHandle(handle);
                        continue;
                    }

                    result._threadHandles.Add(handle);
                }
            }
            catch
            {
                result.ResumeAll();
            }

            return result;
        }

        public void ResumeAll()
        {
            if (_resumed)
                return;

            _resumed = true;
            foreach (var handle in _threadHandles)
            {
                try
                {
                    ResumeThread(handle);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            _threadHandles.Clear();
        }

        public void Dispose() => ResumeAll();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

public sealed record ProcessRunOptions(
    bool LowPriority = false,
    int? DutyCyclePercent = null,
    string? PathPrepend = null);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

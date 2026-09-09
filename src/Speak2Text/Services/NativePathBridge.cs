using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Speak2Text.Services;

/// <summary>
/// Gives the native transcribe.cpp process ASCII-only DOS paths when the
/// portable app/model/work directory contains Chinese or other non-ASCII
/// characters. This avoids Windows narrow-code-page conversion failures in
/// native dependencies without copying the ~1 GB GGUF model.
/// </summary>
public sealed class NativePathBridge : IDisposable
{
    private const uint DddRawTargetPath = 0x00000001;
    private const uint DddRemoveDefinition = 0x00000002;
    private const uint DddExactMatchOnRemove = 0x00000004;
    private const uint DddNoBroadcastSystem = 0x00000008;

    private static readonly object DriveLetterLock = new();

    private readonly List<DriveMapping> _mappings = [];

    private NativePathBridge()
    {
    }

    public string ModelPath { get; private set; } = string.Empty;
    public string WorkDirectory { get; private set; } = string.Empty;

    public static NativePathBridge Create(string modelPath, string workDirectory)
    {
        var bridge = new NativePathBridge();

        try
        {
            var fullModelPath = Path.GetFullPath(modelPath);
            var fullWorkDirectory = Path.GetFullPath(workDirectory);

            bridge.ModelPath = fullModelPath;
            bridge.WorkDirectory = fullWorkDirectory;

            var modelDirectory = Path.GetDirectoryName(fullModelPath)
                ?? throw new InvalidOperationException("无法确定模型所在目录。");

            if (NeedsBridge(fullModelPath))
            {
                var modelMap = bridge.CreateDriveMapping(modelDirectory);
                bridge.ModelPath = Path.Combine(
                    modelMap.DriveRoot,
                    Path.GetFileName(fullModelPath));
            }

            if (NeedsBridge(fullWorkDirectory))
            {
                var workMap = bridge.CreateDriveMapping(fullWorkDirectory);
                bridge.WorkDirectory = workMap.DriveRoot.TrimEnd(Path.DirectorySeparatorChar);
            }

            return bridge;
        }
        catch
        {
            bridge.Dispose();
            throw;
        }
    }

    public string MapWorkPath(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!NeedsBridge(Path.GetFullPath(OriginalWorkDirectory)))
            return fullPath;

        var relative = Path.GetRelativePath(OriginalWorkDirectory, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
            throw new InvalidOperationException("待映射文件不在当前转写临时目录内。");

        return Path.Combine(WorkDirectory, relative);
    }

    private string OriginalWorkDirectory { get; set; } = string.Empty;

    private DriveMapping CreateDriveMapping(string directory)
    {
        if (string.IsNullOrEmpty(OriginalWorkDirectory) && Directory.Exists(directory))
        {
            // Filled below by Create() for the work-directory mapping path logic.
        }

        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (fullDirectory.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "当前原生路径桥接暂不支持包含非 ASCII 字符的 UNC 网络路径。请把模型或临时目录放到本地磁盘。");
        }

        lock (DriveLetterLock)
        {
            var usedMask = GetLogicalDrives();

            foreach (var letter in "ZYXWVUTSRQPONMLKJIHGFED")
            {
                var bit = 1u << (letter - 'A');
                if ((usedMask & bit) != 0)
                    continue;

                var deviceName = $"{letter}:";
                var rawTarget = @"\??\" + fullDirectory;

                if (!DefineDosDevice(
                        DddRawTargetPath | DddNoBroadcastSystem,
                        deviceName,
                        rawTarget))
                {
                    continue;
                }

                var mapping = new DriveMapping(
                    deviceName,
                    $"{deviceName}\\",
                    rawTarget);

                _mappings.Add(mapping);
                return mapping;
            }
        }

        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "无法为 transcribe.cpp 创建 ASCII 临时盘符映射。");
    }

    private static bool NeedsBridge(string path)
        => path.Any(ch => ch > 0x7F);

    public void Dispose()
    {
        lock (DriveLetterLock)
        {
            for (var i = _mappings.Count - 1; i >= 0; i--)
            {
                var mapping = _mappings[i];

                DefineDosDevice(
                    DddRawTargetPath |
                    DddRemoveDefinition |
                    DddExactMatchOnRemove |
                    DddNoBroadcastSystem,
                    mapping.DeviceName,
                    mapping.RawTarget);
            }

            _mappings.Clear();
        }
    }

    private sealed record DriveMapping(
        string DeviceName,
        string DriveRoot,
        string RawTarget);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DefineDosDevice(
        uint flags,
        string deviceName,
        string? targetPath);

    [DllImport("kernel32.dll")]
    private static extern uint GetLogicalDrives();
}

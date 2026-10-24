using System;
using System.Diagnostics;
using System.IO;

namespace Utility.Files;

/// <summary>Platform boundary for desktop file actions; paths are arguments,
/// never command strings. It contains no project or numerical logic.</summary>
public static class DesktopFileActions
{
    public enum DesktopPlatform { Windows, MacOS, Linux }

    public static DesktopPlatform CurrentPlatform => OperatingSystem.IsWindows()
        ? DesktopPlatform.Windows
        : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOS
        : OperatingSystem.IsLinux() ? DesktopPlatform.Linux
        : throw new PlatformNotSupportedException("No desktop file handler is available on this platform.");

    public static ProcessStartInfo CreateOpenRequest(string path, DesktopPlatform platform, bool reveal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (platform == DesktopPlatform.Windows)
        {
            // Selecting a Windows file remains in FileHelpers' shell32 branch;
            // this method is the common system-default-viewer operation.
            return new ProcessStartInfo(path) { UseShellExecute = true };
        }
        string absolutePath = Path.GetFullPath(path);
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = platform == DesktopPlatform.MacOS ? "/usr/bin/open" : "xdg-open",
            UseShellExecute = false
        };
        if (platform == DesktopPlatform.MacOS)
        {
            if (reveal) startInfo.ArgumentList.Add("-R");
            startInfo.ArgumentList.Add("--");
        }
        else if (reveal)
        {
            // The portable Linux desktop handler can open the containing
            // folder; selecting a specific item depends on the file manager.
            absolutePath = Directory.Exists(absolutePath) ? absolutePath : Path.GetDirectoryName(absolutePath)!;
        }
        startInfo.ArgumentList.Add(absolutePath);
        return startInfo;
    }

    public static void Open(string path, bool reveal = false)
    {
        using Process process = Process.Start(CreateOpenRequest(path, CurrentPlatform, reveal))
            ?? throw new InvalidOperationException("The desktop file handler could not be started.");
    }
}

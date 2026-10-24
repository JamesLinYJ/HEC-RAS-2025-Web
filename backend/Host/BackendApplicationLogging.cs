using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Utility.Logging;

namespace Ras.Backend;

/// <summary>
/// Installs the retained source's file logger for Host operations. The backend
/// child process writes only to the shared log files; protocol stdout remains
/// exclusively owned by BackendStandardOutput.
/// </summary>
public static class BackendApplicationLogging
{
    public static IDisposable Initialize(string? logDirectory = null)
    {
        ILoggerFactory previous = LogHelper.LoggerFactory;
        var provider = new BackendApplicationLogProvider(logDirectory);
        ILoggerFactory factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(provider);
        });
        LogHelper.LoggerFactory = factory;
        return new Registration(previous, factory);
    }

    /// <summary>Writes a top-level Host failure even if no source logger was created.</summary>
    public static void WriteUnhandled(Exception error, string? logDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        var writer = new SharedApplicationLogWriter(ResolveDirectory(logDirectory), null);
        writer.AppendUnhandled(error);
    }

    internal static string ResolveDirectory(string? logDirectory)
    {
        if (!string.IsNullOrWhiteSpace(logDirectory)) return Path.GetFullPath(logDirectory);
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(home))
                return Path.Combine(home, "Library", "Application Support", "HEC", "RAS", "Logs");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HEC", "RAS", "Logs");
    }

    private sealed class Registration(ILoggerFactory previous, ILoggerFactory installed) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (ReferenceEquals(LogHelper.LoggerFactory, installed)) LogHelper.LoggerFactory = previous;
            installed.Dispose();
        }
    }
}

/// <summary>
/// Mirrors Ras.Interface.WinUI.ObservableLog's Debug+ ras.log and Warning+
/// ras-errors.log thresholds and source timestamp formats.
/// </summary>
public sealed class BackendApplicationLogProvider : ILoggerProvider
{
    private readonly SharedApplicationLogWriter _writer;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);
    private int _disposed;

    public BackendApplicationLogProvider(string? logDirectory = null, Func<DateTimeOffset>? clock = null)
    {
        _writer = new SharedApplicationLogWriter(BackendApplicationLogging.ResolveDirectory(logDirectory), clock);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _loggers.GetOrAdd(categoryName ?? string.Empty, _ => new BackendApplicationLogger(_writer));
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _loggers.Clear();
    }
}

internal sealed class BackendApplicationLogger(SharedApplicationLogWriter writer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and <= LogLevel.Critical;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel)) return;
        string message = formatter(state, exception);
        writer.Append(logLevel, message, exception);
    }
}

internal sealed class SharedApplicationLogWriter
{
    private const int LockExclusive = 2;
    private const int LockUnlock = 8;
    private const int InterruptedSystemCall = 4;
    private const int DarwinOpenCreate = 0x0200;
    private const int DarwinOpenCloseOnExec = 0x01000000;
    private const int LinuxOpenCreate = 0x0040;
    private const int LinuxOpenCloseOnExec = 0x00080000;
    private const int OpenReadWrite = 0x0002;
    private const uint OwnerReadWriteMode = 0x0180;
    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;

    public SharedApplicationLogWriter(string directory, Func<DateTimeOffset>? clock)
    {
        _directory = Path.GetFullPath(directory);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public void Append(LogLevel level, string message, Exception? exception)
    {
        if (level < LogLevel.Debug || level > LogLevel.Critical || level == LogLevel.None) return;
        DateTimeOffset now = _clock();
        string rendered = message;
        AppendFile("ras.log", $"{now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {rendered}\n");
        if (level >= LogLevel.Warning)
        {
            string severity = level switch
            {
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "FTL",
                _ => "ERR"
            };
            string details = exception?.ToString() ?? string.Empty;
            string exceptionLine = details.Length == 0 ? string.Empty : details + "\n";
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            AppendFile("ras-errors.log", $"{timestamp} [{severity}] {rendered}\n{exceptionLine}");
        }
    }

    public void AppendUnhandled(Exception error)
    {
        Append(LogLevel.Critical, "Unhandled backend exception: " + error.Message, error);
    }

    private void AppendFile(string name, string content)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            SetPrivateMode(_directory, directory: true);
            string lockPath = Path.Combine(_directory, ".ras-log.lock");
            using IDisposable lockLease = AcquireExclusiveLock(lockPath);
            string path = Path.Combine(_directory, name);
            using var output = new FileStream(path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
            SetPrivateMode(path, directory: false);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            output.Write(bytes);
            output.Flush();
        }
        catch (Exception error)
        {
            // Logger failures must not replace the source operation's error or
            // contaminate the JSON protocol. Console.Error is redirected by Host.
            try { Console.Error.WriteLine("Application log write failed: " + error); }
            catch { /* Preserve the operation even if the diagnostic stream is unavailable. */ }
        }
    }

    private static IDisposable AcquireExclusiveLock(string lockPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.None);
            try
            {
                SetPrivateMode(lockPath, directory: false);
                lockFile.Lock(0, 1);
                return new WindowsFileLockLease(lockFile);
            }
            catch
            {
                lockFile.Dispose();
                throw;
            }
        }

        // On Darwin, FileStream sharing is implemented with OS file locks too;
        // opening a shared FileStream and then upgrading it with flock can
        // reject or deadlock another writer before it reaches this lock. A raw
        // descriptor keeps the only Unix lock on this stable file the flock below.
        bool isMacOS = OperatingSystem.IsMacOS();
        if (!isMacOS && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Shared application logging requires Windows, macOS, or Linux file locking.");

        int flags = OpenReadWrite | (isMacOS
            ? DarwinOpenCreate | DarwinOpenCloseOnExec
            : LinuxOpenCreate | LinuxOpenCloseOnExec);
        int descriptor = OpenUnixLockFile(lockPath, flags, OwnerReadWriteMode);
        if (descriptor < 0)
            throw new IOException("Could not open the shared application log lock file.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));

        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            if (ChangeUnixFileMode(descriptor, OwnerReadWriteMode) != 0)
                throw new IOException("Could not restrict permissions on the shared application log lock file.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
            AcquireUnixLock(handle);
            return new UnixFileLockLease(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void AcquireUnixLock(SafeFileHandle lockFile)
    {
        int descriptor = lockFile.DangerousGetHandle().ToInt32();
        while (Flock(descriptor, LockExclusive) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == InterruptedSystemCall) continue;
            throw new IOException("Could not acquire the shared application log lock.", new System.ComponentModel.Win32Exception(error));
        }
    }

    private static void ReleaseUnixLock(SafeFileHandle lockFile)
    {
        int descriptor = lockFile.DangerousGetHandle().ToInt32();
        if (Flock(descriptor, LockUnlock) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new IOException("Could not release the shared application log lock.", new System.ComponentModel.Win32Exception(error));
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class WindowsFileLockLease(FileStream stream) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { stream.Unlock(0, 1); }
            finally { stream.Dispose(); }
        }
    }

    private sealed class UnixFileLockLease(SafeFileHandle handle) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { ReleaseUnixLock(handle); }
            finally { handle.Dispose(); }
        }
    }

    private static void SetPrivateMode(string path, bool directory)
    {
        if (OperatingSystem.IsWindows()) return;
        UnixFileMode mode = directory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, mode);
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnixLockFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static extern int ChangeUnixFileMode(int descriptor, uint mode);
}

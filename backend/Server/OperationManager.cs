using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Ras.Backend;

namespace Ras.Backend.Server;

public sealed class OperationManager : IHostedService
{
    private const int MaximumOperations = 128;
    private const int MaximumRetainedTerminalOperations = 128;
    private const int MaximumEventsPerPage = 100;
    private const int MaximumQueuedControls = 256;
    private const int MaximumEventsPerOperation = 20_000;
    private const int MaximumProtocolLineCharacters = 8 * 1024 * 1024;
    // Result rendering and associated-map layers are bounded by an eight
    // megapixel aggregate Host budget. Their RGBA base64 plus identity
    // metadata needs a separate terminal budget.
    private const int MaximumRasterResultLineCharacters = 64 * 1024 * 1024;
    private const long MaximumEventBytesPerOperation = 64L * 1024 * 1024;
    private const long MaximumRetainedEventBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan TerminalRetention = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, OperationRecord> operations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim concurrency = new(1, 1);
    private readonly object registryGate = new();
    private readonly CancellationTokenSource cleanupStop = new();
    private Task cleanupTask = Task.CompletedTask;
    private bool stopping;
    private long retainedEventBytes;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cleanupTask = Task.Run(() => CleanupLoopAsync(cleanupStop.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cleanupStop.Cancel();
        OperationRecord[] current;
        lock (registryGate)
        {
            stopping = true;
            current = operations.Values.ToArray();
        }

        foreach (OperationRecord operation in current) RequestCancel(operation);
        Task[] jobs = current.Select(operation => operation.RunTask).Where(task => task is not null).Cast<Task>().ToArray();
        if (jobs.Length == 0)
        {
            try { await cleanupTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            return;
        }

        Task allJobs = Task.WhenAll(jobs);
        Task grace = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        if (await Task.WhenAny(allJobs, grace).ConfigureAwait(false) != allJobs)
        {
            foreach (OperationRecord operation in current)
                TryKill(operation.Process);
            try { await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is TimeoutException or OperationCanceledException or InvalidOperationException) { }
        }
        try { await cleanupTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    public bool TrySubmit(string method, JsonElement parameters, out string id)
    {
        lock (registryGate)
        {
            CleanupExpiredLocked(DateTimeOffset.UtcNow);
            int activeOrQueued = operations.Values.Count(operation => !operation.IsTerminal);
            if (stopping || activeOrQueued >= MaximumOperations ||
                operations.Count >= MaximumOperations + MaximumRetainedTerminalOperations)
            {
                id = string.Empty;
                return false;
            }

            id = Guid.NewGuid().ToString("N");
            var operation = new OperationRecord(id, method, parameters.Clone());
            if (!operations.TryAdd(id, operation))
            {
                id = string.Empty;
                return false;
            }
            operation.RunTask = Task.Run(() => RunOperationAsync(operation));
            return true;
        }
    }

    public OperationSnapshot? GetSnapshot(string id, long after)
    {
        lock (registryGate) CleanupExpiredLocked(DateTimeOffset.UtcNow);
        if (!operations.TryGetValue(id, out OperationRecord? operation)) return null;
        lock (operation.Gate)
        {
            operation.LastAccess = DateTimeOffset.UtcNow;
            long lastSequence = operation.Events.Count;
            if (after > lastSequence)
                return new OperationSnapshot(null, InvalidCursor: true);

            OperationEvent[] events = operation.Events
                .Where(item => item.Sequence > after)
                .Take(MaximumEventsPerPage)
                .ToArray();
            long cursor = events.Length == 0 ? after : events[^1].Sequence;
            bool hasMore = cursor < lastSequence;
            bool terminalPageDelivered = operation.IsTerminal && !hasMore && cursor >= lastSequence;
            string visibleState = operation.IsTerminal && !terminalPageDelivered ? "running" : operation.State;

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = operation.Id,
                ["state"] = visibleState,
                ["cursor"] = cursor,
                ["events"] = events.Select(item => new { sequence = item.Sequence, progress = item.Progress }).ToArray(),
                ["hasMore"] = hasMore,
            };
            if (terminalPageDelivered)
            {
                if (operation.Result is JsonElement result) payload["result"] = result;
                if (operation.Error is JsonElement error) payload["error"] = error;
            }
            return new OperationSnapshot(payload, InvalidCursor: false);
        }
    }

    public CancelRequestResult RequestCancel(string id)
    {
        if (!operations.TryGetValue(id, out OperationRecord? operation)) return new(false, false);
        return RequestCancel(operation);
    }

    public ControlSendResult SendControl(string id, JsonElement control)
    {
        if (!operations.TryGetValue(id, out OperationRecord? operation))
            return new(ControlSendStatus.NotFound, 0);
        lock (operation.Gate)
        {
            if (operation.IsTerminal || operation.Terminal is not null) return new(ControlSendStatus.Terminal, 0);
            // Reserve one queue slot so DELETE can always deliver the cancellation frame.
            if (operation.ControlsQueued >= MaximumQueuedControls - 1)
                return new(ControlSendStatus.LimitReached, 0);
            long sequence = ++operation.ControlSequence;
            operation.ControlsQueued++;
            if (!operation.ControlFrames.Writer.TryWrite(new DuplexControlFrame(operation.Id, sequence, control.Clone())))
                return new(ControlSendStatus.Terminal, 0);
            operation.LastAccess = DateTimeOffset.UtcNow;
            return new(ControlSendStatus.Accepted, sequence);
        }
    }

    private CancelRequestResult RequestCancel(OperationRecord operation)
    {
        lock (operation.Gate)
        {
            if (operation.IsTerminal || operation.Terminal is not null) return new(true, false);
            if (!operation.CancelRequested)
            {
                operation.CancelRequested = true;
                operation.LastAccess = DateTimeOffset.UtcNow;
                if (operation.PlanCancellation is PlanCancellationFiles cancellation)
                {
                    try { CreateCancelMarker(cancellation); }
                    catch (Exception error)
                    {
                        operation.ControlFailure = "Could not create the plan cancellation marker: " + error.Message;
                    }
                }
                if (operation.ControlsQueued < MaximumQueuedControls)
                {
                    long sequence = ++operation.ControlSequence;
                    operation.ControlsQueued++;
                    operation.ControlFrames.Writer.TryWrite(new DuplexControlFrame(operation.Id, sequence,
                        JsonSerializer.SerializeToElement(new { type = "cancel" }, BackendJson.Options)));
                }
            }
            return new(true, true);
        }
    }

    private async Task RunOperationAsync(OperationRecord operation)
    {
        bool entered = false;
        Process? process = null;
        Task? controlPump = null;
        Task<string?>? stderrPump = null;
        PlanCancellationFiles? planCancellation = null;
        string? stderrTail = null;
        try
        {
            await concurrency.WaitAsync().ConfigureAwait(false);
            entered = true;

            lock (operation.Gate)
            {
                if (operation.IsTerminal) return;
                if (operation.CancelRequested)
                {
                    SetTerminalLocked(operation, "canceled", error: null, result: null);
                    return;
                }

                if (operation.Method == "plan.compute.execute")
                {
                    planCancellation = CreatePlanCancellationFiles();
                    operation.PlanCancellation = planCancellation;
                    if (operation.CancelRequested) CreateCancelMarker(planCancellation);
                }

                process = StartWorkerProcess();
                operation.Process = process;
                string requestLine = BuildWorkerRequest(operation, planCancellation);
                process.StandardInput.WriteLine(requestLine);
                process.StandardInput.Flush();
                operation.State = "running";
                operation.StartedAt = DateTimeOffset.UtcNow;
                controlPump = Task.Run(() => PumpControlsAsync(operation, process));
            }

            stderrPump = DrainStandardErrorAsync(process);
            Task outputPump = ReadWorkerProtocolAsync(operation, process);
            try
            {
                await outputPump.ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                TryKill(process);
                try { await process.WaitForExitAsync().ConfigureAwait(false); }
                catch (InvalidOperationException) { }
                throw;
            }

            stderrTail = await stderrPump.ConfigureAwait(false);
            if (controlPump is not null)
            {
                operation.ControlFrames.Writer.TryComplete();
                try { await controlPump.ConfigureAwait(false); }
                catch (IOException) { }
            }

            lock (operation.Gate)
            {
                if (operation.ProtocolFailure is not null)
                    throw new InvalidDataException(operation.ProtocolFailure);
                TerminalResponse? terminal = operation.Terminal;
                if (terminal is null)
                {
                    string detail = string.IsNullOrWhiteSpace(stderrTail) ? "The worker exited without a terminal response." :
                        "The worker exited without a terminal response. stderr: " + stderrTail;
                    SetTerminalLocked(operation, "failed", null,
                        JsonSerializer.SerializeToElement(new { code = "emptyResponse", message = detail }, BackendJson.Options));
                }
                else if (process.ExitCode != 0)
                {
                    JsonElement error = terminal.Error ?? JsonSerializer.SerializeToElement(new
                    {
                        code = "workerExit",
                        message = string.IsNullOrWhiteSpace(stderrTail)
                            ? $"The worker exited with code {process.ExitCode}."
                            : $"The worker exited with code {process.ExitCode}. stderr: {stderrTail}",
                    }, BackendJson.Options);
                    SetTerminalLocked(operation, "failed", null, error);
                }
                else if (terminal.Error is JsonElement terminalError)
                {
                    SetTerminalLocked(operation, "failed", null, terminalError);
                }
                else if (terminal.Result is JsonElement terminalResult)
                {
                    string state = IsCanceledResult(terminalResult) ? "canceled" : "completed";
                    SetTerminalLocked(operation, state, terminalResult, null);
                }
                else
                {
                    SetTerminalLocked(operation, "failed", null,
                        JsonSerializer.SerializeToElement(new { code = "invalidResponse", message = "The worker terminal response contained neither result nor error." }, BackendJson.Options));
                }
            }
        }
        catch (Exception error)
        {
            if (process is not null)
            {
                TryKill(process);
                try { await process.WaitForExitAsync().ConfigureAwait(false); }
                catch (InvalidOperationException) { }
            }
            string message = error.Message;
            if (!string.IsNullOrWhiteSpace(stderrTail)) message += " stderr: " + stderrTail;
            lock (operation.Gate)
            {
                if (!operation.IsTerminal)
                    SetTerminalLocked(operation, "failed", null,
                        JsonSerializer.SerializeToElement(new
                        {
                            code = error is ProgressLimitExceededException ? "progressLimitExceeded" : "workerFailure",
                            message,
                        }, BackendJson.Options));
            }
        }
        finally
        {
            operation.ControlFrames.Writer.TryComplete();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
            }
            if (stderrPump is not null && process is not null)
            {
                try { stderrTail = await stderrPump.ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (operation.Gate) operation.Process = null;
            process?.Dispose();
            if (planCancellation is not null) RemovePlanCancellationFiles(planCancellation);
            if (entered) concurrency.Release();
        }
    }

    private static string BuildWorkerRequest(OperationRecord operation, PlanCancellationFiles? cancellation)
    {
        JsonNode? parameters = JsonNode.Parse(operation.Parameters.GetRawText());
        if (parameters is not JsonObject parameterObject)
            throw new InvalidDataException("Backend parameters must be a JSON object.");
        if (operation.Method == "plan.compute.execute")
        {
            if (cancellation is null) throw new InvalidOperationException("The plan cancellation channel was not initialized.");
            parameterObject["cancellation"] = new JsonObject
            {
                ["directory"] = cancellation.Directory,
                ["nonce"] = cancellation.Nonce,
            };
        }
        return JsonSerializer.Serialize(new
        {
            id = operation.Id,
            method = operation.Method,
            parameters,
            streamProgress = true,
            duplex = true,
        }, BackendJson.Options);
    }

    private static Process StartWorkerProcess()
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The server executable path is unavailable; cannot start an isolated backend worker.");
        string executableName = Path.GetFileNameWithoutExtension(executable);
        bool isDotnet = string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase);
        string? assembly = Assembly.GetEntryAssembly()?.Location;
        if (isDotnet && string.IsNullOrWhiteSpace(assembly))
            throw new InvalidOperationException("The server assembly location is unavailable for dotnet worker startup.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (isDotnet) startInfo.ArgumentList.Add(assembly!);
        startInfo.ArgumentList.Add("--backend-worker");
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The isolated backend worker could not be started.");
        }
        return process;
    }

    private static async Task PumpControlsAsync(OperationRecord operation, Process process)
    {
        try
        {
            await foreach (DuplexControlFrame frame in operation.ControlFrames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                string line = JsonSerializer.Serialize(new
                {
                    id = frame.Id,
                    controlSequence = frame.Sequence,
                    control = frame.Control,
                }, BackendJson.Options);
                await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            lock (operation.Gate) operation.ControlFailure ??= error.Message;
        }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (InvalidOperationException) { }
        }
    }

    private async Task ReadWorkerProtocolAsync(OperationRecord operation, Process process)
    {
        int maximumLineCharacters = operation.Method is "map.resultRender" or "map.associations.load"
            ? MaximumRasterResultLineCharacters : MaximumProtocolLineCharacters;
        var lines = new BoundedProtocolLineReader(process.StandardOutput, maximumLineCharacters);
        string? line;
        while ((line = await lines.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("id", out JsonElement id) || id.GetString() != operation.Id)
                    throw new InvalidDataException("The worker emitted a response with an invalid request identity.");

                lock (operation.Gate)
                {
                    if (operation.Terminal is not null)
                        throw new InvalidDataException("The worker emitted data after its terminal response.");
                    if (root.TryGetProperty("progress", out JsonElement progress))
                    {
                        if (line.Length > MaximumProtocolLineCharacters)
                            throw new ProgressLimitExceededException("The worker emitted a progress line larger than the server's protocol limit.");
                        int eventBytes = Encoding.UTF8.GetByteCount(line);
                        if (operation.Events.Count >= MaximumEventsPerOperation ||
                            eventBytes > MaximumEventBytesPerOperation - operation.EventBytes ||
                            !TryReserveEventBytes(eventBytes))
                            throw new ProgressLimitExceededException("The worker exceeded the server's bounded progress-event memory budget.");
                        operation.EventBytes += eventBytes;
                        operation.Events.Add(new OperationEvent(operation.Events.Count + 1L, progress.Clone()));
                        operation.LastAccess = DateTimeOffset.UtcNow;
                        continue;
                    }
                    bool hasResult = root.TryGetProperty("result", out JsonElement result);
                    bool hasError = root.TryGetProperty("error", out JsonElement error) && error.ValueKind != JsonValueKind.Null;
                    if (hasResult == hasError)
                        throw new InvalidDataException("The worker emitted a malformed terminal response.");
                    operation.Terminal = new TerminalResponse(
                        hasResult ? result.Clone() : null,
                        hasError ? error.Clone() : null);
                    operation.ControlFrames.Writer.TryComplete();
                }
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or ProgressLimitExceededException)
            {
                lock (operation.Gate) operation.ProtocolFailure ??= error.Message;
                throw;
            }
        }
    }

    private static async Task<string?> DrainStandardErrorAsync(Process process)
    {
        var tail = new StringBuilder(capacity: 8192);
        char[] buffer = new char[2048];
        while (true)
        {
            int read = await process.StandardError.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) break;
            tail.Append(buffer, 0, read);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
        }
        return tail.Length == 0 ? null : tail.ToString().Trim();
    }

    private static PlanCancellationFiles CreatePlanCancellationFiles()
    {
        string nonce = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(CanonicalPath(Path.GetTempPath()), "hecras-plan-compute-" + nonce);
        UnixFileMode directoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, directoryMode);
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, directoryMode);
            string ownerPath = Path.Combine(directory, "owner");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(ownerPath, options))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(nonce);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(ownerPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return new PlanCancellationFiles(directory, nonce, ownerPath, Path.Combine(directory, "cancel"));
        }
        catch
        {
            RemoveDirectoryBestEffort(directory);
            throw;
        }
    }

    private static void CreateCancelMarker(PlanCancellationFiles cancellation)
    {
        if (File.Exists(cancellation.MarkerPath))
        {
            if (new FileInfo(cancellation.MarkerPath).LinkTarget is not null ||
                (!OperatingSystem.IsWindows() && File.GetUnixFileMode(cancellation.MarkerPath) !=
                    (UnixFileMode.UserRead | UnixFileMode.UserWrite)) ||
                !string.Equals(File.ReadAllText(cancellation.MarkerPath, Encoding.UTF8), cancellation.Nonce, StringComparison.Ordinal))
                throw new InvalidDataException("The plan cancellation marker does not match its owner nonce.");
            return;
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(cancellation.MarkerPath, options);
        stream.Write(Encoding.UTF8.GetBytes(cancellation.Nonce));
        stream.Flush(flushToDisk: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(cancellation.MarkerPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string CanonicalPath(string path, int depth = 0)
    {
        if (depth > 64) throw new IOException("The temporary directory path contains too many symbolic links.");
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ?? throw new ArgumentException("The path must be absolute.", nameof(path));
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is null) continue;
            FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null && !string.Equals(Path.GetFullPath(target.FullName), current, StringComparison.Ordinal))
                current = CanonicalPath(target.FullName, depth + 1);
        }
        return current;
    }

    private static void RemovePlanCancellationFiles(PlanCancellationFiles cancellation)
    {
        RemoveDirectoryBestEffort(cancellation.Directory);
    }

    private static void RemoveDirectoryBestEffort(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsCanceledResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("state", out JsonElement state) ||
            state.ValueKind != JsonValueKind.String) return false;
        return state.GetString() is "canceled" or "cancelled";
    }

    private void CleanupExpiredLocked(DateTimeOffset now)
    {
        var terminal = new List<(OperationRecord Operation, DateTimeOffset CompletedAt)>();
        foreach (OperationRecord operation in operations.Values)
        {
            lock (operation.Gate)
                if (operation.IsTerminal) terminal.Add((operation, operation.TerminalAt));
        }
        terminal.Sort((left, right) => left.CompletedAt.CompareTo(right.CompletedAt));
        foreach ((OperationRecord operation, DateTimeOffset completedAt) in terminal.ToArray())
        {
            bool overLimit = terminal.Count > MaximumRetainedTerminalOperations;
            if (now - completedAt <= TerminalRetention && !overLimit) continue;
            if (!operations.TryRemove(operation.Id, out _)) continue;
            lock (operation.Gate)
            {
                if (!operation.MemoryReleased)
                {
                    Interlocked.Add(ref retainedEventBytes, -operation.EventBytes);
                    operation.MemoryReleased = true;
                }
            }
            terminal.RemoveAll(item => item.Operation.Id == operation.Id);
        }
    }

    private bool TryReserveEventBytes(long bytes)
    {
        while (true)
        {
            long current = Interlocked.Read(ref retainedEventBytes);
            if (bytes > MaximumRetainedEventBytes - current) return false;
            if (Interlocked.CompareExchange(ref retainedEventBytes, current + bytes, current) == current) return true;
        }
    }

    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            lock (registryGate) CleanupExpiredLocked(DateTimeOffset.UtcNow);
        }
    }

    private static void SetTerminalLocked(OperationRecord operation, string state, JsonElement? result, JsonElement? error)
    {
        operation.State = state;
        operation.Result = result;
        operation.Error = error;
        operation.TerminalAt = DateTimeOffset.UtcNow;
        operation.LastAccess = operation.TerminalAt;
        operation.ControlFrames.Writer.TryComplete();
    }

    private static void TryKill(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private sealed class OperationRecord(string id, string method, JsonElement parameters)
    {
        public object Gate { get; } = new();
        public string Id { get; } = id;
        public string Method { get; } = method;
        public JsonElement Parameters { get; } = parameters;
        public List<OperationEvent> Events { get; } = [];
        public Channel<DuplexControlFrame> ControlFrames { get; } = Channel.CreateUnbounded<DuplexControlFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        public string State { get; set; } = "queued";
        public Process? Process { get; set; }
        public Task? RunTask { get; set; }
        public bool CancelRequested { get; set; }
        public long ControlSequence { get; set; }
        public int ControlsQueued { get; set; }
        public long EventBytes { get; set; }
        public bool MemoryReleased { get; set; }
        public string? ControlFailure { get; set; }
        public string? ProtocolFailure { get; set; }
        public PlanCancellationFiles? PlanCancellation { get; set; }
        public TerminalResponse? Terminal { get; set; }
        public JsonElement? Result { get; set; }
        public JsonElement? Error { get; set; }
        public DateTimeOffset SubmittedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset TerminalAt { get; set; }
        public DateTimeOffset LastAccess { get; set; } = DateTimeOffset.UtcNow;
        public bool IsTerminal => State is "completed" or "failed" or "canceled";
    }

    private sealed record OperationEvent(long Sequence, JsonElement Progress);
    private sealed record TerminalResponse(JsonElement? Result, JsonElement? Error);
    private sealed record PlanCancellationFiles(string Directory, string Nonce, string OwnerPath, string MarkerPath);
    private sealed record DuplexControlFrame(string Id, long Sequence, JsonElement Control);
    private sealed class ProgressLimitExceededException(string message) : Exception(message) { }

    private sealed class BoundedProtocolLineReader(TextReader reader, int maximumLineCharacters)
    {
        private readonly char[] buffer = new char[8192];
        private int offset;
        private int count;

        public async Task<string?> ReadLineAsync()
        {
            var line = new StringBuilder();
            while (true)
            {
                if (offset >= count)
                {
                    count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                    offset = 0;
                    if (count == 0) return line.Length == 0 ? null : line.ToString();
                }

                int newline = Array.IndexOf(buffer, '\n', offset, count - offset);
                int end = newline >= 0 ? newline : count;
                int length = end - offset;
                if (line.Length + length > maximumLineCharacters)
                    throw new ProgressLimitExceededException("The worker emitted a response line larger than the server's protocol limit.");
                line.Append(buffer.AsSpan(offset, length));
                offset = newline >= 0 ? newline + 1 : count;
                if (newline < 0) continue;
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                return line.ToString();
            }
        }
    }
}

public sealed record OperationSnapshot(object? Payload, bool InvalidCursor);
public sealed record CancelRequestResult(bool Found, bool CancelRequested);
public sealed record ControlSendResult(ControlSendStatus Status, long Sequence);
public enum ControlSendStatus { Accepted, NotFound, Terminal, LimitReached }

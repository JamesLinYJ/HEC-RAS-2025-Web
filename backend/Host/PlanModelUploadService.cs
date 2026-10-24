using System.Security.Cryptography;
using Ras.ModelLibrary;
using Utility.Progress;

namespace Ras.Backend;

public sealed record PlanModelUploadRequest(PlanExportResult Receipt, string ProjectSessionID,
    string ModelID, string Owner, string S3AccessKey, string S3SecretKey,
    string ProjectPath, string PlanPath, string SourceRevision, string ReceiptID,
    CoreLayerRequestContext? CoreLayerContext = null);
public sealed record PlanModelUploadResult(string State, string ModelID, string OutputPath,
    string ReceiptID);

internal interface IPlanModelUploader : IDisposable
{
    Task PushAsync(string modelID, string zipPath, string owner, ProgressReporter reporter);
}

internal sealed class SourcePlanModelUploader(string accessKey, string secretKey) : IPlanModelUploader
{
    private readonly ModelLibraryClient client = new(accessKey, secretKey);

    public Task PushAsync(string modelID, string zipPath, string owner, ProgressReporter reporter) =>
        client.PushAsync(modelID, zipPath, owner, reporter, force: false);

    public void Dispose() => client.Dispose();
}

/// <summary>Uploads only the exact ZIP returned by the immediately preceding plan export.</summary>
public static class PlanModelUploadService
{
    internal static async Task<PlanModelUploadResult> ExecuteDuplexAsync(
        PlanModelUploadRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
        => await ExecuteDuplexAsync(request, report, duplexControls,
            static (accessKey, secretKey) => new SourcePlanModelUploader(accessKey, secretKey)).ConfigureAwait(false);

    // Tests may replace only the final network boundary. All request, identity,
    // committed-receipt and content-hash checks still run before this factory
    // is called, and the production route above always uses force:false.
    internal static async Task<PlanModelUploadResult> ExecuteDuplexAsync(
        PlanModelUploadRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls,
        Func<string, string, IPlanModelUploader> createUploader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(duplexControls);
        ArgumentNullException.ThrowIfNull(createUploader);
        ValidateRequest(request);

        var reporter = new ProgressReporter();
        reporter.ProgressReported += (sender, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        using var cancellation = new CancellationTokenSource();
        using var stopControls = new CancellationTokenSource();
        using var cancelRegistration = cancellation.Token.Register(reporter.RequestCancel);
        ProgressReporter? uploadReporter = null;
        Exception? controlError = null;
        object controlErrorGate = new();
        Task controlPump = Task.Run(async () =>
        {
            try
            {
                await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(stopControls.Token)
                                   .ConfigureAwait(false))
                {
                    if (!frame.Control.TryGetProperty("type", out System.Text.Json.JsonElement type) ||
                        type.ValueKind != System.Text.Json.JsonValueKind.String ||
                        !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal))
                    {
                        lock (controlErrorGate)
                            controlError = new InvalidDataException("The model upload received an unsupported control type.");
                        cancellation.Cancel();
                        return;
                    }
                    cancellation.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            catch (Exception error)
            {
                lock (controlErrorGate) controlError = error;
                cancellation.Cancel();
            }
        }, CancellationToken.None);

        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            reporter.ReportMessage("Verifying the committed plan ZIP receipt...");
            using FileStream sourceLease = new(request.Receipt.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            (string sha256, long length) = HashStream(sourceLease, reporter);
            if (length != request.Receipt.OutputBytes ||
                !string.Equals(sha256, request.Receipt.OutputSHA256, StringComparison.Ordinal))
                throw new InvalidDataException("The exported ZIP changed after it was committed. Export the plan again before uploading.");
            sourceLease.Position = 0;

            cancellation.Token.ThrowIfCancellationRequested();
            reporter.ReportMessage("Uploading plan ZIP as model ID '{modelId}'...", request.ModelID);
            using IPlanModelUploader uploader = createUploader(request.S3AccessKey, request.S3SecretKey);
            uploadReporter = reporter.WithProgressModifier(50, 0.5f);
            await uploader.PushAsync(request.ModelID, request.Receipt.OutputPath, request.Owner,
                uploadReporter).ConfigureAwait(false);

            Exception? invalidControl;
            lock (controlErrorGate) invalidControl = controlError;
            if (invalidControl is not null)
                throw new InvalidDataException("The model upload received an invalid cancellation control.", invalidControl);
            return new("completed", request.ModelID, request.Receipt.OutputPath, request.Receipt.ReceiptID);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || reporter.CancelRequested ||
                                                 uploadReporter?.CancelRequested == true)
        {
            Exception? invalidControl;
            lock (controlErrorGate) invalidControl = controlError;
            if (invalidControl is not null)
                throw new InvalidDataException("The model upload received an invalid cancellation control.", invalidControl);
            return new("cancelled", request.ModelID, request.Receipt.OutputPath, request.Receipt.ReceiptID);
        }
        catch (InvalidDataException error)
        {
            // Preserve the stable data-integrity/protocol failure type while
            // redacting credentials and dropping any SDK exception chain.
            throw new InvalidDataException(Redact(error.Message, request.S3AccessKey, request.S3SecretKey));
        }
        catch (Exception error)
        {
            throw Sanitize(error, request.S3AccessKey, request.S3SecretKey);
        }
        finally
        {
            stopControls.Cancel();
            try { await controlPump.ConfigureAwait(false); }
            catch { }
        }
    }

    private static void ValidateRequest(PlanModelUploadRequest request)
    {
        PlanExportResult receipt = request.Receipt ?? throw new ArgumentException("The committed plan ZIP receipt is missing.");
        if (!receipt.Zip || string.IsNullOrWhiteSpace(receipt.OutputSHA256) || receipt.OutputBytes <= 0)
            throw new ArgumentException("Only a committed ZIP plan export can be uploaded.");
        if (string.IsNullOrWhiteSpace(request.ProjectSessionID) ||
            !Guid.TryParse(request.ProjectSessionID, out _ ) ||
            !string.Equals(receipt.ProjectSessionID, request.ProjectSessionID, StringComparison.Ordinal))
            throw new ArgumentException("The export receipt belongs to another project session.");
        if (string.IsNullOrWhiteSpace(receipt.ProjectPath) || string.IsNullOrWhiteSpace(receipt.PlanPath) ||
            string.IsNullOrWhiteSpace(receipt.SourceRevision) || string.IsNullOrWhiteSpace(receipt.ReceiptID))
            throw new ArgumentException("The plan export receipt is incomplete.");
        if (!string.Equals(Path.GetExtension(receipt.OutputPath), ".zip", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(receipt.OutputPath) ||
            !Path.IsPathFullyQualified(receipt.ProjectPath) || !Path.IsPathFullyQualified(receipt.PlanPath))
            throw new ArgumentException("The plan export receipt contains an invalid path.");
        string expectedReceiptID = PlanExportService.CreateReceiptID(receipt.OutputPath,
            receipt.ProjectPath, receipt.PlanPath, receipt.ProjectSessionID, receipt.SourceRevision,
            receipt.CoreLayerIdentityToken, receipt.OutputSHA256, receipt.OutputBytes);
        if (!string.Equals(receipt.ReceiptID, expectedReceiptID, StringComparison.Ordinal))
            throw new InvalidDataException("The committed plan ZIP receipt is invalid.");
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || string.IsNullOrWhiteSpace(request.PlanPath) ||
            string.IsNullOrWhiteSpace(request.SourceRevision) || string.IsNullOrWhiteSpace(request.ReceiptID) ||
            !Path.IsPathFullyQualified(request.ProjectPath) || !Path.IsPathFullyQualified(request.PlanPath) ||
            !PathsEqual(request.ProjectPath, receipt.ProjectPath) ||
            !PathsEqual(request.PlanPath, receipt.PlanPath) ||
            !string.Equals(request.SourceRevision, receipt.SourceRevision, StringComparison.Ordinal) ||
            !string.Equals(request.ReceiptID, receipt.ReceiptID, StringComparison.Ordinal))
            throw new ArgumentException("The committed plan ZIP receipt does not match the captured project and Plan.");

        CoreLayerRequestContext? context = request.CoreLayerContext;
        if (context is null)
        {
            if (receipt.CoreLayerIdentityToken is not null)
                throw new ArgumentException("The captured external core-layer context is missing.");
        }
        else if (!string.Equals(context.ProjectSessionID, request.ProjectSessionID, StringComparison.Ordinal) ||
                 !PathsEqual(context.ProjectPath, receipt.ProjectPath) ||
                 !string.Equals(context.IdentityToken, receipt.CoreLayerIdentityToken, StringComparison.Ordinal))
        {
            throw new ArgumentException("The captured external core-layer context does not match the exported plan.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelID))
            throw new ArgumentException("Enter a model ID before uploading the plan ZIP.");
        if (string.IsNullOrWhiteSpace(request.Owner))
            throw new ArgumentException("The application owner identity is unavailable.");
        if (string.IsNullOrWhiteSpace(request.S3AccessKey) || string.IsNullOrWhiteSpace(request.S3SecretKey))
            throw new InvalidOperationException("Configure developer S3 credentials before uploading a plan ZIP.");
        if (!File.Exists(receipt.OutputPath))
            throw new FileNotFoundException("The committed plan ZIP no longer exists.", receipt.OutputPath);
    }

    private static (string SHA256, long Length) HashStream(FileStream input, ProgressReporter reporter)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long length = 0;
        while (true)
        {
            if (reporter.CancelRequested) throw new OperationCanceledException();
            int read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            length += read;
        }
        return (Convert.ToHexString(hash.GetHashAndReset()), length);
    }

    private static Exception Sanitize(Exception error, string accessKey, string secretKey)
    {
        // Do not retain the original exception as InnerException: SDK exceptions
        // can include request details, and the host writes full exception chains
        // to its diagnostic log.
        return new InvalidOperationException(Redact(error.Message, accessKey, secretKey));
    }

    private static string Redact(string message, string accessKey, string secretKey)
    {
        if (!string.IsNullOrEmpty(accessKey)) message = message.Replace(accessKey, "[redacted]", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(secretKey)) message = message.Replace(secretKey, "[redacted]", StringComparison.Ordinal);
        return message;
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

using BoundaryCondition = Ras.Layers.BoundaryCondition;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Rasters;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Layers.Results;
using Ras.Engine;
using Ras.Observables;
using Utility.Files;
using Utility.Logging;
using Utility.Progress;
using Visual.Observables;

namespace Ras.Backend;

public sealed record PlanComputeDependency(string Role, string Path, string Revision, bool WillWrite);
public sealed record PlanComputeInspectionRequest(ModelSaveRequest Model);
public sealed record PlanComputeCancellation(string Directory, string Nonce);
public sealed record PlanComputeExecuteRequest(ModelSaveRequest Model, string InspectionId,
    PlanComputeCancellation Cancellation);
public sealed record PlanComputeSummary(bool Success, string? FailureReason, string? Solver,
    string? SoftwareVersion, DateTime? StartTime, DateTime? EndTime,
    double? TotalDurationSeconds, double? SolverInitDurationSeconds, double? OutputDurationSeconds,
    int? GlobalRetryCount, string? ComputeLog);
public sealed record PlanComputeInspection(string InspectionId, string Revision,
    string ProjectRevision, string ProjectXmlFingerprint, bool CanExecute, bool RequiresCUDA,
    string[] Reasons, PlanPrecomputeMessage[] Messages, PlanComputeDependency[] SourceDependencies,
    string[] SideEffects);
public sealed record PlanComputeExecutionResult(string InspectionId, string Revision, string State,
    string? ResultPath, string? ResultRevision, PlanComputeSummary? Summary,
    PlanPrecomputeMessage[] Messages, string? ErrorMessage,
    PlanComputeDependency[] FinalDependencies, IReadOnlyDictionary<string, string> SourceValues,
    ResultComputeEnvelope? ResultCompute = null);

/// <summary>
/// Inspects and executes the retained source Plan.ComputeAsync workflow. The
/// inspection is an identity token for the exact in-memory model/project draft
/// and the file dependencies that the source solver will read or write.
/// </summary>
public static partial class ModelEditorAdapter
{
    private const string MissingPlanComputeDependency = "missing";
    private const string PlanComputeOwnerFile = "owner";
    private const string PlanComputeCancelFile = "cancel";
    private const string PlanComputeDirectoryPrefix = "hecras-plan-compute-";

    public static Task<PlanComputeInspection> InspectPlanComputeAsync(PlanComputeInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing plan compute model request.");
        ValidatePlanComputeModel(model);
        string projectPath = SourceFilePaths.Canonical(model.ProjectPath!);
        string assetPath = SourceFilePaths.Canonical(model.AssetPath);
        string revision = Revision(assetPath);
        if (!string.Equals(revision, model.Revision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        VerifyOwningProject(projectPath, assetPath, model.CoreLayerContext);
        string projectRevision = Revision(projectPath);
        string projectXmlFingerprint = HashText(model.ProjectXml!);

        PlanComputeInspection inspection;
        using (var context = new ModelContext(model))
        {
            if (context.Layer is not Plan plan || context.Observable is not ObservablePlan observable)
                throw new ArgumentException("The selected model is not a source plan.");
            if (plan.IsResult) throw new ValidationException("Result plans are read-only.");

            Dictionary<string, string> ordinaryFields = ApplyPlanTimeWindowDraft(context, model);
            ApplyFields(context, null, ordinaryFields);

            PlanComputeDependency[] before = CapturePlanComputeDependencies(context.Project, plan,
                projectPath, includeResultOutput: true);
            observable.UpdateLatestPreComputeResult();
            OperationResult operation = observable.LatestPreComputeResult
                ?? throw new InvalidDataException("The source plan returned no precompute result.");
            PlanComputeDependency[] after = CapturePlanComputeDependencies(context.Project, plan,
                projectPath, includeResultOutput: true);
            EnsureDependenciesUnchanged(before, after);

            PlanPrecomputeMessage[] messages = ConvertMessages(operation, context.Project, plan,
                projectPath, projectRevision, assetPath, revision);
            bool requiresCuda = RequiresCuda(plan);
            var reasons = new List<string>();
            if (!operation.Result) reasons.Add("The source plan check did not pass.");
            if (requiresCuda) reasons.Add(OperatingSystem.IsMacOS()
                ? "CUDA compute is unavailable on macOS. Select the CPU solver to run this plan."
                : "This compute operation supports the CPU solver only.");

            string fingerprint = PlanComputeFingerprint(model, projectPath, projectRevision,
                projectXmlFingerprint, before);
            inspection = new PlanComputeInspection(fingerprint, revision, projectRevision,
                projectXmlFingerprint, operation.Result && !requiresCuda, requiresCuda,
                reasons.ToArray(), messages, before, PlanComputeSideEffects);
        }

        if (!string.Equals(Revision(assetPath), revision, StringComparison.Ordinal) ||
            !string.Equals(Revision(projectPath), projectRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return Task.FromResult(inspection);
    }

    public static Task<PlanComputeExecutionResult> ExecutePlanComputeAsync(
        PlanComputeExecuteRequest request, Action<BackendOperationProgress>? report = null) =>
        ExecutePlanComputeCoreAsync(request, report, duplexControls: null);

    internal static Task<PlanComputeExecutionResult> ExecutePlanComputeDuplexAsync(
        PlanComputeExecuteRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls) =>
        ExecutePlanComputeCoreAsync(request, report, duplexControls);

    private static async Task<PlanComputeExecutionResult> ExecutePlanComputeCoreAsync(
        PlanComputeExecuteRequest request, Action<BackendOperationProgress>? report,
        BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ModelSaveRequest model = request.Model ?? throw new ArgumentException("Missing plan compute model request.");
        if (string.IsNullOrWhiteSpace(request.InspectionId))
            throw new ArgumentException("Inspect this plan before starting compute.");
        ValidatePlanComputeModel(model);
        CancellationControl control = ValidateCancellationControl(request.Cancellation);

        string projectPath = SourceFilePaths.Canonical(model.ProjectPath!);
        string assetPath = SourceFilePaths.Canonical(model.AssetPath);
        string revision = Revision(assetPath);
        if (!string.Equals(revision, model.Revision, StringComparison.Ordinal)) throw new SourceFileChangedException();
        VerifyOwningProject(projectPath, assetPath, model.CoreLayerContext);
        string projectRevision = Revision(projectPath);
        string projectXmlFingerprint = HashText(model.ProjectXml!);

        using var context = new ModelContext(model);
        if (context.Layer is not Plan plan || context.Observable is not ObservablePlan observable)
            throw new ArgumentException("The selected model is not a source plan.");
        if (plan.IsResult) throw new ValidationException("Result plans are read-only.");

        Dictionary<string, string> ordinaryFields = ApplyPlanTimeWindowDraft(context, model);
        ApplyFields(context, null, ordinaryFields);
        PlanComputeDependency[] dependencies = CapturePlanComputeDependencies(context.Project, plan,
            projectPath, includeResultOutput: true);
        observable.UpdateLatestPreComputeResult();
        OperationResult readiness = observable.LatestPreComputeResult
            ?? throw new InvalidDataException("The source plan returned no precompute result.");
        PlanComputeDependency[] verifiedDependencies = CapturePlanComputeDependencies(context.Project, plan,
            projectPath, includeResultOutput: true);
        EnsureDependenciesUnchanged(dependencies, verifiedDependencies);
        string currentInspectionId = PlanComputeFingerprint(model, projectPath, projectRevision,
            projectXmlFingerprint, dependencies);
        if (!string.Equals(currentInspectionId, request.InspectionId, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (!string.Equals(Revision(assetPath), revision, StringComparison.Ordinal) ||
            !string.Equals(Revision(projectPath), projectRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        var computeIdentity = new ResultComputeTracker(Guid.NewGuid().ToString("D"), projectPath,
            projectRevision, projectXmlFingerprint, assetPath, revision);
        void ReportCorrelated(BackendOperationProgress update) =>
            report?.Invoke(update with { ResultCompute = computeIdentity.Snapshot() });

        PlanPrecomputeMessage[] readinessMessages = ConvertMessages(readiness, context.Project, plan,
            projectPath, projectRevision, assetPath, revision);
        if (RequiresCuda(plan))
        {
            string reason = OperatingSystem.IsMacOS()
                ? "CUDA compute is unavailable on macOS. Select the CPU solver to run this plan."
                : "This compute operation supports the CPU solver only.";
            ResultComputeEnvelope terminalIdentity = computeIdentity.Finish("failed", null, reason);
            ReportCorrelated(new(ResultCompute: terminalIdentity));
            return new(currentInspectionId, revision, "failed", null, null, null,
                readinessMessages, reason, dependencies, new Dictionary<string, string>(), terminalIdentity);
        }
        if (!readiness.Result)
        {
            const string reason = "The source plan check did not pass.";
            ResultComputeEnvelope terminalIdentity = computeIdentity.Finish("failed", null, reason);
            ReportCorrelated(new(ResultCompute: terminalIdentity));
            return new(currentInspectionId, revision, "failed", null, null, null,
                readinessMessages, reason, dependencies, new Dictionary<string, string>(), terminalIdentity);
        }

        var reporter = new ProgressReporter();
        var job = PlanComputeJobMessageCapture.CreateJob(reporter, "Plan Compute");
        string canonicalPlanPath = SourceFilePaths.Canonical(plan.Filename);
        var jobMessages = new PlanComputeJobMessageCapture(job, computeIdentity.Snapshot().RunId,
            (index, message) => ProjectPlanComputeJobMessage(index, message, context.Project, plan,
                projectPath, projectRevision, canonicalPlanPath, revision),
            update => ReportCorrelated(new(PlanMessages: update)));
        ObservableResult? createdResult = null;
        Result.TimeStepCompletedEvent? timestepHandler = null;
        PlanComputeProfilePublisher? liveProfiles = null;
        PlanComputeLiveMapRenderer? liveMapRenderer = null;
        object liveMapRendererGate = new();
        var liveMapTargets = new PlanComputeLiveMapTargetRegistry();
        var telemetry = new PlanComputeTelemetry(plan, ReportCorrelated);
        OperationResult? operation = null;
        Exception? sourceError = null;
        int cancelAcknowledged = 0;
        using var monitorStop = new CancellationTokenSource();

        reporter.ProgressReported += (sender, percent) => ReportCorrelated(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: sender.FullyQualifiedTaskName));
        reporter.MessageReported += (sender, message) => ReportCorrelated(new(
            Message: message.EvaluatedMessage, TaskName: sender.FullyQualifiedTaskName));

        Task? duplexControlPump = duplexControls is null ? null : Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame message in duplexControls.ReadAllAsync(monitorStop.Token)
                               .ConfigureAwait(false))
            {
                JsonElement control = message.Control;
                if (!control.TryGetProperty("type", out JsonElement typeValue) ||
                    typeValue.GetString() is not { } type) continue;
                if (type == "cancel")
                {
                    if (Interlocked.Exchange(ref cancelAcknowledged, 1) == 0) job.RequestCancel();
                    continue;
                }
                if (type != "targets") continue;
                if (!control.TryGetProperty("targets", out JsonElement targetsValue) ||
                    targetsValue.ValueKind != JsonValueKind.Array)
                {
                    ReportTargetUpdate(liveMapTargets.Reject(message.ControlSequence));
                    continue;
                }
                PlanComputeLiveMapRenderTarget[]? next;
                try
                {
                    next = targetsValue.Deserialize<PlanComputeLiveMapRenderTarget[]>(BackendJson.Options);
                }
                catch (JsonException)
                {
                    ReportTargetUpdate(liveMapTargets.Reject(message.ControlSequence));
                    continue;
                }
                ReportTargetUpdate(liveMapTargets.Replace(message.ControlSequence, next));
            }
        }, monitorStop.Token);

        void ReportTargetUpdate(PlanComputeLiveMapTargetUpdateResult result)
        {
            if (result.Status == PlanComputeLiveMapTargetUpdateStatus.RejectedAndCleared)
                ReportCorrelated(new(Message: result.Message,
                    TaskName: "Live map rendering"));
            if (result.Status != PlanComputeLiveMapTargetUpdateStatus.Stale)
            {
                lock (liveMapRendererGate) liveMapRenderer?.TargetsChanged();
            }
        }

        if (HasValidCancellationMarker(control))
        {
            job.RequestCancel();
            Interlocked.Exchange(ref cancelAcknowledged, 1);
        }

        ReportCorrelated(new());
        Task<OperationResult> sourceCompute = observable.ComputeAsync(job, result =>
        {
            createdResult = result;
            if (!string.IsNullOrWhiteSpace(result.Result.Filename))
                computeIdentity.SetStartedResult(result.Result.Filename, result.Name);
            ReportCorrelated(new());
            if (result.Result.BaseOutputBlock is not null && duplexControls is not null &&
                !string.IsNullOrWhiteSpace(result.Result.Filename))
            {
                lock (liveMapRendererGate)
                    liveMapRenderer = new PlanComputeLiveMapRenderer(context.Project, result.Result,
                        result.Result.Filename ?? string.Empty, computeIdentity.Snapshot, liveMapTargets, ReportCorrelated);
            }
            // Subscribe the renderer first so its synchronous TimestepAdded
            // callback snapshots the immutable profile identity before the
            // publisher exposes that timestep to a host that may immediately
            // change its viewport.
            liveProfiles = new PlanComputeProfilePublisher(result.Result.BaseOutputBlock, ReportCorrelated);
            telemetry.Start();
            timestepHandler = telemetry.OnTimeStepCompleted;
            result.Result.TimeStepCompleted += timestepHandler;
        });
        Task cancellationMonitor = MonitorCancellationAsync(control, job,
            () => Volatile.Read(ref cancelAcknowledged) != 0,
            value => { if (value) Interlocked.Exchange(ref cancelAcknowledged, 1); }, monitorStop.Token);
        try
        {
            operation = await sourceCompute.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            sourceError = error;
        }
        finally
        {
            monitorStop.Cancel();
            try { await cancellationMonitor.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            liveMapTargets.Clear();
            PlanComputeLiveMapRenderer? renderer;
            lock (liveMapRendererGate)
            {
                renderer = liveMapRenderer;
                liveMapRenderer = null;
            }
            renderer?.Dispose();
            liveProfiles?.Dispose();
            if (duplexControlPump is not null)
            {
                try { await duplexControlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            if (createdResult is not null && timestepHandler is not null)
                createdResult.Result.TimeStepCompleted -= timestepHandler;
            try { job.MarkComplete(operation?.Result == true && createdResult?.Result.ComputeSummary?.Success == true); }
            catch (Exception completionError) { sourceError ??= completionError; }
            try { jobMessages.Flush(); }
            catch (Exception messageError) { sourceError ??= messageError; }
            finally { jobMessages.Dispose(); }
        }

        PlanComputeSummary? summary = createdResult?.Result.ComputeSummary is { } computeSummary
            ? new PlanComputeSummary(computeSummary.Success, NullIfEmpty(computeSummary.FailureReason),
                NullIfEmpty(computeSummary.Solver), NullIfEmpty(computeSummary.SoftwareVersion),
                computeSummary.StartTime, computeSummary.EndTime, computeSummary.TotalDuration.TotalSeconds,
                computeSummary.SolverInitDuration.TotalSeconds, computeSummary.OutputDuration.TotalSeconds,
                computeSummary.GlobalRetryCount, NullIfEmpty(computeSummary.ComputeLog))
            : null;
        string? resultPath = createdResult?.Result.Filename is { Length: > 0 } path
            ? SourceFilePaths.Canonical(path) : null;
        string? resultRevision = resultPath is not null && File.Exists(resultPath) ? Revision(resultPath) : null;

        PlanComputeDependency[] finalDependencies = FinalPlanComputeDependencies(dependencies,
            resultPath, resultRevision);
        string? errorMessage = sourceError?.Message;
        bool inputsStayedStable = true;
        try
        {
            if (!string.Equals(Revision(assetPath), revision, StringComparison.Ordinal) ||
                !string.Equals(Revision(projectPath), projectRevision, StringComparison.Ordinal))
                inputsStayedStable = false;
            PlanComputeDependency[] now = CapturePlanComputeDependencies(context.Project, plan,
                projectPath, includeResultOutput: false);
            var currentByIdentity = now.ToDictionary(item => (item.Role, item.Path));
            foreach (PlanComputeDependency before in dependencies.Where(item => !item.WillWrite))
            {
                if (!currentByIdentity.TryGetValue((before.Role, before.Path), out PlanComputeDependency? current) ||
                    !string.Equals(before.Revision, current.Revision, StringComparison.Ordinal))
                {
                    inputsStayedStable = false;
                    break;
                }
            }
        }
        catch { inputsStayedStable = false; }
        if (!inputsStayedStable)
            errorMessage = "A read-only compute input changed while the source solver was running. The source output is retained for inspection.";

        bool sourceCancellationMessage = operation?.Messages.Any(message =>
            message.MessageType?.Code == Ras.Engine.EngineMessageCodes.UserCancelled.Code
            || message.MessageType?.Code == GeneralMessageCodes.UserOperationCancelled.Code) == true;
        bool completed = sourceError is null && inputsStayedStable && operation?.Result == true && summary?.Success == true
            && resultPath is not null && resultRevision is not null;
        bool sourceCancelled = !completed && (sourceCancellationMessage ||
            Volatile.Read(ref cancelAcknowledged) != 0 && operation?.Result == false);
        string state = completed ? "completed" : sourceCancelled ? "cancelled" : "failed";
        PlanPrecomputeMessage[] messages = operation is null ? readinessMessages : ConvertMessages(operation,
            context.Project, plan, projectPath, projectRevision, canonicalPlanPath, revision);
        if (resultPath is not null && resultRevision is null && errorMessage is null)
            errorMessage = "The source created a result path but its final HDF revision could not be read.";

        ResultComputeEnvelope currentIdentity = computeIdentity.Snapshot();
        string? correlatedResultRevision = resultPath is not null &&
            string.Equals(currentIdentity.ResultPath, resultPath, StringComparison.Ordinal)
                ? resultRevision : null;
        if (resultPath is not null && correlatedResultRevision is null && errorMessage is null)
            errorMessage = "The source result path did not match the path reported by computeStarted.";
        string? failureReason = errorMessage;
        if (failureReason is null && state != "completed")
            failureReason = createdResult is null
                ? NullIfEmpty(summary?.FailureReason)
                : NullIfEmpty(createdResult.FriendlyFailureReason);
        ResultComputeEnvelope terminalComputeIdentity = computeIdentity.Finish(state,
            correlatedResultRevision, failureReason);
        ReportCorrelated(new(ResultCompute: terminalComputeIdentity));

        return new(currentInspectionId, revision, state, resultPath, resultRevision, summary,
            messages, errorMessage, finalDependencies, telemetry.Values, terminalComputeIdentity);
    }

    private sealed class ResultComputeTracker(string runId, string projectPath, string projectRevision,
        string projectXmlFingerprint, string planPath, string planRevision)
    {
        private readonly object gate = new();
        private ResultComputeEnvelope value = new(runId, projectPath, projectRevision,
            projectXmlFingerprint, planPath, planRevision, null, "running");

        public ResultComputeEnvelope Snapshot()
        {
            lock (gate) return value;
        }

        public void SetStartedResult(string filename, string? resultName)
        {
            string actualPath = SourceFilePaths.Canonical(filename);
            lock (gate) value = value with
            {
                ResultPath = actualPath,
                ResultName = string.IsNullOrWhiteSpace(resultName) ? null : resultName
            };
        }

        public ResultComputeEnvelope Finish(string state, string? resultRevision, string? failureReason)
        {
            lock (gate)
            {
                value = value with
                {
                    State = state,
                    ResultRevision = value.ResultPath is null ? null : resultRevision,
                    FailureReason = failureReason
                };
                return value;
            }
        }
    }

    private sealed class PlanComputeTelemetry(Plan plan, Action<BackendOperationProgress>? report)
    {
        private readonly Stopwatch elapsed = new();
        private readonly object gate = new();
        private IReadOnlyDictionary<string, string> values = new Dictionary<string, string>();
        private long lastReportTicks;

        public IReadOnlyDictionary<string, string> Values
        {
            get { lock (gate) return new Dictionary<string, string>(values); }
        }

        public void Start() => elapsed.Restart();

        public void OnTimeStepCompleted(Result sender, MeshTimestepStats stats)
        {
            int totalCells = plan.Geometry?.Mesh?.CellCount ?? 0;
            int dryCells = totalCells > 0 ? Math.Max(0, totalCells - stats.WetCellCount) : 0;
            double duration = plan.ComputeDurationSeconds;
            double fraction = duration > 0 && double.IsFinite(stats.Time)
                ? Math.Clamp(stats.Time / duration, 0, 1) : 0;
            double elapsedSeconds = elapsed.Elapsed.TotalSeconds;
            double remaining = fraction > 0 && elapsedSeconds > 0
                ? Math.Max(0, elapsedSeconds * (1 - fraction) / fraction) : double.NaN;
            double speed = elapsedSeconds > 0 && stats.Time >= 0 ? stats.Time / elapsedSeconds : double.NaN;
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Steps"] = stats.Step.ToString(CultureInfo.InvariantCulture),
                ["LastIterations"] = stats.Iterations.ToString(CultureInfo.InvariantCulture),
                ["LastGlobalTimestep"] = Format(stats.TimeStep),
                ["LastMaxGlobalTimestep"] = Format(stats.MaxTimeStep),
                ["LastLimitingCell"] = stats.LimitingCell.ToString(CultureInfo.InvariantCulture),
                ["LastLimitingFace"] = stats.LimitingFace.ToString(CultureInfo.InvariantCulture),
                ["LastActiveCells"] = Format(stats.ActiveCells),
                ["WetCellCount"] = stats.WetCellCount.ToString(CultureInfo.InvariantCulture),
                ["Retries"] = stats.Retries.ToString(CultureInfo.InvariantCulture),
                ["RetryElevationCount"] = stats.RetryElevationCount.ToString(CultureInfo.InvariantCulture),
                ["RetryNaNCount"] = stats.RetryNanCount.ToString(CultureInfo.InvariantCulture),
                ["RetryNegVolumeCount"] = stats.RetryNegVolumeCount.ToString(CultureInfo.InvariantCulture),
                ["AdvectionLimitCount"] = stats.AdvectionLimitCount.ToString(CultureInfo.InvariantCulture),
                ["UserLimitCount"] = stats.UserLimitCount.ToString(CultureInfo.InvariantCulture),
                ["CelerityLimitCount"] = stats.CelerityLimitCount.ToString(CultureInfo.InvariantCulture),
                ["IncreaseLimitCount"] = stats.IncreaseLimitCount.ToString(CultureInfo.InvariantCulture),
                ["ElapsedTime"] = Format(elapsedSeconds),
                ["ComputationalSpeed"] = Format(speed)
            };
            if (totalCells > 0)
            {
                snapshot["TotalCellCount"] = totalCells.ToString(CultureInfo.InvariantCulture);
                snapshot["DryCellCount"] = dryCells.ToString(CultureInfo.InvariantCulture);
                snapshot["PercentWet"] = Format(100d * stats.WetCellCount / totalCells);
            }
            if (DateTime.MinValue < plan.StartTime && double.IsFinite(stats.Time))
                snapshot["SimulationTime"] = plan.StartTime.AddSeconds(stats.Time).ToString("O", CultureInfo.InvariantCulture);
            if (double.IsFinite(remaining)) snapshot["EstimatedTimeRemaining"] = Format(remaining);

            lock (gate) values = snapshot;
            long now = elapsed.ElapsedTicks;
            long reportInterval = Stopwatch.Frequency / 4;
            if (stats.Step == 0 || now - Interlocked.Read(ref lastReportTicks) >= reportInterval || fraction >= 1)
            {
                Interlocked.Exchange(ref lastReportTicks, now);
                report?.Invoke(new(Fraction: fraction, TaskName: "Plan Compute", Values: snapshot));
            }
        }

        private static string Format(double value) => double.IsFinite(value)
            ? value.ToString("0.###", CultureInfo.InvariantCulture) : "";
    }

    private static readonly string[] PlanComputeSideEffects =
    [
        "The source compute saves the associated Geometry HDF before solver preparation.",
        "The source compute saves the associated Boundary Conditions HDF before solver preparation.",
        "The source compute creates a new Result HDF before solving; failed and cancelled result files are retained.",
        "The current Plan draft is applied in memory. The Project file and Plan HDF are not saved."
    ];

    private static void ValidatePlanComputeModel(ModelSaveRequest model)
    {
        if (model.Kind != "Plan") throw new NotSupportedException("Plan compute is only available for source plans.");
        if (string.IsNullOrWhiteSpace(model.ProjectPath) || !Path.IsPathFullyQualified(model.ProjectPath))
            throw new ArgumentException("Plan compute requires its owning project.");
        string projectPath = Path.GetFullPath(model.ProjectPath);
        if (!projectPath.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(projectPath))
            throw new ArgumentException("Choose the saved owning project.");
        if (string.IsNullOrWhiteSpace(model.AssetPath) || !Path.IsPathFullyQualified(model.AssetPath) || !File.Exists(model.AssetPath))
            throw new ArgumentException("Choose an existing plan in the owning project.");
        if (model.ProjectXml is null) throw new ArgumentException("Plan compute requires the current project draft.");
        if (string.IsNullOrWhiteSpace(model.Revision)) throw new ArgumentException("Reload the Plan before computing.");
        ArgumentNullException.ThrowIfNull(model.Fields);
        if (HasRows(model.Rows) || HasSamples(model.Samples))
            throw new NotSupportedException("Plan compute does not apply table or time-series edits.");
        if (model.GeometryTopologyState is not null || model.GeometryMeshState is not null || model.GeometryCulvertState is not null)
            throw new NotSupportedException("Plan compute does not accept geometry child drafts.");
    }

    private static bool RequiresCuda(Plan plan)
    {
        if (plan.UseGPU) return true;
        if (string.IsNullOrWhiteSpace(plan.DevOptions)) return false;
        return plan.DevOptions.Split('\n').Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("//", StringComparison.Ordinal))
            .Any(line =>
            {
                int separator = line.IndexOf(' ');
                string key = separator < 0 ? line : line[..separator];
                return string.Equals(key, "UseGPU", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static PlanComputeDependency[] CapturePlanComputeDependencies(Project project, Plan plan,
        string projectPath, bool includeResultOutput)
    {
        var dependencies = new Dictionary<(string Role, string Path), PlanComputeDependency>();
        void Add(string role, string? path, bool willWrite = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string canonical = SourceFilePaths.Canonical(path);
            string revision = File.Exists(canonical) ? "sha256:" + Revision(canonical) : MissingPlanComputeDependency;
            dependencies[(role, canonical)] = new(role, canonical, revision, willWrite);
        }
        void AddExpected(string role, string? path, bool willWrite = false) => Add(role, path, willWrite);

        Add("Project", projectPath);
        Add("Plan", plan.Filename);
        if (plan.IsResult) throw new ValidationException("Result plans are read-only.");
        PlanAssociation association = project.GetPlanAssociation(plan);
        Geometry? geometry = association.Geometry;
        if (!string.IsNullOrWhiteSpace(association.GeometryName))
            AddExpected("Geometry", geometry?.Filename ?? Geometry.MakeFilename(
                project.ProjectDirectory, association.GeometryName), willWrite: geometry is not null);
        if (geometry is not null)
        {
            GeometryAssociation geometryAssociation = project.GetGeometryAssociation(geometry);
            Terrain? terrain = geometryAssociation.Terrain;
            NValueLayer? nValues = geometryAssociation.NValueLayer;
            if (!string.IsNullOrWhiteSpace(geometryAssociation.TerrainName))
                AddExpected("Terrain", terrain?.Filename ?? Terrain.MakeFilename(
                    project.ProjectDirectory, geometryAssociation.TerrainName));
            if (terrain is not null)
                foreach (var input in terrain.AllRasterInputs()) Add("TerrainRaster", input.Filename);
            if (!string.IsNullOrWhiteSpace(geometryAssociation.NValueLayerName))
                AddExpected("NValues", nValues?.Filename ?? NValueLayer.MakeFilename(
                    project.ProjectDirectory, geometryAssociation.NValueLayerName));
            if (nValues?.AssociatedClassificationLayer is { } classification)
            {
                Add("NValueClassification", classification.Filename);
                foreach (var input in classification.AllRasterInputs())
                    Add("NValueClassification", input.Filename);
            }
        }
        if (!string.IsNullOrWhiteSpace(association.BoundaryConditionName))
        {
            BoundaryCondition? boundary = association.BoundaryCondition;
            AddExpected("BoundaryCondition", boundary?.Filename ?? BoundaryCondition.MakeFilename(
                project.ProjectDirectory, association.BoundaryConditionName), willWrite: boundary is not null);
            if (boundary is not null) CaptureBoundaryExternalInputs(project, boundary, Add);
        }
        if (includeResultOutput)
        {
            string output = SourceFilePaths.Canonical(plan.GetAvailableResultFilename());
            Add("ResultOutput", output, willWrite: true);
        }
        return dependencies.Values.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    private static void CaptureBoundaryExternalInputs(Project project, BoundaryCondition boundary,
        Action<string, string?, bool> add)
    {
        void AddConnection(string? name, string role)
        {
            if (string.IsNullOrWhiteSpace(name) || !project.ConnectionSource.TryFindConnection(name, out ExternalConnectionBase? connection)
                || connection is not PathExternalConnection pathConnection) return;
            if (connection is IFileExternalConnection fileConnection && fileConnection.TryGetFile(out string filename))
            {
                add(role, filename, false);
                return;
            }
            string? candidate = !string.IsNullOrWhiteSpace(pathConnection.RelativePath) &&
                !Path.IsPathFullyQualified(pathConnection.RelativePath) &&
                !string.IsNullOrWhiteSpace(pathConnection.PathRelativeSourceDirectory)
                    ? Path.GetFullPath(pathConnection.RelativePath, pathConnection.PathRelativeSourceDirectory)
                    : pathConnection.AbsolutePath;
            add(role, candidate, false);
        }

        foreach (BoundaryConditionLine line in boundary.BoundaryConditionLines)
        {
            if (!line.IsEnabled || !line.TryGetHydraulicPayload(out PayloadBase? payload) || payload is not ExternalPayload external)
                continue;
            AddConnection(external.ExternalConnection, "BoundaryPayloadInput");
        }
        foreach (IInitialConditionLayer layer in boundary.InitialConditions.AllLayers())
            if (layer.IsEnabled && layer.SpatialDataType == Ras.Layers.SpatialDataType.Gridded &&
                layer.GridDataLocation == Ras.Layers.Meteorology.DataLocation.Remote)
                AddConnection(layer.ExternalGriddedConnection, "BoundaryInitialConditionInput");

        SpatialBoundaryConditionBase[] spatialLayers =
        [boundary.Wind, boundary.Precipitation, boundary.Evapotranspiration, boundary.AirDensity,
            boundary.AirPressure, boundary.AirTemperature, boundary.Humidity];
        foreach (SpatialBoundaryConditionBase layer in spatialLayers)
            if (layer is not null && layer.IsEnabled && layer.SpatialDataType == Ras.Layers.SpatialDataType.Gridded &&
                layer.GridDataLocation == Ras.Layers.Meteorology.DataLocation.Remote)
                AddConnection(layer.ExternalGriddedConnection, "BoundarySpatialInput");
    }

    private static string PlanComputeFingerprint(ModelSaveRequest model, string projectPath,
        string projectRevision, string projectXmlFingerprint, PlanComputeDependency[] dependencies)
    {
        var identity = new
        {
            projectPath,
            projectRevision,
            projectXmlFingerprint,
            assetPath = SourceFilePaths.Canonical(model.AssetPath),
            model.Revision,
            sourceState = model.SourceState,
            fields = model.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { key = pair.Key, value = pair.Value }).ToArray(),
            timeWindowEdits = model.PlanTimeWindowEdits?.Select(edit => new { edit.Id, edit.FieldID, edit.Value }).ToArray(),
            dependencies = dependencies.Select(item => new { item.Role, item.Path, item.Revision, item.WillWrite }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity, BackendJson.Options)));
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void EnsureDependenciesUnchanged(PlanComputeDependency[] before, PlanComputeDependency[] after)
    {
        if (!before.SequenceEqual(after)) throw new SourceFileChangedException();
    }

    private static PlanPrecomputeMessage[] ConvertMessages(OperationResult operation, Project project, Plan plan,
        string projectPath, string projectRevision, string planPath, string planRevision) => operation.EnumerateAggregatedMessages()
        .Select(aggregate => new PlanPrecomputeMessage(
            aggregate.Message.MessageType.Code,
            aggregate.Message.MessageType.DefaultSeverity.ToString(),
            aggregate.Message.EvaluatedMessage,
            aggregate.Message.LogData.Select(pair => new PlanPrecomputeLogDatum(pair.Key, FormatLogValue(pair.Value),
                ResolvePlanDiagnosticTarget(project, plan, pair.Key, FormatLogValue(pair.Value),
                    projectPath, projectRevision, planPath, planRevision))).ToArray(),
            aggregate.ExtraCount)).ToArray();

    private static PlanComputeDependency[] FinalPlanComputeDependencies(PlanComputeDependency[] initial,
        string? resultPath, string? resultRevision)
    {
        var final = new List<PlanComputeDependency>();
        foreach (PlanComputeDependency dependency in initial)
        {
            if (dependency.Role == "ResultOutput") continue;
            string revision = File.Exists(dependency.Path)
                ? "sha256:" + Revision(dependency.Path) : MissingPlanComputeDependency;
            final.Add(dependency with { Revision = revision });
        }
        if (resultPath is not null)
            final.Add(new("Result", resultPath, resultRevision is null ? MissingPlanComputeDependency : "sha256:" + resultRevision, true));
        return final.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record CancellationControl(string Directory, string Nonce, string OwnerPath, string MarkerPath);

    private static CancellationControl ValidateCancellationControl(PlanComputeCancellation cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        if (!Guid.TryParseExact(cancellation.Nonce, "N", out Guid parsedNonce) ||
            !string.Equals(parsedNonce.ToString("N"), cancellation.Nonce, StringComparison.Ordinal))
            throw new ArgumentException("The compute cancellation nonce is invalid.");
        if (string.IsNullOrWhiteSpace(cancellation.Directory) || !Path.IsPathFullyQualified(cancellation.Directory))
            throw new ArgumentException("The compute cancellation directory must be an absolute private temporary directory.");
        string directory = Path.GetFullPath(cancellation.Directory);
        var info = new DirectoryInfo(directory);
        string temp = SourceFilePaths.Canonical(Path.GetFullPath(Path.GetTempPath()));
        string canonical = SourceFilePaths.Canonical(directory);
        string parent = SourceFilePaths.Canonical(info.Parent?.FullName ?? "");
        if (info.LinkTarget is not null || !string.Equals(parent, temp, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(directory), PlanComputeDirectoryPrefix + cancellation.Nonce, StringComparison.Ordinal) ||
            !string.Equals(canonical, directory, StringComparison.Ordinal))
            throw new ArgumentException("The compute cancellation directory is not a private task-owned temporary directory.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The compute cancellation directory is missing.");
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(directory) !=
            (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            throw new UnauthorizedAccessException("The compute cancellation directory must have mode 0700.");

        string ownerPath = Path.Combine(directory, PlanComputeOwnerFile);
        if (new FileInfo(ownerPath).LinkTarget is not null || !File.Exists(ownerPath))
            throw new ArgumentException("The compute cancellation owner file is missing or invalid.");
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(ownerPath) !=
            (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            throw new UnauthorizedAccessException("The compute cancellation owner file must have mode 0600.");
        if (!string.Equals(File.ReadAllText(ownerPath, Encoding.UTF8), cancellation.Nonce, StringComparison.Ordinal))
            throw new ArgumentException("The compute cancellation owner nonce does not match this request.");
        return new(directory, cancellation.Nonce, ownerPath, Path.Combine(directory, PlanComputeCancelFile));
    }

    private static bool HasValidCancellationMarker(CancellationControl control)
    {
        if (!File.Exists(control.MarkerPath)) return false;
        if (new FileInfo(control.MarkerPath).LinkTarget is not null) return false;
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(control.MarkerPath) !=
            (UnixFileMode.UserRead | UnixFileMode.UserWrite)) return false;
        return string.Equals(File.ReadAllText(control.OwnerPath, Encoding.UTF8), control.Nonce, StringComparison.Ordinal) &&
            string.Equals(File.ReadAllText(control.MarkerPath, Encoding.UTF8), control.Nonce, StringComparison.Ordinal);
    }

    private static async Task MonitorCancellationAsync(CancellationControl control, BatchJob job,
        Func<bool> isAcknowledged, Action<bool> setAcknowledged, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!isAcknowledged() && HasValidCancellationMarker(control))
            {
                job.RequestCancel();
                setAcknowledged(true);
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}

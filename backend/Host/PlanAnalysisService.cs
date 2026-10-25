using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Ras.Interface.WinUI.Debug.R2RMatrix;
using Ras.Layers;
using Ras.Observables;
using Utility.Files;
using Utility.Progress;
using Visual.Observables.Tree;

namespace Ras.Backend;

/// <summary>
/// Portable host endpoints for the retained PlanDiffView and R2RMatrixView.
/// All drafts are replayed into disposable source models. Ready-to-run preparation
/// writes only private dependency copies and the requested output files.
/// </summary>
public static partial class ModelEditorAdapter
{
    private const int PlanMatrixMaximumCombinations = 2048;
    private const int PlanAnalysisMaximumProjectXmlCharacters = 32 * 1024 * 1024;

    public static PlanAnalysisDiffOptionsResponse LoadPlanAnalysisDiffOptions(
        PlanAnalysisDiffOptionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlanAnalysisProjectCapture capture = ValidatePlanAnalysisCapture(request.Capture);
        var project = CoreLayerImportService.OpenProject(capture.ProjectPath, capture.ProjectXml,
            capture.CoreLayerContext);
        try
        {
            var observable = new ObservableProject(project, Utility.SynchronizationContext.None);
            PlanAnalysisDiffOption[] options = BuildPlanDiffOptions(observable);
            EnsurePlanAnalysisSourcesCurrent(capture);
            return new(capture.ProjectPath, capture.ProjectRevision,
                capture.ProjectXmlFingerprint, options);
        }
        finally
        {
            DisposeProjectLayers(project);
        }
    }

    public static PlanAnalysisDiffResponse ComparePlanAnalysis(PlanAnalysisDiffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlanAnalysisProjectCapture capture = ValidatePlanAnalysisCapture(request.Capture);
        PlanAnalysisDiffOption[] options = LoadPlanAnalysisDiffOptions(new(capture)).Options;
        PlanAnalysisDiffOption left = options.SingleOrDefault(item => item.Id == request.LeftOptionID)
            ?? throw new ArgumentException("The left Plan selection is no longer part of this project.");
        PlanAnalysisDiffOption right = options.SingleOrDefault(item => item.Id == request.RightOptionID)
            ?? throw new ArgumentException("The right Plan selection is no longer part of this project.");
        VerifyOptionStillCurrent(left);
        VerifyOptionStillCurrent(right);

        Dictionary<string, PlanAnalysisPlanDraft> drafts = ResolvePlanDrafts(capture,
            request.PlanDrafts ?? []);
        PlanAnalysisDiffRow[] rows;
        using (ModelContext leftContext = OpenAnalysisPlan(capture, left.PlanPath,
                   left.PlanRevision, FindDraft(drafts, left.PlanPath)))
        using (ModelContext rightContext = OpenAnalysisPlan(capture, right.PlanPath,
                   right.PlanRevision, FindDraft(drafts, right.PlanPath)))
        {
            if (leftContext.Layer is not Plan leftPlan || rightContext.Layer is not Plan rightPlan)
                throw new ArgumentException("The selected item is not a Plan.");
            rows = SimplePlanProperties().Select(property =>
            {
                string leftValue = ReadPlanValue(property, leftPlan);
                string rightValue = ReadPlanValue(property, rightPlan);
                return new PlanAnalysisDiffRow(property.Name, leftValue, rightValue,
                    !string.Equals(leftValue, rightValue, StringComparison.Ordinal));
            }).ToArray();
        }

        EnsurePlanAnalysisSourcesCurrent(capture);
        VerifyOptionStillCurrent(left);
        VerifyOptionStillCurrent(right);
        return new(capture.ProjectPath, capture.ProjectRevision,
            capture.ProjectXmlFingerprint, left, right, rows);
    }

    public static PlanMatrixOptionsResponse LoadPlanMatrixOptions(PlanMatrixOptionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlanAnalysisProjectCapture capture = ValidatePlanAnalysisCapture(request.Capture);
        PlanMatrixPlanChoice[] plans;
        var project = CoreLayerImportService.OpenProject(capture.ProjectPath, capture.ProjectXml,
            capture.CoreLayerContext);
        try
        {
            plans = project.Plans
                .Where(plan => !plan.IsResult)
                .OrderBy(plan => plan.Name, Comparer<string>.Create(TreeNode.NameComparer.CompareNames))
                .Select(plan =>
                {
                    string path = SourceFilePaths.Canonical(plan.Filename);
                    return new PlanMatrixPlanChoice(path, Revision(path), plan.Name);
                }).ToArray();
        }
        finally { DisposeProjectLayers(project); }

        string? selectedPath = null;
        string? outputDirectory = null;
        PlanMatrixProperty[] properties = [];
        PlanMatrixRule[] rules = PlanSweepRules.Illegal
            .Select(rule => new PlanMatrixRule(rule.Name, rule.Reason, Enabled: true)).ToArray();
        if (plans.Length > 0)
        {
            string requestedPath = request.SelectedPlanPath is null
                ? plans[0].PlanPath : SourceFilePaths.Canonical(request.SelectedPlanPath);
            PlanMatrixPlanChoice selected = plans.SingleOrDefault(item => SameSource(item.PlanPath, requestedPath))
                ?? throw new ArgumentException("The selected Plan is no longer part of this project.");
            using ModelContext context = OpenAnalysisPlan(capture, selected.PlanPath,
                selected.Revision, ValidateOptionalDraft(capture, selected.PlanPath,
                    selected.Revision, request.PlanDraft));
            if (context.Layer is not Plan plan || plan.IsResult)
                throw new ArgumentException("Ready-to-run matrix requires a source Plan.");
            ValidatePlanReadyToRunOwner(context.Project, plan);
            selectedPath = selected.PlanPath;
            outputDirectory = string.IsNullOrWhiteSpace(plan.ExportDirectory)
                ? null : Path.GetFullPath(plan.ExportDirectory);
            properties = PlanSweepProperty.All.Select(property => MatrixProperty(property, plan)).ToArray();
        }

        EnsurePlanAnalysisSourcesCurrent(capture);
        foreach (PlanMatrixPlanChoice plan in plans) VerifyFileRevision(plan.PlanPath, plan.Revision);
        return new(capture.ProjectPath, capture.ProjectRevision,
            capture.ProjectXmlFingerprint, plans, selectedPath, outputDirectory, properties, rules);
    }

    public static PlanMatrixPreviewResponse PreviewPlanMatrix(PlanMatrixPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PreparedPlanMatrix prepared = BuildPlanMatrix(request);
        EnsurePlanAnalysisSourcesCurrent(prepared.Capture);
        VerifyFileRevision(prepared.PlanPath, prepared.PlanRevision);
        return prepared.Response;
    }

    public static Task<PlanMatrixEmitResponse> EmitPlanMatrixAsync(PlanMatrixEmitRequest request,
        Action<BackendOperationProgress>? report = null) =>
        EmitPlanMatrixCoreAsync(request, report, duplexControls: null);

    internal static Task<PlanMatrixEmitResponse> EmitPlanMatrixDuplexAsync(PlanMatrixEmitRequest request,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader duplexControls) =>
        EmitPlanMatrixCoreAsync(request, report, duplexControls);

    private static async Task<PlanMatrixEmitResponse> EmitPlanMatrixCoreAsync(PlanMatrixEmitRequest request,
        Action<BackendOperationProgress>? report, BackendDuplexControlReader? duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        PreparedPlanMatrix prepared = BuildPlanMatrix(request.Preview);
        if (!string.Equals(prepared.Response.InspectionID, request.InspectionID,
                StringComparison.Ordinal))
            throw new SourceFileChangedException();
        EnsurePlanAnalysisSourcesCurrent(prepared.Capture);

        var reporter = new ProgressReporter();
        var outcomes = new Dictionary<PlanSweepCombination, PlanEmitOutcome>();
        using var stopControls = new CancellationTokenSource();
        int cancelReceived = 0;
        reporter.ProgressReported += (_, percent) => report?.Invoke(new(
            Fraction: Math.Clamp(percent / 100.0, 0, 1), TaskName: "Ready-to-run matrix"));
        reporter.MessageReported += (_, message) => report?.Invoke(new(
            Message: message.EvaluatedMessage, TaskName: "Ready-to-run matrix"));

        Task? controlPump = duplexControls is null ? null : Task.Run(async () =>
        {
            await foreach (BackendDuplexControlFrame frame in duplexControls.ReadAllAsync(stopControls.Token)
                               .ConfigureAwait(false))
            {
                if (!frame.Control.TryGetProperty("type", out JsonElement type) ||
                    !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal)) continue;
                if (Interlocked.Exchange(ref cancelReceived, 1) == 0) reporter.RequestCancel();
            }
        }, stopControls.Token);

        try
        {
            PlanEmitOptions emitOptions = new()
            {
                Overwrite = request.Overwrite,
                NameEmbeddedPlan = request.NameEmbeddedPlan,
                WriteManifest = request.WriteManifest
            };
            PlanEmitSummary summary = await Task.Run(() =>
            {
                using var workspace = new PlanMatrixPreparationWorkspace();
                using ModelContext context = OpenAnalysisPlan(prepared.Capture, prepared.PlanPath,
                    prepared.PlanRevision, ValidateOptionalDraft(prepared.Capture, prepared.PlanPath,
                        prepared.PlanRevision, request.Preview.PlanDraft));
                if (context.Layer is not Plan plan) throw new ArgumentException("The selected source is not a Plan.");
                ValidatePlanReadyToRunOwner(context.Project, plan);
                VerifyFileRevision(prepared.PlanPath, prepared.PlanRevision);
                VerifyPlanMatrixDependencies(prepared.Dependencies);
                workspace.Stage(context.Project, plan);
                VerifyPlanMatrixDependencies(prepared.Dependencies);
                return PlanReadyToRunEmitter.EmitAll(plan, prepared.Response.OutputDirectory,
                    prepared.Combinations, emitOptions,
                    combination =>
                    {
                        report?.Invoke(new(Message: combination.Filename, TaskName: "Ready-to-run matrix",
                            Values: new Dictionary<string, string>
                            {
                                ["phase"] = "writing",
                                ["filename"] = combination.Filename
                            }));
                    },
                    (combination, outcome) =>
                    {
                        outcomes[combination] = outcome;
                        report?.Invoke(new(TaskName: "Ready-to-run matrix",
                            Values: new Dictionary<string, string>
                            {
                                ["phase"] = "fileCompleted",
                                ["filename"] = combination.Filename,
                                ["status"] = outcome.Status.ToString(),
                                ["message"] = outcome.Message ?? string.Empty,
                                ["filePath"] = outcome.FilePath ?? string.Empty,
                                ["fileSizeBytes"] = outcome.FileSizeBytes.ToString(CultureInfo.InvariantCulture)
                            }));
                    }, reporter);
            }).ConfigureAwait(false);

            bool sourcesStillCurrent = IsPlanAnalysisCaptureCurrent(prepared.Capture) &&
                IsFileRevisionCurrent(prepared.PlanPath, prepared.PlanRevision) &&
                prepared.Dependencies.All(source => IsFileRevisionCurrent(source.Path, source.Revision));
            PlanMatrixEmitFile[] files = prepared.Combinations.Select((combination, index) =>
            {
                if (combination.IsExcluded)
                    return new PlanMatrixEmitFile(combination.Filename, "Excluded", null, 0,
                        combination.ExclusionReason);
                if (outcomes.TryGetValue(combination, out PlanEmitOutcome outcome))
                    return new PlanMatrixEmitFile(combination.Filename, outcome.Status.ToString(),
                        outcome.FilePath, outcome.FileSizeBytes, outcome.Message);
                return new PlanMatrixEmitFile(combination.Filename,
                    summary.WasCancelled ? "Cancelled" : "NotRun", null, 0,
                    summary.WasCancelled ? "Cancelled" : summary.AbortReason);
            }).ToArray();
            string state = summary.WasCancelled ? "cancelled"
                : summary.WasAborted || summary.Failed > 0 ? "failed" : "completed";
            return new(prepared.Capture.ProjectPath, prepared.Capture.ProjectRevision,
                prepared.Capture.ProjectXmlFingerprint, prepared.PlanPath, prepared.PlanRevision,
                prepared.Response.InspectionID, prepared.Response.OutputDirectory, state,
                summary.Written, summary.Skipped, summary.Failed, summary.Excluded,
                summary.WasCancelled, summary.WasAborted, summary.AbortReason,
                summary.ManifestPath, sourcesStillCurrent, files);
        }
        finally
        {
            stopControls.Cancel();
            if (controlPump is not null)
            {
                try { await controlPump.ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopControls.IsCancellationRequested) { }
            }
        }
    }

    private sealed record PreparedPlanMatrix(PlanAnalysisProjectCapture Capture,
        string PlanPath, string PlanRevision, List<PlanSweepCombination> Combinations,
        PlanMatrixDependency[] Dependencies, PlanMatrixPreviewResponse Response);

    private static PreparedPlanMatrix BuildPlanMatrix(PlanMatrixPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlanAnalysisProjectCapture capture = ValidatePlanAnalysisCapture(request.Capture);
        string planPath = SourceFilePaths.Canonical(request.PlanPath);
        string planRevision = request.PlanRevision;
        VerifyFileRevision(planPath, planRevision);
        PlanAnalysisPlanDraft? draft = ValidateOptionalDraft(capture, planPath, planRevision, request.PlanDraft);
        using ModelContext context = OpenAnalysisPlan(capture, planPath, planRevision, draft);
        if (context.Layer is not Plan plan || plan.IsResult)
            throw new ArgumentException("Ready-to-run matrix requires a source Plan.");
        ValidatePlanReadyToRunOwner(context.Project, plan);
        PlanMatrixDependency[] dependencies = CapturePlanMatrixDependencies(plan);

        var axes = new List<PlanSweepAxis>();
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlanMatrixAxisSelection selection in request.Axes ?? [])
        {
            if (selection is null || string.IsNullOrWhiteSpace(selection.PropertyName) ||
                !seenProperties.Add(selection.PropertyName))
                throw new ArgumentException("Choose each Plan sweep property once.");
            PlanSweepProperty property = PlanSweepProperty.Find(selection.PropertyName)
                ?? throw new ArgumentException("The selected Plan sweep property is unavailable.");
            var values = new List<object>();
            foreach (string value in selection.Values ?? [])
            {
                if (!property.TryParse(value, out object parsed, out string error))
                    throw new ArgumentException(property.DisplayName + ": " + error);
                if (!values.Any(existing => Equals(existing, parsed))) values.Add(parsed);
            }
            axes.Add(new PlanSweepAxis(property, values));
        }

        var ruleLookup = PlanSweepRules.Illegal.ToDictionary(rule => rule.Name, StringComparer.Ordinal);
        var rules = new List<PlanCombinationRule>();
        foreach (string name in request.EnabledRuleNames ?? [])
        {
            if (!ruleLookup.TryGetValue(name, out PlanCombinationRule? rule))
                throw new ArgumentException("An unsupported ready-to-run rule was selected.");
            if (!rules.Contains(rule)) rules.Add(rule);
        }
        PlanSweepOptions options = new() { IllegalRules = rules, CollapseIrrelevant = request.CollapseIrrelevant };
        if (!PlanSweepMatrixBuilder.TryBuild(plan, axes, options,
                out List<PlanSweepCombination> combinations, out string errorMessage))
            throw new ArgumentException(errorMessage);
        if (combinations.Count > PlanMatrixMaximumCombinations)
            throw new ArgumentException("That's more than 2048 combinations. Narrow the axes down.");

        string defaultOutputDirectory = plan.ExportDirectory;
        if (string.IsNullOrWhiteSpace(request.OutputDirectory) && request.OutputDirectory is not null)
            throw new ArgumentException("Choose an output directory.");
        string outputDirectory = request.OutputDirectory is null
            ? (string.IsNullOrWhiteSpace(defaultOutputDirectory)
                ? throw new ArgumentException("The selected Plan has no ready-to-run output directory.")
                : Path.GetFullPath(defaultOutputDirectory))
            : Path.GetFullPath(request.OutputDirectory);
        string inspectionID = MatrixInspectionID(capture, planPath, planRevision, draft,
            axes, rules, request.CollapseIrrelevant, outputDirectory, dependencies);
        int writeCount = combinations.Count(combination => !combination.IsExcluded);
        int collapsedCount = combinations.Count(combination => combination.IsCollapsed);
        int illegalCount = combinations.Count - writeCount - collapsedCount;
        var summaryParts = new List<string> { $"{combinations.Count} combinations", $"{writeCount} files" };
        if (illegalCount > 0) summaryParts.Add($"{illegalCount} illegal");
        if (collapsedCount > 0) summaryParts.Add($"{collapsedCount} collapsed");
        string hint = BuildInertAxisHint(axes, combinations);
        PlanMatrixCombination[] rows = combinations.Select((combination, index) =>
            new PlanMatrixCombination(index + 1, combination.Label, combination.Filename,
                combination.IsExcluded, combination.IsCollapsed, combination.ExclusionReason,
                combination.Assignments.Select(assignment => new PlanMatrixAssignment(
                    assignment.Property.PropertyName, assignment.Property.DisplayName,
                    assignment.Property.Describe(assignment.Value), assignment.IsRelevant,
                    assignment.IrrelevantReason)).ToArray())).ToArray();
        var response = new PlanMatrixPreviewResponse(capture.ProjectPath, capture.ProjectRevision,
            capture.ProjectXmlFingerprint, planPath, planRevision, inspectionID,
            outputDirectory, writeCount, string.Join(" · ", summaryParts), hint, rows);
        EnsurePlanAnalysisSourcesCurrent(capture);
        VerifyFileRevision(planPath, planRevision);
        VerifyPlanMatrixDependencies(dependencies);
        return new(capture, planPath, planRevision, combinations, dependencies, response);
    }

    private static string BuildInertAxisHint(IReadOnlyList<PlanSweepAxis> axes,
        IReadOnlyList<PlanSweepCombination> combinations)
    {
        var inert = new List<string>();
        foreach (PlanSweepAxis axis in axes)
        {
            PlanSweepAssignment[] assignments = combinations.SelectMany(combination => combination.Assignments)
                .Where(assignment => assignment.Property == axis.Property).ToArray();
            if (assignments.Length == 0 || assignments.Any(assignment => assignment.IsRelevant)) continue;
            string? reason = assignments.Select(assignment => assignment.IrrelevantReason)
                .FirstOrDefault(value => !string.IsNullOrEmpty(value));
            if (!string.IsNullOrEmpty(reason)) inert.Add(axis.Property.DisplayName + " (" + reason + ")");
        }
        return inert.Count == 0 ? string.Empty
            : "Ignored by every combination, so it produces no extra files: " + string.Join("; ", inert) +
              ". Add the switch that enables it as an axis, or turn it on in the plan.";
    }

    private static string MatrixInspectionID(PlanAnalysisProjectCapture capture, string planPath,
        string planRevision, PlanAnalysisPlanDraft? draft, IReadOnlyList<PlanSweepAxis> axes,
        IReadOnlyList<PlanCombinationRule> rules, bool collapseIrrelevant, string outputDirectory,
        PlanMatrixDependency[] dependencies)
    {
        var identity = new
        {
            capture.ProjectPath, capture.ProjectRevision, capture.ProjectXmlFingerprint,
            dependencies,
            planPath, planRevision,
            draft?.Fields,
            draft?.SourceState,
            planTimeWindowEdits = draft?.PlanTimeWindowEdits,
            axes = axes.Select(axis => new
            {
                axis.Property.PropertyName,
                values = axis.Values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)).ToArray()
            }).ToArray(),
            rules = rules.Select(rule => rule.Name).ToArray(),
            collapseIrrelevant,
            outputDirectory
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(identity, BackendJson.Options);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static PlanMatrixProperty MatrixProperty(PlanSweepProperty property, Plan plan)
    {
        IEnumerable<object> values = property.IsClosedSet
            ? property.EnumeratedValues : new[] { property.GetValue(plan) };
        PlanMatrixValueChoice[] initialValues = values.Select(value => new PlanMatrixValueChoice(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            property.Describe(value), Selected: true, Removable: !property.IsClosedSet)).ToArray();
        return new(property.PropertyName, property.DisplayName, property.ValueType.Name,
            property.InputHint, property.IsClosedSet, initialValues);
    }

    private static PlanAnalysisDiffOption[] BuildPlanDiffOptions(ObservableProject project)
    {
        var options = new List<PlanAnalysisDiffOption>();
        foreach (ObservablePlan observablePlan in project.Plans.OrderBy(plan => plan.Name,
                     Comparer<string>.Create(TreeNode.NameComparer.CompareNames)))
        {
            Plan plan = observablePlan.Plan;
            string planPath = SourceFilePaths.Canonical(plan.Filename);
            string planRevision = Revision(planPath);
            options.Add(new("plan|" + planPath, planPath, planRevision, null, null,
                observablePlan.Name, observablePlan.Name, false));
            int resultIndex = 0;
            foreach (ObservableResult result in observablePlan.NameMatchedResults
                         .OrderBy(item => item.Result?.ComputeSummary?.EndTime ?? DateTime.MinValue))
            {
                string? resultPath = string.IsNullOrWhiteSpace(result.Result?.Filename)
                    ? null : SourceFilePaths.Canonical(result.Result.Filename);
                DateTime? endTime = result.Result?.ComputeSummary?.EndTime;
                string computeTime = endTime?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;
                string header = string.IsNullOrEmpty(computeTime)
                    ? result.Name : result.Name + "  (" + computeTime + ")";
                string display = "      → " + (string.IsNullOrEmpty(computeTime)
                    ? result.Name : result.Name + "  (" + computeTime + ")");
                string identityPath = resultPath ?? "missing:" + result.Name;
                // Keep duplicate names or unavailable result files selectable as
                // separate source rows without using display text as identity.
                options.Add(new("result|" + planPath + "|" + identityPath + "|" + resultIndex,
                    planPath, planRevision, resultPath, null, display, header, true));
                resultIndex++;
            }
        }
        return options.ToArray();
    }

    private static IEnumerable<PropertyInfo> SimplePlanProperties() =>
        typeof(Plan).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0 &&
                IsSimplePlanType(property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static bool IsSimplePlanType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!type.IsPrimitive && !type.IsEnum && type != typeof(string) && type != typeof(decimal) &&
            type != typeof(DateTime) && type != typeof(TimeSpan)) return type == typeof(Guid);
        return true;
    }

    private static string ReadPlanValue(PropertyInfo property, Plan plan)
    {
        try { return FormatPlanValue(property.GetValue(plan)); }
        catch (Exception error) { return "<error: " + error.GetType().Name + ">"; }
    }

    private static string FormatPlanValue(object? value)
    {
        if (value is null) return "<null>";
        if (value is double doubleValue) return doubleValue.ToString("0.######", CultureInfo.CurrentCulture);
        if (value is float floatValue) return floatValue.ToString("0.######", CultureInfo.CurrentCulture);
        if (value is DateTime dateTime) return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        return value.ToString() ?? string.Empty;
    }

    private static PlanAnalysisProjectCapture ValidatePlanAnalysisCapture(PlanAnalysisProjectCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (string.IsNullOrWhiteSpace(capture.ProjectPath) || !Path.IsPathFullyQualified(capture.ProjectPath) ||
            string.IsNullOrWhiteSpace(capture.ProjectRevision) || string.IsNullOrWhiteSpace(capture.ProjectXml) ||
            string.IsNullOrWhiteSpace(capture.ProjectXmlFingerprint))
            throw new ArgumentException("Open a saved project and capture its current project draft before using Plan analysis.");
        string projectPath = SourceFilePaths.Canonical(capture.ProjectPath);
        if (!projectPath.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(projectPath))
            throw new ArgumentException("Choose the saved owning project.");
        if (!string.Equals(Revision(projectPath), capture.ProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (capture.CoreLayerContext is { } coreLayerContext)
            CoreLayerImportService.ValidateContext(coreLayerContext, projectPath);
        string fingerprint = HashPlanAnalysisText(capture.ProjectXml);
        if (!string.Equals(fingerprint, capture.ProjectXmlFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The captured project draft fingerprint does not match its XML.");
        _ = ParsePlanAnalysisProjectXml(capture.ProjectXml);
        return capture with { ProjectPath = projectPath };
    }

    private static XElement ParsePlanAnalysisProjectXml(string xml)
    {
        if (xml.Length > PlanAnalysisMaximumProjectXmlCharacters)
            throw new ArgumentException("The captured project XML is too large to inspect.");
        using var text = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = PlanAnalysisMaximumProjectXmlCharacters
        });
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        return document.Root ?? throw new ArgumentException("The captured project XML has no root element.");
    }

    private static ModelContext OpenAnalysisPlan(PlanAnalysisProjectCapture capture, string planPath,
        string revision, PlanAnalysisPlanDraft? draft)
    {
        if (!Path.IsPathFullyQualified(planPath) || !File.Exists(planPath))
            throw new ArgumentException("Choose an existing Plan in the owning project.");
        string canonicalPlan = SourceFilePaths.Canonical(planPath);
        VerifyFileRevision(canonicalPlan, revision);
        if (draft is not null) _ = ValidateOptionalDraft(capture, canonicalPlan, revision, draft);
        var request = new ModelSaveRequest(canonicalPlan, "Plan", capture.ProjectPath, revision,
            draft?.Fields ?? new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, ImageData[]>(StringComparer.Ordinal), capture.ProjectXml,
            draft?.SourceState, PlanTimeWindowEdits: draft?.PlanTimeWindowEdits,
            CoreLayerContext: capture.CoreLayerContext);
        var context = new ModelContext(request);
        try
        {
            if (context.Layer is not Plan plan || plan.IsResult ||
                !context.Project.Plans.Any(candidate => SourceFilePaths.SameSourceFile(candidate.Filename, canonicalPlan)))
                throw new ArgumentException("The selected source Plan is no longer part of this project.");
            Dictionary<string, string> ordinaryFields = ApplyPlanTimeWindowDraft(context, request);
            ApplyFields(context, null, ordinaryFields);
            VerifyFileRevision(canonicalPlan, revision);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static PlanAnalysisPlanDraft? ValidateOptionalDraft(PlanAnalysisProjectCapture capture,
        string planPath, string planRevision, PlanAnalysisPlanDraft? draft)
    {
        if (draft is null) return null;
        if (string.IsNullOrWhiteSpace(draft.AssetPath) || !Path.IsPathFullyQualified(draft.AssetPath) ||
            !SourceFilePaths.SameSourceFile(draft.AssetPath, planPath) ||
            !string.Equals(draft.Revision, planRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (draft.Fields is null) throw new ArgumentException("The captured Plan draft has no field snapshot.");
        if (draft.PlanTimeWindowEdits is { } edits && edits.Any(edit => edit is null))
            throw new ArgumentException("The captured ordered Plan time-window edits are invalid.");
        VerifyFileRevision(planPath, planRevision);
        _ = ValidatePlanAnalysisCapture(capture);
        return draft with { AssetPath = SourceFilePaths.Canonical(draft.AssetPath) };
    }

    private static Dictionary<string, PlanAnalysisPlanDraft> ResolvePlanDrafts(
        PlanAnalysisProjectCapture capture, IEnumerable<PlanAnalysisPlanDraft> drafts)
    {
        var result = new Dictionary<string, PlanAnalysisPlanDraft>(StringComparer.Ordinal);
        foreach (PlanAnalysisPlanDraft draft in drafts)
        {
            if (draft is null) throw new ArgumentException("A captured Plan draft is missing.");
            string path = SourceFilePaths.Canonical(draft.AssetPath);
            PlanAnalysisPlanDraft validated = ValidateOptionalDraft(capture, path, draft.Revision, draft)!;
            if (!result.TryAdd(path, validated))
                throw new ArgumentException("A Plan draft was supplied more than once.");
        }
        return result;
    }

    private static PlanAnalysisPlanDraft? FindDraft(
        IReadOnlyDictionary<string, PlanAnalysisPlanDraft> drafts, string path) =>
        drafts.TryGetValue(SourceFilePaths.Canonical(path), out PlanAnalysisPlanDraft? draft) ? draft : null;

    private static void ValidatePlanReadyToRunOwner(Project project, Plan plan)
    {
        if (plan.IsInMemory) throw new ArgumentException("In-memory plans have no ready-to-run location.");
        if (!project.Plans.Any(candidate => ReferenceEquals(candidate, plan)))
            throw new ArgumentException("The selected Plan is no longer part of the open project.");
        Geometry? geometry = plan.Geometry;
        if (geometry is null)
            throw new ArgumentException("The selected Plan has no associated geometry.");
        if (!project.Geometries.Contains(geometry))
            throw new ArgumentException("The selected Plan points at a geometry the project no longer holds.");
    }

    private static void VerifyOptionStillCurrent(PlanAnalysisDiffOption option)
    {
        VerifyFileRevision(option.PlanPath, option.PlanRevision);
    }

    private static void VerifyFileRevision(string path, string expectedRevision)
    {
        if (!IsFileRevisionCurrent(path, expectedRevision)) throw new SourceFileChangedException();
    }

    private static bool IsFileRevisionCurrent(string path, string expectedRevision)
    {
        try
        {
            return File.Exists(path) && string.Equals(Revision(path), expectedRevision,
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void EnsurePlanAnalysisSourcesCurrent(PlanAnalysisProjectCapture capture)
    {
        if (!IsPlanAnalysisCaptureCurrent(capture)) throw new SourceFileChangedException();
    }

    private static bool IsPlanAnalysisCaptureCurrent(PlanAnalysisProjectCapture capture) =>
        IsFileRevisionCurrent(capture.ProjectPath, capture.ProjectRevision) &&
        string.Equals(HashPlanAnalysisText(capture.ProjectXml), capture.ProjectXmlFingerprint,
            StringComparison.OrdinalIgnoreCase);

    private static string HashPlanAnalysisText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool SameSource(string left, string right) =>
        SourceFilePaths.SameSourceFile(left, right);
}

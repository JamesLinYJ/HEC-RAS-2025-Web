using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Ras.Layers;
using Ras.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private const string PlanTimeWindowDraftKind = "PlanTimeWindowDraft";
    private sealed record PlanTimeWindowDraftState(
        string Kind,
        string AssetPath,
        string Revision,
        DateTime InternalStartTime,
        double InternalComputeDurationSeconds,
        PlanTimeWindowMode TimeWindowMode);

    private static PlanTimeWindowFieldIDs GetPlanTimeWindowFieldIDs(ModelContext context)
    {
        if (context.Layer is not Plan) throw new ArgumentException("Time-window fields require a plan.");
        Field[] fields = FieldsFor(context.Layer);
        string KeyFor(string propertyName)
        {
            Field field = fields.Single(field => PropertyName(field) == propertyName);
            return Key(field);
        }
        return new PlanTimeWindowFieldIDs(
            KeyFor(nameof(ObservablePlan.TimeWindowMode)),
            KeyFor(nameof(ObservablePlan.ComputeStartTime)),
            KeyFor(nameof(ObservablePlan.ComputeEndTime)));
    }

    private static string SerializePlanTimeWindowDraft(ModelContext context, string assetPath, string revision)
    {
        if (context.Layer is not Plan plan || context.Observable is not ObservablePlan observable)
            throw new ArgumentException("Time-window state requires a source plan.");
        PlanTimeWindowDraftState state = new(PlanTimeWindowDraftKind, SourceFilePaths.Canonical(assetPath), revision,
            observable.StartTime, plan.InternalComputeDurationSeconds, observable.TimeWindowMode);
        return JsonSerializer.Serialize(state, BackendJson.Options);
    }

    private static PlanTimeWindowDraftState? ParsePlanTimeWindowDraft(ModelContext context, ModelSaveRequest request)
    {
        if (request.SourceState is null) return null;
        PlanTimeWindowDraftState state;
        try
        {
            state = JsonSerializer.Deserialize<PlanTimeWindowDraftState>(request.SourceState, BackendJson.Options)
                ?? throw new ArgumentException("Missing plan time-window draft state.");
        }
        catch (JsonException error)
        {
            throw new ArgumentException("Invalid plan time-window draft state.", error);
        }
        if (state.Kind != PlanTimeWindowDraftKind || !SourceFilePaths.SameSourceFile(state.AssetPath, request.AssetPath))
            throw new ArgumentException("The plan time-window draft belongs to another source plan.");
        if (state.Revision != request.Revision) throw new SourceFileChangedException();
        if (!double.IsFinite(state.InternalComputeDurationSeconds) || state.InternalComputeDurationSeconds <= 0 ||
            !Enum.IsDefined(typeof(PlanTimeWindowMode), state.TimeWindowMode))
            throw new ValidationException("The plan time-window draft is invalid.");
        if (context.Layer is not Plan || context.Observable is not ObservablePlan)
            throw new ArgumentException("Time-window state requires a source plan.");
        return state;
    }

    // Restore the last acknowledged internal window first, then replay new UI
    // changes through the original observable setters in their captured order.
    // A null event array is retained for older callers that still send the
    // window as ordinary fields; new clients always send the typed array.
    private static Dictionary<string, string> ApplyPlanTimeWindowDraft(ModelContext context, ModelSaveRequest request)
    {
        if (context.Layer is not Plan || context.Observable is not ObservablePlan) return request.Fields;

        if (((Plan)context.Layer).IsResult)
        {
            PlanTimeWindowFieldIDs planFields = GetPlanTimeWindowFieldIDs(context);
            if (request.Fields.Keys.Any(planFields.Contains) || request.PlanTimeWindowEdits is { Length: > 0 })
                throw new ValidationException("Result plans are read-only.");
        }

        PlanTimeWindowDraftState? state = ParsePlanTimeWindowDraft(context, request);
        if (state is not null)
        {
            var observable = (ObservablePlan)context.Observable;
            var plan = (Plan)context.Layer;
            if (plan.IsResult)
            {
                if (observable.StartTime != state.InternalStartTime ||
                    Math.Abs(plan.InternalComputeDurationSeconds - state.InternalComputeDurationSeconds) > 1e-7 ||
                    observable.TimeWindowMode != state.TimeWindowMode)
                    throw new ValidationException("Result plans cannot restore a changed time-window draft.");
            }
            else
            {
                observable.StartTime = state.InternalStartTime;
                observable.EndTime = state.InternalStartTime.AddSeconds(state.InternalComputeDurationSeconds);
                observable.TimeWindowMode = state.TimeWindowMode;
                if (observable.StartTime != state.InternalStartTime ||
                    Math.Abs(plan.InternalComputeDurationSeconds - state.InternalComputeDurationSeconds) > 1e-7 ||
                    observable.TimeWindowMode != state.TimeWindowMode)
                    throw new ValidationException("The source plan rejected its saved time-window draft.");
            }
        }

        if (request.PlanTimeWindowEdits is null) return request.Fields;
        PlanTimeWindowFieldIDs fieldIDs = GetPlanTimeWindowFieldIDs(context);
        if (request.Fields.Keys.Any(fieldIDs.Contains))
            throw new ArgumentException("Plan time-window edits must use the ordered edit list.");
        Field[] fields = FieldsFor(context.Layer);
        HashSet<string> eventIDs = new(StringComparer.Ordinal);
        foreach (PlanTimeWindowEdit edit in request.PlanTimeWindowEdits)
        {
            if (edit is null || string.IsNullOrWhiteSpace(edit.Id) || !eventIDs.Add(edit.Id))
                throw new ArgumentException("Plan time-window edit identities must be unique and non-empty.");
            if (!fieldIDs.Contains(edit.FieldID)) throw new ArgumentException("Unknown plan time-window field.");
            Field field = fields.Single(candidate => Key(candidate) == edit.FieldID);
            object target = Target(field, context);
            PropertyInfo property = Property(field, context);
            if (field.ReadOnly || property.SetMethod?.IsPublic != true || field.Kind == "images")
                throw new ArgumentException("The plan time-window field is read-only.");
            object value = Parse(edit.Value, property.PropertyType, field.AllowZero);
            var validation = new ValidationContext(target) { MemberName = property.Name };
            Validator.ValidateValue(value, validation, property.GetCustomAttributes<ValidationAttribute>());
            property.SetValue(target, value);
            if (!Equals(property.GetValue(target), value))
                throw new ValidationException("The current plan settings do not accept " + property.Name + ".");
        }

        return request.Fields;
    }

    public static async Task<ModelContextRefreshResult> RefreshPlanContextAsync(ModelSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != "Plan") throw new NotSupportedException("Plan context refresh is only available for plans.");
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Plan context refresh requires its owning project.");
        string projectPath = Path.GetFullPath(request.ProjectPath);
        if (!projectPath.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(projectPath))
            throw new ArgumentException("Choose the saved owning project.");
        if (string.IsNullOrWhiteSpace(request.AssetPath))
            throw new ArgumentException("Choose an existing plan in the owning project.");
        if (!Path.IsPathFullyQualified(request.AssetPath) || !File.Exists(request.AssetPath))
            throw new ArgumentException("Choose an existing plan in the owning project.");
        string? projectXml = request.ProjectXml;
        if (projectXml is null) throw new ArgumentException("Plan context refresh requires the current project draft.");
        Dictionary<string, string>? fields = request.Fields;
        if (fields is null) throw new ArgumentException("Plan context refresh requires a plan field draft.");
        if (HasRows(request.Rows) || HasSamples(request.Samples))
            throw new NotSupportedException("Plan context refresh does not apply table or time-series edits.");

        string assetPath = Path.GetFullPath(request.AssetPath);
        string revision = Revision(assetPath);
        if (revision != request.Revision) throw new SourceFileChangedException();

        VerifyOwningProject(projectPath, assetPath, request.CoreLayerContext);

        ModelRequest model = new(assetPath, "Plan", projectPath, projectXml,
            CoreLayerContext: request.CoreLayerContext);
        using var context = new ModelContext(model);
        if (context.Layer is not Plan plan || context.Observable is not ObservablePlan)
            throw new ArgumentException("The selected model is not a source plan.");
        if (plan.IsResult && (fields.Count > 0 || request.PlanTimeWindowEdits is { Length: > 0 }))
            throw new ValidationException("Result plans are read-only.");

        ModelSnapshot baseline = await Snapshot(context, revision, hasProject: true, boundary: null);
        // Match the source editor/save setter ordering and validation. The project
        // XML is applied by ModelContext first; changes remain in memory.
        Dictionary<string, string> ordinaryFields = ApplyPlanTimeWindowDraft(context, request);
        ApplyFields(context, null, ordinaryFields);
        if (plan.InternalComputeDurationSeconds <= 0)
            throw new ValidationException("The plan end time must follow its start time.");
        ModelSnapshot draft = await Snapshot(context, revision, hasProject: true, boundary: null);

        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        return new ModelContextRefreshResult(baseline, draft);
    }

    private static bool HasRows(Dictionary<string, ModelRow[]>? rows) =>
        rows?.Any(pair => pair.Value is null || pair.Value.Length > 0) == true;

    private static bool HasSamples(Dictionary<string, ModelSample[]>? samples) =>
        samples?.Any(pair => pair.Value is null || pair.Value.Length > 0) == true;

    private static void VerifyOwningProject(string projectPath, string assetPath,
        CoreLayerRequestContext? coreLayerContext)
    {
        var project = CoreLayerImportService.OpenProject(projectPath, null, coreLayerContext);
        try
        {
            if (!project.Plans.Any(plan => SourceFilePaths.SameSourceFile(plan.Filename, assetPath)))
                throw new ArgumentException("The plan does not belong to the selected project.");
        }
        finally
        {
            DisposeProjectLayers(project);
        }
    }

    private static void DisposeProjectLayers(Project project)
    {
        IEnumerable<IDisposable> layers = project.Plans.Cast<IDisposable>()
            .Concat(project.Geometries.Cast<IDisposable>())
            .Concat(project.Terrains.Cast<IDisposable>())
            .Concat(project.BoundaryConditions.Cast<IDisposable>())
            .Concat(project.NValues.Cast<IDisposable>())
            .Concat(project.ClassificationLayers.Cast<IDisposable>())
            .Concat(project.Results.Cast<IDisposable>());
        foreach (IDisposable layer in layers) layer.Dispose();
    }
}

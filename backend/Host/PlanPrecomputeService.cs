using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Observables;
using Utility.Logging;

namespace Ras.Backend;

public sealed record PlanPrecomputeLogDatum(string Key, string? Value, PlanDiagnosticTarget? Target = null);
public sealed record PlanPrecomputeMessage(string Code, string DefaultSeverity,
    string EvaluatedMessage, PlanPrecomputeLogDatum[] LogData, int ExtraCount);
public sealed record PlanPrecomputeResult(string Revision, bool Result, PlanPrecomputeMessage[] Messages);

public static partial class ModelEditorAdapter
{
    private const string MissingPlanPrecomputeDependency = "missing";

    public static Task<PlanPrecomputeResult> PrecomputePlanAsync(ModelSaveRequest request) =>
        PrecomputePlanCoreAsync(request, null, null);

    public static async Task<PlanPrecomputeDraftResponse> PrecomputePlanDraftsAsync(
        PlanPrecomputeDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Plan);
        if (string.IsNullOrWhiteSpace(request.CaptureID))
            throw new ArgumentException("Plan precompute requires a draft capture identity.");
        PlanPrecomputeResult result = await PrecomputePlanCoreAsync(request.Plan,
            request.AssociatedGeometry, request.AssociatedBoundaryCondition).ConfigureAwait(false);
        return new PlanPrecomputeDraftResponse(request.CaptureID, result);
    }

    private static async Task<PlanPrecomputeResult> PrecomputePlanCoreAsync(ModelSaveRequest request,
        ModelSaveRequest? associatedGeometry, ModelSaveRequest? associatedBoundaryCondition)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != "Plan") throw new NotSupportedException("Plan precompute is only available for plans.");
        if (string.IsNullOrWhiteSpace(request.ProjectPath) || !Path.IsPathFullyQualified(request.ProjectPath))
            throw new ArgumentException("Plan precompute requires its owning project.");
        string projectPath = Path.GetFullPath(request.ProjectPath);
        if (!projectPath.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(projectPath))
            throw new ArgumentException("Choose the saved owning project.");
        if (string.IsNullOrWhiteSpace(request.AssetPath) || !Path.IsPathFullyQualified(request.AssetPath) || !File.Exists(request.AssetPath))
            throw new ArgumentException("Choose an existing plan in the owning project.");
        if (request.ProjectXml is null)
            throw new ArgumentException("Plan precompute requires the current project draft.");
        ArgumentNullException.ThrowIfNull(request.Fields);
        if (HasRows(request.Rows) || HasSamples(request.Samples))
            throw new NotSupportedException("Plan precompute does not apply table or time-series edits.");

        string assetPath = Path.GetFullPath(request.AssetPath);
        string revision = Revision(assetPath);
        if (revision != request.Revision) throw new SourceFileChangedException();
        VerifyOwningProject(projectPath, assetPath, request.CoreLayerContext);
        string projectRevision = Revision(projectPath);
        Dictionary<string, string> dependencySnapshot = CapturePlanPrecomputeDependencies(
            projectPath, assetPath, request.ProjectXml, request.CoreLayerContext);

        ModelRequest model = new(assetPath, "Plan", projectPath, request.ProjectXml, request.SourceState,
            request.CoreLayerContext);
        PlanPrecomputeResult result;
        bool geometryPreparePending;
        using (var context = new ModelContext(model))
        {
            if (context.Layer is not Plan plan || context.Observable is not ObservablePlan observable)
                throw new ArgumentException("The selected model is not a source plan.");
            if (plan.IsResult)
                throw new ValidationException("Result plans are read-only.");

            geometryPreparePending = await ApplyPlanPrecomputeAssociatedDrafts(context, plan, request,
                associatedGeometry, associatedBoundaryCondition).ConfigureAwait(false);

            Dictionary<string, string> ordinaryFields = ApplyPlanTimeWindowDraft(context, request);
            ApplyFields(context, null, ordinaryFields);
            ValidatePlanPrecomputeImageKeys(context.Layer, request.Images);

            // This is the source editor's pure CanCompute check. It does not
            // prepare geometry, create a job, solve, or save any layer.
            observable.UpdateLatestPreComputeResult();
            OperationResult operation = observable.LatestPreComputeResult
                ?? throw new InvalidDataException("The source plan returned no precompute result.");
            string planPath = SourceFilePaths.Canonical(plan.Filename);
            string planRevision = Revision(planPath);
            PlanPrecomputeMessage[] messages = operation.EnumerateAggregatedMessages()
                .Select(aggregate => new PlanPrecomputeMessage(
                    aggregate.Message.MessageType.Code,
                    aggregate.Message.MessageType.DefaultSeverity.ToString(),
                    aggregate.Message.EvaluatedMessage,
                    aggregate.Message.LogData.Select(pair => new PlanPrecomputeLogDatum(
                        pair.Key, FormatLogValue(pair.Value), ResolvePlanDiagnosticTarget(context.Project, plan,
                            pair.Key, FormatLogValue(pair.Value), projectPath, projectRevision,
                            planPath, planRevision))).ToArray(),
                    aggregate.ExtraCount))
                .ToArray();
            if (geometryPreparePending)
            {
                messages = [.. messages, new PlanPrecomputeMessage(
                    "Geometry_PrepareDraftPending", "Error",
                    "The associated Geometry has a pending Prepare request. This read-only check does not run it because the source operation saves the Geometry file. Save or complete Geometry preparation, then refresh the Plan check.",
                    [new PlanPrecomputeLogDatum("GeometryName", context.Project.GetPlanAssociation(plan).Geometry?.Name)], 0)];
            }
            result = new PlanPrecomputeResult(revision, operation.Result && !geometryPreparePending, messages);
        }

        if (Revision(assetPath) != revision || Revision(projectPath) != projectRevision)
            throw new SourceFileChangedException();
        VerifyPlanPrecomputeDependencies(projectPath, assetPath, request.ProjectXml,
            request.CoreLayerContext, dependencySnapshot);
        return result;
    }

    private static async Task<bool> ApplyPlanPrecomputeAssociatedDrafts(ModelContext context, Plan plan,
        ModelSaveRequest planRequest, ModelSaveRequest? geometryRequest,
        ModelSaveRequest? boundaryRequest)
    {
        PlanAssociation association = context.Project.GetPlanAssociation(plan);
        bool geometryPreparePending = false;
        if (geometryRequest is not null)
        {
            string expectedPath = association.Geometry?.Filename
                ?? throw new ArgumentException("The draft Geometry is not associated with this Plan.");
            VerifyAssociatedPrecomputeRequest(planRequest, geometryRequest, "Geometry", expectedPath);
            string sourcePath = Path.GetFullPath(expectedPath);
            using ModelContext geometryContext = context.ForAssociatedLayer("Geometry", sourcePath);
            if (geometryContext.Layer is not Geometry)
                throw new ArgumentException("The selected Plan Geometry association is not a source Geometry.");
            ApplyGeometryTopologyDraft(geometryContext, sourcePath, geometryRequest.Revision,
                geometryRequest.GeometryTopologyState);
            if (!string.IsNullOrWhiteSpace(geometryRequest.GeometryMeshState))
            {
                GeometryMeshDraftState meshState = ReadGeometryMeshDraft(geometryRequest.GeometryMeshState,
                    sourcePath, geometryRequest.Revision);
                geometryPreparePending = meshState.Mutations.Any(mutation => mutation.Operation == "prepare");
            }
            await ApplyGeometryMeshDraftForPlanPrecompute(geometryContext, sourcePath, geometryRequest.Revision,
                geometryRequest.GeometryMeshState).ConfigureAwait(false);
            ApplyGeometryCulvertDraft(geometryContext, geometryRequest);
            ApplyFields(geometryContext, null, geometryRequest.Fields);
            ValidatePlanPrecomputeImageKeys(geometryContext.Layer, geometryRequest.Images);
            if (HasRows(geometryRequest.Rows) || HasSamples(geometryRequest.Samples))
                throw new NotSupportedException("Geometry precompute drafts do not apply table or time-series edits.");
            VerifyAssociatedPrecomputeSource(geometryRequest, sourcePath);
        }

        if (boundaryRequest is not null)
        {
            string expectedPath = association.BoundaryCondition?.Filename
                ?? throw new ArgumentException("The draft Boundary Condition is not associated with this Plan.");
            VerifyAssociatedPrecomputeRequest(planRequest, boundaryRequest, "Boundary Conditions", expectedPath);
            string sourcePath = Path.GetFullPath(expectedPath);
            using ModelContext boundaryContext = context.ForAssociatedLayer("Boundary Conditions", sourcePath);
            BoundaryEditorService boundary = Boundary(boundaryContext, sourcePath,
                boundaryRequest.Revision, boundaryRequest.SourceState)
                ?? throw new ArgumentException("The selected Plan Boundary Condition is unavailable.");
            ApplyFields(boundaryContext, boundary, boundaryRequest.Fields);
            boundary.ApplyData(boundaryRequest.Rows, boundaryRequest.Samples);
            if (boundaryContext.Layer is BoundaryCondition condition && condition.Duration <= TimeSpan.Zero)
                throw new ValidationException("The boundary end time must follow its start time.");
            ValidatePlanPrecomputeImageKeys(boundaryContext.Layer, boundaryRequest.Images);
            VerifyAssociatedPrecomputeSource(boundaryRequest, sourcePath);
        }
        return geometryPreparePending;
    }

    private static async Task ApplyGeometryMeshDraftForPlanPrecompute(ModelContext context,
        string assetPath, string revision, string? encodedState)
    {
        if (string.IsNullOrWhiteSpace(encodedState)) return;
        GeometryMeshDraftState state = ReadGeometryMeshDraft(encodedState, assetPath, revision);
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(context));
        // Plan.CanCompute does not consume property tables. Preserve ordinary
        // in-memory mesh edits and generated previews, but do not run a queued
        // terrain-dependent table computation as a side effect of refreshing
        // readiness; the geometry editor replays that explicit action on save.
        _ = await ApplyGeometryMeshMutationsAsync(context, assetPath, revision, state,
            replayPropertyTableComputations: false).ConfigureAwait(false);
    }

    private static void VerifyAssociatedPrecomputeRequest(ModelSaveRequest planRequest,
        ModelSaveRequest childRequest, string expectedKind, string expectedPath)
    {
        if (!string.Equals(childRequest.Kind, expectedKind, StringComparison.Ordinal))
            throw new ArgumentException("The associated draft has the wrong model kind.");
        if (string.IsNullOrWhiteSpace(childRequest.ProjectPath)
            || !SourceFilePaths.SameSourceFile(childRequest.ProjectPath, planRequest.ProjectPath!))
            throw new ArgumentException("The associated draft must use the Plan's owning project.");
        if (!string.Equals(childRequest.ProjectXml, planRequest.ProjectXml, StringComparison.Ordinal))
            throw new ArgumentException("The associated draft must use the captured Plan project context.");
        if (string.IsNullOrWhiteSpace(childRequest.AssetPath) || !Path.IsPathFullyQualified(childRequest.AssetPath)
            || !SourceFilePaths.SameSourceFile(childRequest.AssetPath, expectedPath))
            throw new ArgumentException("The associated draft is not the exact model selected by this Plan.");
        VerifyAssociatedPrecomputeSource(childRequest, Path.GetFullPath(expectedPath));
    }

    private static void VerifyAssociatedPrecomputeSource(ModelSaveRequest request, string expectedPath)
    {
        if (!File.Exists(expectedPath) || Revision(expectedPath) != request.Revision)
            throw new SourceFileChangedException();
    }

    private static void ValidatePlanPrecomputeImageKeys(CoreLayerBase layer,
        Dictionary<string, ImageData[]> images)
    {
        Field[] imageFields = FieldsFor(layer).Where(field => field.Kind == "images").ToArray();
        string[] imageKeys = imageFields.Select(Key).ToArray();
        foreach (string key in images.Keys)
            if (!imageKeys.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException("Unsupported image collection: " + key);
        // CanCompute never reads embedded images. Keep their pending draft in
        // the caller capture without applying it: ReplaceImages deletes HDF
        // datasets immediately through UseWriter, even before layer.Save.
        // Only ordinary staged Save may materialize these image changes.
    }

    internal static Dictionary<string, string> CapturePlanPrecomputeDependencies(
        string projectPath, string assetPath, string projectXml,
        CoreLayerRequestContext? coreLayerContext)
    {
        var project = CoreLayerImportService.OpenProject(projectPath, projectXml, coreLayerContext);
        try
        {
            Plan plan = project.Plans.FirstOrDefault(candidate =>
                SourceFilePaths.SameSourceFile(candidate.Filename, assetPath))
                ?? throw new ArgumentException("The plan does not belong to the selected project.");

            var dependencies = new Dictionary<string, string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

            void AddDependency(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string canonicalPath = SourceFilePaths.Canonical(path);
                dependencies[canonicalPath] = File.Exists(canonicalPath)
                    ? "sha256:" + Revision(canonicalPath)
                    : MissingPlanPrecomputeDependency;
            }

            AddDependency(plan.Filename);
            if (plan.IsResult) return dependencies;

            PlanAssociation association = project.GetPlanAssociation(plan);
            if (!string.IsNullOrWhiteSpace(association.GeometryName))
            {
                Geometry? geometry = association.Geometry;
                AddDependency(geometry?.Filename ?? Geometry.MakeFilename(
                    project.ProjectDirectory, association.GeometryName));

                if (geometry is not null)
                {
                    GeometryAssociation geometryAssociation = project.GetGeometryAssociation(geometry);
                    if (!string.IsNullOrWhiteSpace(geometryAssociation.TerrainName))
                    {
                        Terrain? terrain = geometryAssociation.Terrain;
                        AddDependency(terrain?.Filename ?? Terrain.MakeFilename(
                            project.ProjectDirectory, geometryAssociation.TerrainName));
                    }
                    if (!string.IsNullOrWhiteSpace(geometryAssociation.NValueLayerName))
                    {
                        NValueLayer? nValues = geometryAssociation.NValueLayer;
                        AddDependency(nValues?.Filename ?? NValueLayer.MakeFilename(
                            project.ProjectDirectory, geometryAssociation.NValueLayerName));
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(association.BoundaryConditionName))
            {
                BoundaryCondition? boundary = association.BoundaryCondition;
                AddDependency(boundary?.Filename ?? BoundaryCondition.MakeFilename(
                    project.ProjectDirectory, association.BoundaryConditionName));
            }

            return dependencies;
        }
        finally
        {
            DisposeProjectLayers(project);
        }
    }

    internal static void VerifyPlanPrecomputeDependencies(string projectPath, string assetPath,
        string projectXml, CoreLayerRequestContext? coreLayerContext,
        Dictionary<string, string> expected)
    {
        Dictionary<string, string> current = CapturePlanPrecomputeDependencies(projectPath, assetPath,
            projectXml, coreLayerContext);
        bool unchanged = expected.Count == current.Count && expected.All(pair =>
            current.TryGetValue(pair.Key, out string? state) && string.Equals(pair.Value, state, StringComparison.Ordinal));
        if (!unchanged) throw new SourceFileChangedException();
    }

    private static string? FormatLogValue(object? value) => value switch
    {
        null => null,
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}

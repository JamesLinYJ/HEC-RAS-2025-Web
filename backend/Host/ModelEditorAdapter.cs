using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ras.Engine.Native;
using Ras.Layers;
using H5Assist;
using System.Xml.Linq;
using Ras.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    public static SurfaceCatalogEntry[] Catalog(ModelCatalogRequest request) => request.AssetPaths.Distinct(StringComparer.Ordinal).Select(filename =>
    {
        string path = Path.GetFullPath(filename);
        try
        {
            using var reader = new H5Reader(path);
            bool nvalues = reader.ExistsAsGroup(NValueLayer.HDF5Group), classification = reader.ExistsAsGroup(ClassificationLayer.HDF5Group);
            if (nvalues == classification) throw new InvalidDataException("The file is not an unambiguous original surface layer.");
            return new SurfaceCatalogEntry(path, nvalues ? "NValues" : "Classifications", null);
        }
        catch (Exception error) { return new SurfaceCatalogEntry(path, null, error.Message); }
    }).ToArray();

    private sealed record Field(string Id, string Scope, string Kind, bool ReadOnly, string[] EnabledBy, bool AllowZero);
    private sealed record Schema(string Id, Field[] Fields);
    private sealed record Library(Schema[] Schemas);
    private static readonly Schema[] Schemas = LoadSchemas();
    private static Field[] FieldsFor(CoreLayerBase layer) => Schemas.Where(schema => SupportedSchemas(layer).Contains(schema.Id)).SelectMany(schema => schema.Fields).ToArray();
    private static string[] SupportedSchemas(CoreLayerBase layer) => layer switch
    {
        Plan => ["PlanCatalogSummary", "PlanSettingsPage", "PlanComputeSettingsPage"],
        Geometry => ["GeometryCatalogSummary", "GeometrySummaryPage"],
        Terrain => ["TerrainCatalogSummary", "TerrainProperties"],
        BoundaryCondition => ["BoundaryConditionsSummary", "BoundaryConditionCatalogSummary", "BoundaryConditionTimeWindow", "PrecipitationControl", "EvapotranspirationControl", "WindForcesControl", "WindConstantControl", "AirPressureControl", "AirDensityControl"],
        NValueLayer => ["NValueCatalogSummary"],
        ClassificationLayer => ["ClassificationCatalogSummary", "ClassificationLayerSummary"],
        _ => throw new NotSupportedException("Unsupported source model type: " + layer.GetType().Name)
    };

    public static async Task<ModelSnapshot> LoadAsync(ModelRequest request)
    {
        VerifyKind(request);
        string revision = Revision(request.AssetPath);
        using var context = new ModelContext(request);
        ModelSnapshot snapshot = await Snapshot(context, revision, request.ProjectPath != null, Boundary(context, request.AssetPath, revision, request.SourceState), Manning(context, request.AssetPath, revision, request.SourceState), Classifications(context, request.AssetPath, revision, request.SourceState), TerrainInputs(context, request.AssetPath, revision, request.SourceState));
        if (Revision(request.AssetPath) != revision) throw new SourceFileChangedException();
        return snapshot with { CoreLayerContext = request.CoreLayerContext };
    }

    public static Task<ModelSnapshot> SaveAsync(ModelSaveRequest request, SpatialGridSelection? gridImport = null,
        ClassificationImportFile[]? classificationImport = null,
        BoundaryInitialConditionsStagingContext? initialConditions = null,
        CumulusGridImportOperation? cumulusImport = null) =>
        SaveModelCoreAsync(request, gridImport, classificationImport, initialConditions, cumulusImport);

    internal static async Task<ModelSnapshot> SaveModelCoreAsync(ModelSaveRequest request, SpatialGridSelection? gridImport = null,
        ClassificationImportFile[]? classificationImport = null,
        BoundaryInitialConditionsStagingContext? initialConditions = null,
        CumulusGridImportOperation? cumulusImport = null,
        DssGridImportCancellation? dssGridCancellation = null,
        TerrainSaveCommitTestHooks? terrainSaveTestHooks = null)
    {
        if (initialConditions is not null) request = initialConditions.UseCapturedProjectXml(request);
        VerifyKind(request);
        if (string.IsNullOrEmpty(request.ProjectPath)) throw new ArgumentException("Saving requires the owning project.");
        string projectPath = request.ProjectPath;
        if (Revision(request.AssetPath) != request.Revision) throw new SourceFileChangedException();
        // Replacing a symbolic-link entry would silently move a Terrain to a
        // different canonical source after its receipt was verified.
        string original = request.Kind == "Terrain"
            ? SourceFilePaths.Canonical(request.AssetPath) : Path.GetFullPath(request.AssetPath);
        string staging = Path.Combine(Path.GetDirectoryName(original)!, ".model-edit-" + Guid.NewGuid().ToString("N") + ".pending");
        string? terrainProjectRevision = request.Kind == "Terrain" ? Revision(projectPath) : null;
        string? boundaryState = null;
        TerrainEditor? terrainForSave = null;
        int? classificationSelection = null, terrainSelection = null;
        ModelSnapshot? dssCommittedSnapshot = null;
        ModelSnapshot? terrainCommittedSnapshot = null;
        List<string> createdRasters = []; bool published = false;
        try
        {
            dssGridCancellation?.ThrowIfCancellationRequested();
            // Start from the actual input, retaining unknown HDF objects/attributes.
            File.Copy(original, staging);
            using (var context = new ModelContext(request, staging))
            {
                var boundary = Boundary(context, request.AssetPath, request.Revision, request.SourceState);
                var manning = Manning(context, request.AssetPath, request.Revision, request.SourceState);
                var classification = Classifications(context, request.AssetPath, request.Revision, request.SourceState);
                var terrain = TerrainInputs(context, request.AssetPath, request.Revision, request.SourceState);
                terrain?.CaptureSelectedInput(request.Fields);
                terrain?.ApplySelections(request.TableSelections);
                terrainForSave = terrain;
                ApplyGeometryTopologyDraft(context, request.AssetPath, request.Revision, request.GeometryTopologyState);
                await ApplyGeometryMeshDraft(context, request.AssetPath, request.Revision, request.GeometryMeshState);
                ApplyGeometryCulvertDraft(context, request);
                Dictionary<string, string> ordinaryFields = ApplyPlanTimeWindowDraft(context, request);
                ApplyFields(context, boundary, ordinaryFields);
                classification?.Apply(request.Fields, request.Rows);
                classificationSelection = classification?.SelectedIndex;
                terrain?.Apply(request.Fields); terrainSelection = terrain?.SelectedIndex;
                terrain?.ApplyRows(request.Rows, request.ChangedCells);
                if (terrain != null && request.Samples?.Any(pair => pair.Value.Length > 0) == true)
                    throw new NotSupportedException("Terrain series edits are not connected to this source editor.");
                manning?.Apply(request.Fields, request.Rows);
                boundary?.ApplyData(request.Rows, request.Samples);
                if (context.Layer is BoundaryCondition condition && condition.Duration <= TimeSpan.Zero) throw new ValidationException("The boundary end time must follow its start time.");
                if (boundary == null && manning == null && classification == null && terrain == null && (request.Rows?.Any(pair => pair.Value.Length > 0) == true || request.Samples?.Any(pair => pair.Value.Length > 0) == true))
                    throw new NotSupportedException("This model's table and series edits are not connected.");
                if (context.Layer is Plan plan && plan.InternalComputeDurationSeconds <= 0) throw new ValidationException("The plan end time must follow its start time.");
                var imageKeys = FieldsFor(context.Layer).Where(field => field.Kind == "images").Select(Key).ToArray();
                foreach (string key in request.Images.Keys)
                    if (!imageKeys.Contains(key)) throw new ArgumentException("Unsupported image collection: " + key);
                foreach (string key in imageKeys)
                    if (request.Images.TryGetValue(key, out ImageData[]? images)) await ReplaceImages(context.Layer, images);
                if (gridImport != null && cumulusImport != null)
                    throw new ArgumentException("A model save cannot apply two gridded imports at once.");
                if (gridImport != null) SpatialGridService.Import(
                    context.Layer as BoundaryCondition ?? throw new ArgumentException("Grid imports require boundary conditions."),
                    gridImport, dssGridCancellation);
                if (gridImport?.DssSource is { } dssGrid)
                {
                    dssGridCancellation?.ThrowIfCancellationRequested();
                    DssGridImportService.VerifySourceRevision(dssGrid.SourcePath, dssGrid.Revision);
                }
                if (cumulusImport != null)
                {
                    if (cumulusImport.Reporter.CancelRequested) throw new OperationCanceledException("Cumulus grid import was cancelled.");
                    cumulusImport.Apply(context.Layer as BoundaryCondition ?? throw new ArgumentException("Cumulus grids require boundary conditions."));
                    if (cumulusImport.Reporter.CancelRequested) throw new OperationCanceledException("Cumulus grid import was cancelled.");
                }
                if (manning != null && request.Samples?.Any(pair => pair.Value.Length > 0) == true) throw new ArgumentException("Manning layers have no time series.");
                if (classification != null && request.Samples?.Any(pair => pair.Value.Length > 0) == true) throw new ArgumentException("Classification layers have no time series.");
                manning?.VerifyDependencies(); classification?.VerifyDependencies(); terrain?.VerifyDependencies();
                if (classificationImport != null) classificationSelection = ClassificationImportService.Import(context.Layer as ClassificationLayer ?? throw new ArgumentException("This model is not a classification layer."), classificationImport, createdRasters);
                // Prepare must see every captured geometry edit, including
                // culverts and ordinary fields. It runs only on the staging
                // model, so failure never publishes a partially prepared HDF.
                ApplyGeometryMeshPrepare(context, request.AssetPath, request.Revision, request.GeometryMeshState);
                terrain?.PublishPendingRasters(createdRasters);
                if (initialConditions is not null)
                {
                    if (context.Layer is not BoundaryCondition initialConditionBoundary)
                        throw new ArgumentException("Initial conditions can only be attached to a Boundary Condition.");
                    initialConditions.Prepare(initialConditionBoundary, context.Project, staging);
                }
                context.Layer.Save();
                if (gridImport?.DssSource is { } savedDssGrid)
                {
                    dssGridCancellation?.ThrowIfCancellationRequested();
                    DssGridImportService.VerifySourceRevision(savedDssGrid.SourcePath, savedDssGrid.Revision);
                }
                if (cumulusImport?.Reporter.CancelRequested == true)
                    throw new OperationCanceledException("Cumulus grid import was cancelled.");
                if (classificationImport != null) ClassificationImportService.Verify(classificationImport);
                manning?.VerifyDependencies(); classification?.VerifyDependencies(); terrain?.VerifyDependencies();
                boundaryState = boundary?.State(persisted: true);
                if (gridImport?.DssSource is not null)
                    dssCommittedSnapshot = await Snapshot(context, "", request.ProjectPath != null,
                        boundary, manning, classification, terrain);
            }
            if (dssCommittedSnapshot is not null)
            {
                dssCommittedSnapshot = PrepareDssCommittedSnapshot(dssCommittedSnapshot, staging, boundaryState);
                boundaryState = dssCommittedSnapshot.SourceState;
            }
            if (terrainForSave is not null)
            {
                terrainSaveTestHooks?.StagingClosed?.Invoke(staging);
                terrainCommittedSnapshot = await PrepareTerrainCommittedSnapshotAsync(request, staging, terrainSelection);
                terrainSaveTestHooks?.ReceiptPrepared?.Invoke(staging, terrainCommittedSnapshot);
            }
            if (gridImport?.DssSource is { } committedDssGrid)
            {
                dssGridCancellation?.ThrowIfCancellationRequested();
                DssGridImportService.VerifySourceRevision(committedDssGrid.SourcePath, committedDssGrid.Revision);
            }
            VerifyGeometryCulvertSaveDependencies(request);
            if (cumulusImport?.Reporter.CancelRequested == true)
                throw new OperationCanceledException("Cumulus grid import was cancelled.");
            void Publish()
            {
                if (terrainCommittedSnapshot is not null)
                {
                    // The receipt describes these closed HDF bytes and this
                    // exact owning project/session, not an in-memory draft.
                    if (Revision(projectPath) != terrainProjectRevision ||
                        !SourceFilePaths.SameSourceFile(request.AssetPath, original))
                        throw new SourceFileChangedException();
                    if (request.CoreLayerContext is { } terrainCoreContext)
                        CoreLayerImportService.ValidateContext(terrainCoreContext, projectPath);
                    terrainForSave!.VerifyDependencies();
                    if (Revision(staging) != terrainCommittedSnapshot.Revision)
                        throw new SourceFileChangedException();
                }
                // Dependency verification can read an entire DSS file. Check
                // the target after those reads, inside the import commit gate,
                // to reject model changes made during dependency validation.
                if (Revision(original) != request.Revision) throw new SourceFileChangedException();
                if (initialConditions is null) File.Move(staging, original, overwrite: true);
                else initialConditions.Commit(staging);
            }
            if (gridImport?.DssSource is not null)
            {
                if (dssGridCancellation is null)
                    throw new InvalidOperationException("A DSS grid import lost its cancellation context before publication.");
                dssGridCancellation.Publish(Publish);
            }
            else Publish();
            published = true;
            try
            {
                string? terrainCleanupToken = terrainForSave?.ImportDraftToken;
                if (terrainCommittedSnapshot is not null)
                {
                    // No post-publication rebase/load/selection is allowed to
                    // turn this committed save into a retryable failure.
                    terrainSaveTestHooks?.Published?.Invoke();
                    string? warning = terrainForSave!.CleanupCommittedPending();
                    return WithTerrainSaveCleanupWarning(terrainCommittedSnapshot, terrainCleanupToken, warning);
                }
                if (boundaryState != null)
                {
                    var state = JsonSerializer.Deserialize<BoundaryEditorState>(boundaryState, BackendJson.Options)!;
                    boundaryState = JsonSerializer.Serialize(state with { Revision = Revision(original) }, BackendJson.Options);
                }
                CoreLayerRequestContext? committedCoreLayerContext = null;
                if (request.CoreLayerContext is { } capturedCoreLayerContext)
                {
                    string? committedProjectRevision = initialConditions is not null &&
                        request.ProjectPath is { Length: > 0 } committedProjectPath
                        ? Revision(committedProjectPath) : capturedCoreLayerContext.ProjectRevision;
                    committedCoreLayerContext = CoreLayerImportService.RebaseContext(
                        capturedCoreLayerContext, committedProjectRevision, original, Revision(original));
                }
                var result = await LoadAsync(request with
                {
                    SourceState = boundaryState,
                    ProjectXml = initialConditions?.CommittedProjectXml ?? request.ProjectXml,
                    CoreLayerContext = committedCoreLayerContext
                });
                if (classificationSelection is { } index && result.Classification is { } data && index >= 0 && index < data.Inputs.Length)
                {
                    string id = data.Inputs[index].Id;
                    result.Fields[ClassificationEditor.SourceField] = id;
                    result = result with { Classification = data with { SelectedId = id } };
                }
                return result with { CoreLayerContext = committedCoreLayerContext };
            }
            catch (Exception error) when (terrainCommittedSnapshot is not null)
            {
                // Publication already succeeded. Preserve its independently
                // read receipt; keep the import token for explicit cleanup.
                return WithTerrainSaveCleanupWarning(terrainCommittedSnapshot, terrainForSave?.ImportDraftToken,
                    "地形已保存，临时导入数据清理未完成：" + error.Message);
            }
            catch (Exception error) when (gridImport?.DssSource is not null
                && dssCommittedSnapshot is not null && dssGridCancellation is not null)
            {
                dssGridCancellation.RecordRefreshFailure(error);
                return dssCommittedSnapshot;
            }
        }
        finally
        {
            if (File.Exists(staging))
            {
                try { File.Delete(staging); }
                catch (Exception error) when (published && gridImport?.DssSource is not null
                    && dssCommittedSnapshot is not null && dssGridCancellation is not null)
                { dssGridCancellation.RecordRefreshFailure(error); }
            }
            if (!published) foreach (string raster in createdRasters) if (File.Exists(raster)) File.Delete(raster);
        }
    }

    private static void ApplyFields(ModelContext context, BoundaryEditorService? boundary, Dictionary<string, string> values)
    {
        var changes = values.Where(pair => boundary?.ContainsField(pair.Key) != true && !(context.Layer is NValueLayer && ManningEditor.ContainsField(pair.Key)) && !(context.Layer is ClassificationLayer && ClassificationEditor.ContainsField(pair.Key)) && !(context.Layer is Terrain && TerrainEditor.ContainsField(pair.Key))).Select(pair => {
                    Field field = FieldsFor(context.Layer).SingleOrDefault(field => Key(field) == pair.Key)
                        ?? throw new ArgumentException("Unsupported model field: " + pair.Key);
                    PropertyInfo property = Property(field, context);
                    if (field.ReadOnly || property.SetMethod?.IsPublic != true || field.Kind == "images")
                        throw new ArgumentException("The model field is read-only: " + pair.Key);
                    object value = Parse(pair.Value, property.PropertyType, field.AllowZero);
                    if (field.Scope == "EvapotranspirationControl" && property.Name == "SpatialDataType" && value.ToString() == "Gridded")
                        throw new ValidationException("Evapotranspiration supports constant or point data.");
                    var validation = new ValidationContext(Target(field, context)) { MemberName = property.Name };
                    Validator.ValidateValue(value, validation, property.GetCustomAttributes<ValidationAttribute>());
                    return (field, property, value);
                }).OrderBy(change => WriteOrder(change.property.Name)).ToArray();

                foreach (var change in changes) change.property.SetValue(Target(change.field, context), change.value);
        if (boundary != null)
            foreach (var pair in values.Where(pair => boundary.ContainsField(pair.Key)).OrderBy(pair => pair.Key.EndsWith("TimeSeriesDefinition_UseFixedStartTime", StringComparison.Ordinal) ? 0 : 1))
                boundary.SetField(pair.Key, pair.Value);
        foreach (var change in changes)
            if (!Equals(change.property.GetValue(Target(change.field, context)), change.value))
                throw new ValidationException("The current model settings do not accept " + change.property.Name + ".");
        boundary?.VerifyFields(values);
    }

    public static async Task<ModelSnapshot> TransformSeriesAsync(SeriesTransformRequest request)
    {
        ModelSaveRequest model = request.Model;
        VerifyKind(model);
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        using var context = new ModelContext(model);
        var boundary = Boundary(context, model.AssetPath, model.Revision, model.SourceState) ?? throw new NotSupportedException("This model has no boundary series.");
        ApplyFields(context, boundary, model.Fields); boundary.ApplyData(model.Rows, model.Samples);
        boundary.Transform(request.PageId, request.Operation);
        var result = await Snapshot(context, model.Revision, model.ProjectPath != null, boundary);
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        return result;
    }
    private static BoundaryEditorService? Boundary(ModelContext context, string assetPath, string revision, string? state) => context.Layer is BoundaryCondition boundary
        ? new(boundary, (ObservableBoundaryCondition)context.Observable, assetPath, revision, state) : state == null || context.Layer is NValueLayer or ClassificationLayer or Terrain or Plan ? null : throw new ArgumentException("The source state belongs to a boundary editor.");

    public static async Task<ModelSnapshot> TransformBoundaryAsync(BoundaryTransformRequest request)
    {
        ModelSaveRequest model = request.Model;
        VerifyKind(model);
        if (string.IsNullOrEmpty(model.ProjectPath)) throw new ArgumentException("Editing payloads requires the owning project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        using var context = new ModelContext(model);
        var boundary = Boundary(context, model.AssetPath, model.Revision, model.SourceState) ?? throw new NotSupportedException("This model has no boundary payloads.");
        ApplyFields(context, boundary, model.Fields); boundary.ApplyData(model.Rows, model.Samples);
        boundary.MutatePayload(request.LineId, request.Operation, request.PayloadId, request.Fields);
        var result = await Snapshot(context, model.Revision, true, boundary);
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        return result;
    }

    /// <summary>
    /// Rebuilds a boundary editor snapshot against the supplied current project
    /// context while replaying its captured draft. This operation is read-only:
    /// it never mutates payload structure or writes the project/model files.
    /// </summary>
    public static async Task<ModelSnapshot> RefreshBoundaryAsync(ModelSaveRequest request)
    {
        VerifyKind(request);
        if (request.Kind != "Boundary Conditions")
            throw new ArgumentException("Boundary refresh requires a Boundary Condition source.");
        if (string.IsNullOrEmpty(request.ProjectPath))
            throw new ArgumentException("Refreshing boundary choices requires the owning project.");
        if (string.IsNullOrWhiteSpace(request.SourceState) || request.Fields is null ||
            request.Rows is null || request.Samples is null)
            throw new ArgumentException("Boundary refresh requires the captured source state and complete field, row, and sample drafts.");
        if (Revision(request.AssetPath) != request.Revision) throw new SourceFileChangedException();

        using var context = new ModelContext(request);
        var boundary = Boundary(context, request.AssetPath, request.Revision, request.SourceState)
            ?? throw new NotSupportedException("This model has no boundary payloads.");
        ApplyFields(context, boundary, request.Fields);
        boundary.ApplyData(request.Rows, request.Samples);

        ModelSnapshot result = await Snapshot(context, request.Revision, true, boundary);
        if (Revision(request.AssetPath) != request.Revision) throw new SourceFileChangedException();
        return result with { CoreLayerContext = request.CoreLayerContext };
    }

    public static async Task<ModelSnapshot> TransformSpatialAsync(SpatialTransformRequest request)
    {
        ModelSaveRequest model = request.Model;
        VerifyKind(model);
        if (string.IsNullOrEmpty(model.ProjectPath)) throw new ArgumentException("Editing spatial inputs requires the owning project.");
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        using var context = new ModelContext(model);
        var boundary = Boundary(context, model.AssetPath, model.Revision, model.SourceState) ?? throw new NotSupportedException("This model has no spatial inputs.");
        ApplyFields(context, boundary, model.Fields); boundary.ApplyData(model.Rows, model.Samples);
        boundary.MutateSpatial(request.LayerId, request.Operation, request.ItemId);
        var result = await Snapshot(context, model.Revision, true, boundary);
        if (Revision(model.AssetPath) != model.Revision) throw new SourceFileChangedException();
        return result;
    }

    private static async Task<ModelSnapshot> Snapshot(ModelContext context, string revision, bool hasProject, BoundaryEditorService? boundary, ManningEditor? manning = null, ClassificationEditor? classification = null, TerrainEditor? terrain = null)
    {
        Field[] fields = FieldsFor(context.Layer);
        Dictionary<string, string> values = [];
        foreach (Field field in fields)
        {
            if (field.Kind != "images") values[Key(field)] = Format(Property(field, context).GetValue(Target(field, context)));
            foreach (string gate in field.EnabledBy.Where(route => route.StartsWith("ViewModel_SelectedPlan_", StringComparison.Ordinal)))
            {
                var property = context.Observable.GetType().GetProperty(gate["ViewModel_SelectedPlan_".Length..])
                    ?? throw new InvalidOperationException("Missing source model gate: " + gate);
                values[field.Scope + "/" + gate] = Format(property.GetValue(context.Observable));
            }
        }
        Dictionary<string, ImageData[]> collections = [];
        foreach (Field field in fields.Where(field => field.Kind == "images"))
        {
            collections[Key(field)] = await Task.WhenAll(context.Layer.EmbeddedImages.Images.Select(async image =>
                new ImageData(image.H5Dataset, image.Name,
                    await image.TryLoadImageAsync() ?? throw new IOException("Cannot read embedded image " + image.Name))));
        }
        var groups = fields.Where(field => field.Kind != "images").GroupBy(field => SpatialPrefix(field) + PropertyName(field))
            .Where(group => group.Count() > 1).ToDictionary(group => group.Key, group => group.Select(Key).ToArray());
        if (boundary != null)
        {
            foreach (var pair in boundary.Fields()) values[pair.Key] = pair.Value;
            foreach (var pair in boundary.Groups()) groups[pair.Key] = pair.Value;
        }
        if (manning != null) foreach (var pair in manning.Fields()) values[pair.Key] = pair.Value;
        if (classification != null)
        {
            foreach (var pair in classification.Fields()) values[pair.Key] = pair.Value;
            foreach (var pair in classification.Groups()) groups[pair.Key] = pair.Value;
        }
        if (terrain != null)
        {
            foreach (var pair in terrain.Fields()) values[pair.Key] = pair.Value;
            foreach (var pair in terrain.Groups()) groups[pair.Key] = pair.Value;
        }
        manning?.VerifyDependencies(); classification?.VerifyDependencies(); terrain?.VerifyDependencies();
        string? sourceState = context.Layer is Plan
            ? SerializePlanTimeWindowDraft(context, context.Layer.Filename, revision)
            : terrain?.State() ?? classification?.State() ?? manning?.State() ?? boundary?.State();
        PlanTimeWindowFieldIDs? planTimeWindowFieldIDs = context.Layer is Plan ? GetPlanTimeWindowFieldIDs(context) : null;
        Dictionary<string, SourceEditorChoice[]> fieldChoices = PlanFieldChoices(context.Layer, fields);
        Dictionary<string, string> fieldTitles = PlanFieldTitles(context, fields);
        return new ModelSnapshot(revision, values, collections, hasProject, groups,
            manning?.CanSetDefaults == true ? ["setDefaultNValues"]
                : hasProject && context.Layer is Plan { IsResult: false } ? ["compute"] : [], boundary?.Snapshot(), sourceState, terrain?.ReadOnlyFields() ?? boundary?.ReadOnlyFields(),
            context.Layer is BoundaryCondition condition ? SpatialGridService.Overviews(condition) : null, manning?.Snapshot(), classification?.Snapshot(), terrain?.Snapshot(), planTimeWindowFieldIDs,
            FieldChoices: fieldChoices, FieldTitles: fieldTitles);
    }

    private static Dictionary<string, SourceEditorChoice[]> PlanFieldChoices(CoreLayerBase layer, Field[] fields)
    {
        const string solverField = "PlanSettingsPage/ViewModel_SelectedPlan_SolverType";
        if (layer is not Plan || !fields.Any(field => Key(field) == solverField)) return [];

        // PlanSettingsPage.GetSolverTypeDescription uses this retained source
        // probe for every solver type. It catches unavailable CUDA assemblies or
        // driver errors and returns an empty device name in those cases.
        string compatibleGpu = GPUSolver.TryFindCompatibleGPU();
        string gpuDescription = string.IsNullOrEmpty(compatibleGpu)
            ? "No NVIDIA GPU detected. You can try running the GPU solver to check for driver errors"
            : "Use the explicit GPU solver with device: " + compatibleGpu;
        SourceEditorChoice[] choices = Enum.GetValues<SolverType>().Select(solver => solver switch
        {
            SolverType.CPU => new SourceEditorChoice("CPU", "CPU", false, false, "CPUColored1", "Use the explicit CPU solver"),
            SolverType.GPU => new SourceEditorChoice("GPU", "GPU", false, false, "GPUColored", gpuDescription),
            _ => throw new NotSupportedException("The source PlanSettingsPage does not describe solver type " + solver + ".")
        }).ToArray();
        return new Dictionary<string, SourceEditorChoice[]>(StringComparer.Ordinal) { [solverField] = choices };
    }

    private static Dictionary<string, string> PlanFieldTitles(ModelContext context, Field[] fields)
    {
        if (context.Layer is not Plan) return [];
        string unit = context.Project.Units == Utility.Units.UnitSystem.USCustomary ? "ft" : "m";
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ViewModel_SelectedPlan_PartialGridElevationTolerance"] = "Partial Grid Elevation Tolerance",
            ["ViewModel_SelectedPlan_VolumeTolerance"] = "Volume Tolerance",
            ["ViewModel_SelectedPlan_VolumeMaxTolerance"] = "Volume Maximum Tolerance",
            ["ViewModel_SelectedPlan_VolumeRMSTolerance"] = "Volume RMS Tolerance",
            ["ViewModel_SelectedPlan_WaterSurfaceTolerance"] = "Water Surface Tolerance",
            ["ViewModel_SelectedPlan_WaterSurfaceMaxTolerance"] = "Water Surface Maximum Tolerance",
            ["ViewModel_SelectedPlan_WaterSurfaceRMSTolerance"] = "Water Surface RMS Tolerance"
        };
        return fields.Where(field => field.Scope == "PlanComputeSettingsPage" && labels.ContainsKey(field.Id))
            .ToDictionary(Key, field => labels[field.Id] + " (" + unit + ")", StringComparer.Ordinal);
    }

    private static async Task ReplaceImages(CoreLayerBase layer, ImageData[] requested)
    {
        if (requested.Select(image => image.Id).Distinct().Count() != requested.Length)
            throw new ArgumentException("Image identities must be unique.");
        var existing = layer.EmbeddedImages.Images.ToArray();
        var retained = new HashSet<EmbeddedImage>();
        var selected = new List<(ImageData request, EmbeddedImage? source)>();
        foreach (ImageData image in requested)
        {
            if (image.Data.Length == 0) throw new ArgumentException("Image data must not be empty.");
            EmbeddedImage? source = existing.SingleOrDefault(source => source.H5Dataset == image.Id);
            if (source != null)
            {
                if (source.Name != image.Name || !(await source.TryLoadImageAsync()).SequenceEqual(image.Data))
                    throw new ArgumentException("Existing image content cannot be replaced under another identity.");
            }
            else
            {
                // A newly added UI image still has its draft identity until the next
                // load. Recognize saved bytes without replacing retained metadata.
                foreach (EmbeddedImage candidate in existing.Where(source => !retained.Contains(source)))
                    if ((await candidate.TryLoadImageAsync()).SequenceEqual(image.Data))
                    { source = candidate; break; }
            }
            if (source != null && !retained.Add(source)) throw new ArgumentException("An embedded image cannot appear twice under the same source identity.");
            selected.Add((image, source));
        }
        layer.UseWriter(writer =>
        {
            foreach (EmbeddedImage image in existing.Where(image => !retained.Contains(image))) writer.Delete(image.H5Dataset);
        });
        layer.EmbeddedImages.Images.Clear();
        foreach (var (image, source) in selected)
        {
            if (source != null)
            {
                layer.EmbeddedImages.Images.Add(source);
                continue;
            }
            string name = Path.GetFileName(image.Name);
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Missing image name.");
            string unique = name;
            for (int suffix = 2; layer.EmbeddedImages.Images.Any(existing => existing.Name == unique); suffix++) unique = name + " (" + suffix + ")";
            layer.EmbeddedImages.Add(unique, "", image.Data);
        }
    }

    private static int WriteOrder(string property) => property switch
    {
        "ConstantValue" or "ConstantVx" or "ConstantVy" => 0, "SpatialDataType" => 1, "IsEnabled" => 20,
        "TimeWindowMode" => 0, "EquationSet" => 1, "SolverType" => 2, "TimeStepping" => 3,
        "ComputeStartTime" => 4, "ComputeEndTime" => 5, _ => 10
    };
    private static Schema[] LoadSchemas()
    {
        using Stream stream = typeof(ModelEditorAdapter).Assembly.GetManifestResourceStream("editor-schemas.json")!;
        var library = JsonSerializer.Deserialize<Library>(stream, BackendJson.Options) ?? throw new InvalidOperationException("Missing source UI contract.");
        return library.Schemas;
    }
    private static string Key(Field field) => field.Scope + "/" + field.Id;
    // These prefixes are the retained source view/observable boundaries. They are
    // explicit per schema; they never expose arbitrary reflection routes to clients.
    private static readonly string[] LayerPrefixes = ["Context_Layer_", "Terrain_Terrain_", "ClassificationLayer_ClassificationLayer_"];
    private static readonly string[] ObservablePrefixes = ["ViewModel_SelectedPlan_", "ViewModel_SelectedGeometryVisualContext_Context_", "Context_", "BoundaryCondition_", "Terrain_", "ClassificationLayer_"];
    private static readonly string[] SpatialPrefixes = ["PrecipitationLayer_", "EvapotranspirationLayer_", "WindLayer_", "AirPressureLayer_", "AirDensityLayer_"];
    private static string SpatialPrefix(Field field) => field.Scope == "WindConstantControl" ? "WindLayer_" : SpatialPrefixes.FirstOrDefault(prefix => field.Id.StartsWith(prefix, StringComparison.Ordinal)) ?? "";
    private static string PropertyName(Field field)
    {
        if (field.Scope == "WindConstantControl") return field.Id switch { "Constant1" => "ConstantVx", "Constant2" => "ConstantVy", _ => throw new ArgumentException("Unknown wind component: " + field.Id) };
        string prefix = LayerPrefixes.Concat(ObservablePrefixes).Concat(SpatialPrefixes).FirstOrDefault(prefix => field.Id.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Unmapped source model binding: " + Key(field));
        return field.Id[prefix.Length..];
    }
    private static object Target(Field field, ModelContext context)
    {
        string spatial = SpatialPrefix(field);
        if (spatial.Length > 0)
        {
            if (context.Observable is not ObservableBoundaryCondition boundary) throw new ArgumentException("Spatial fields require a boundary condition.");
            return spatial switch {
                "PrecipitationLayer_" => boundary.PrecipitationLayer, "EvapotranspirationLayer_" => boundary.EvapotranspirationLayer,
                "WindLayer_" => boundary.WindLayer, "AirPressureLayer_" => boundary.AirPressure, "AirDensityLayer_" => boundary.AirDensity,
                _ => throw new ArgumentException("Unknown spatial layer.")
            };
        }
        return LayerPrefixes.Any(prefix => field.Id.StartsWith(prefix, StringComparison.Ordinal)) ? context.Layer : context.Observable;
    }
    private static PropertyInfo Property(Field field, ModelContext context) => Target(field, context).GetType().GetProperty(PropertyName(field))
        ?? throw new InvalidOperationException("Missing source model property: " + Key(field));
    private static string Format(object? value) => value switch
    {
        null => "", bool flag => flag ? "true" : "false",
        TimeSpan duration => duration.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
        DateTime date => DateTime.SpecifyKind(date, DateTimeKind.Local).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? ""
    };
    private static object Parse(string value, Type type, bool allowZero)
    {
        if (type == typeof(string)) return value;
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(DateTime)) return DateTime.SpecifyKind(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).LocalDateTime, DateTimeKind.Unspecified);
        if (type.IsEnum)
        {
            object result = Enum.Parse(type, value);
            return Enum.IsDefined(type, result) ? result : throw new ArgumentException("Invalid " + type.Name + " value.");
        }
        if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
        double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number)) throw new ArgumentException("Numeric values must be finite.");
        if (type == typeof(float))
        {
            float valueAsFloat = (float)number;
            if (!float.IsFinite(valueAsFloat)) throw new ArgumentException("Numeric values exceed the source model range.");
            return valueAsFloat;
        }
        if (type == typeof(TimeSpan))
        {
            if (allowZero ? number < 0 : number <= 0) throw new ArgumentException(allowZero ? "Interval value must not be negative." : "Interval value must be positive.");
            return TimeSpan.FromSeconds(number);
        }
        return type == typeof(double) ? number : throw new ArgumentException("Unsupported model property type: " + type.Name);
    }
    private static void VerifyKind(ModelRequest request)
    {
        if (request.Kind is not ("Plan" or "Geometry" or "Terrain" or "Boundary Conditions" or "Surface Layers")) throw new NotSupportedException("This backend adapter does not yet support " + request.Kind + ".");
    }
    internal static ModelSnapshot PrepareDssCommittedSnapshot(ModelSnapshot snapshot, string stagedPath,
        string? boundaryState)
    {
        string revision = Revision(stagedPath);
        if (boundaryState is not null)
        {
            BoundaryEditorState state = JsonSerializer.Deserialize<BoundaryEditorState>(boundaryState, BackendJson.Options)!;
            boundaryState = JsonSerializer.Serialize(state with { Revision = revision }, BackendJson.Options);
        }
        return snapshot with { Revision = revision, SourceState = boundaryState ?? snapshot.SourceState };
    }

    private static string Revision(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static string RevisionForInitialConditions(string path) => Revision(path);

    private sealed class ModelContext : IDisposable
    {
        private readonly Project project;
        private readonly ObservableProject observableProject;
        private readonly bool ownsProject = true;
        public Project Project => project;
        public CoreLayerBase Layer { get; }
        public ObservableCoreLayer Observable { get; }
        public string SourceFilename { get; }
        public ModelContext(ModelRequest request, string? staging = null)
        {
            if (request.ProjectPath is not null)
                project = CoreLayerImportService.OpenProject(request.ProjectPath, request.ProjectXml,
                    request.CoreLayerContext);
            else
            {
                if (request.CoreLayerContext is { References.Length: > 0 })
                    throw new ArgumentException("A session-only core-layer reference requires its saved project.");
                project = Project.CreateInMemory();
            }
            try
            {
                string source = Path.GetFullPath(request.AssetPath);
                var original = CoreLayers().FirstOrDefault(layer => SourceFilePaths.SameSourceFile(layer.Filename, source));
                SourceFilename = original?.Filename ?? source;
                if (request.CoreLayerContext is not null && request.ProjectPath is not null &&
                    staging is null && original is null && request.Kind is ("Geometry" or "Boundary Conditions"))
                    throw new ArgumentException("The Geometry or Boundary Condition is not part of this project session. Add an explicit session-only reference first.");
                if (request.CoreLayerContext is not null && request.ProjectPath is not null &&
                    staging is null && original is null && request.Kind is ("Plan" or "Terrain"))
                {
                    string expectedKind = request.Kind == "Plan" ? "plan" : "terrain";
                    bool capturedSessionHdf = (request.CoreLayerContext.SessionHdfReferences ?? []).Any(reference =>
                        reference.Kind == expectedKind &&
                        SourceFilePaths.SameSourceFile(reference.CanonicalSourcePath, source));
                    if (!capturedSessionHdf)
                        throw new ArgumentException("The Plan or Terrain is not part of this project session. Add its exact HDF source to the current session first.");
                }
                CoreLayerBase layer = staging == null ? original ?? OpenLayer(request.Kind, source) : OpenLayer(request.Kind, staging);
                if (SupportedKind(layer) != request.Kind) throw new ArgumentException("The requested editor does not match the source model.");
                if (original == null || staging != null)
                {
                    if (staging != null) layer.OverrideName(original?.Name ?? Path.GetFileNameWithoutExtension(source));
                    switch (layer)
                    {
                        case Plan plan:
                            project.AddPlan(plan);
                            if (original is Plan originalPlan)
                            {
                                var associations = project.GetPlanAssociation(originalPlan);
                                var copy = project.GetPlanAssociation(plan);
                                copy.Geometry = associations.Geometry; copy.BoundaryCondition = associations.BoundaryCondition;
                            }
                            break;
                        case Geometry geometry:
                            project.AddGeometry(geometry);
                            if (original is Geometry originalGeometry)
                            {
                                var associations = project.GetGeometryAssociation(originalGeometry);
                                var copy = project.GetGeometryAssociation(geometry);
                                copy.Terrain = associations.Terrain; copy.NValueLayer = associations.NValueLayer;
                            }
                            break;
                        case Terrain terrain: project.AddTerrain(terrain); break;
                        case BoundaryCondition boundary: project.AddBoundaryCondition(boundary); break;
                        case NValueLayer nvalues: project.AddNValueLayer(nvalues); break;
                        case ClassificationLayer classification: project.AddClassification(classification); break;
                    }
                }
                if (layer.HasLoadErrors) throw new IOException(string.Join("\n", layer.LayerErrors.Select(error => error.EvaluatedMessage)));
                Layer = layer;
                observableProject = new ObservableProject(project, Utility.SynchronizationContext.None);
                Observable = ObservableLayers()
                    .Single(item => ReferenceEquals(item.Layer, layer));
            }
            catch { Dispose(); throw; }
        }
        private ModelContext(ModelContext owner, CoreLayerBase layer)
        {
            project = owner.project;
            observableProject = owner.observableProject;
            ownsProject = false;
            Layer = layer;
            SourceFilename = layer.Filename;
            Observable = ObservableLayers().Single(item => ReferenceEquals(item.Layer, layer));
        }
        public ModelContext ForAssociatedLayer(string kind, string canonicalAssetPath)
        {
            if (kind is not ("Geometry" or "Boundary Conditions")
                || string.IsNullOrWhiteSpace(canonicalAssetPath) || !Path.IsPathFullyQualified(canonicalAssetPath))
                throw new ArgumentException("Choose the exact associated geometry or boundary condition.");
            CoreLayerBase layer = CoreLayers().SingleOrDefault(candidate => SupportedKind(candidate) == kind
                && SourceFilePaths.SameSourceFile(candidate.Filename, canonicalAssetPath))
                ?? throw new ArgumentException("The associated model does not belong to the current project.");
            if (layer.HasLoadErrors)
                throw new IOException(string.Join("\n", layer.LayerErrors.Select(error => error.EvaluatedMessage)));
            // This view shares the Plan's actual object graph. Disposing it must
            // never dispose its owner's project or save an associated layer.
            return new ModelContext(this, layer);
        }
        private IEnumerable<ObservableCoreLayer> ObservableLayers() => observableProject.Plans.Cast<ObservableCoreLayer>()
            .Concat(observableProject.Geometries).Concat(observableProject.Terrains)
            .Concat(observableProject.BoundaryConditions).Concat(observableProject.NValues)
            .Concat(observableProject.Classifications);
        private CoreLayerBase OpenLayer(string kind, string filename) => kind switch
        {
            "Plan" => new Plan(filename, project), "Geometry" => new Geometry(filename, project),
            "Terrain" => new Terrain(filename, project), "Boundary Conditions" => new BoundaryCondition(filename, project),
            "Surface Layers" => OpenSurface(filename), _ => throw new NotSupportedException("Unsupported model editor: " + kind)
        };
        private CoreLayerBase OpenSurface(string filename)
        {
            using var reader = new H5Reader(filename);
            if (reader.ExistsAsGroup(NValueLayer.HDF5Group)) return new NValueLayer(filename, project);
            if (reader.ExistsAsGroup(ClassificationLayer.HDF5Group)) return new ClassificationLayer(filename, project);
            throw new ArgumentException("The file is not an original surface layer.");
        }
        private static string SupportedKind(CoreLayerBase layer) => layer switch
        {
            Plan => "Plan", Geometry => "Geometry", Terrain => "Terrain", BoundaryCondition => "Boundary Conditions",
            NValueLayer or ClassificationLayer => "Surface Layers", _ => ""
        };
        private IEnumerable<CoreLayerBase> CoreLayers() => project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
            .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers);
        public void Dispose()
        {
            if (!ownsProject) return;
            foreach (var layer in CoreLayers().Cast<IDisposable>().Concat(project.Results)) layer.Dispose();
        }
    }
}

using System.Globalization;
using System.Text.Json;
using Geospatial.Rasters.Classifications;
using OSGeo.GDAL;
using Ras.Layers;
using Ras.Layers.Classifiers;
using Ras.Runtime;
using Utility.Units;

namespace Ras.Backend.ServerChecks;

internal sealed record ClassificationManningAcceptanceReport(
    string State,
    string FixtureDirectory,
    string ManifestPath,
    string ProjectPath,
    string ProjectRevision,
    string ClassificationPath,
    string ClassificationInitialRevision,
    string ClassificationSavedRevision,
    string NValuePath,
    string NValueInitialRevision,
    string NValueSavedRevision,
    string ClassificationRasterPath,
    string ClassificationRasterRevision,
    bool ClassificationMappingValueRoundTrip,
    bool MappingInsertRemoveAndKeyMutationRejected,
    bool PolygonUndoRedoReadback,
    bool ManningNLCDDefaultsReadback,
    bool NValuePreviewReadback,
    bool SourceHdfUnchangedBeforeSave,
    bool SourceRasterUnchanged,
    bool ClassificationSavedReadback,
    bool ManningSavedReadback);

/// <summary>
/// Exercises classification mapping and polygon draft operations plus the real
/// Manning editor and preview APIs against a newly created, isolated project.
/// Only the supplied output directory is modified.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string ManningTable = "ClassificationLayerManningNEditor/InterpretationTable";
    private static readonly HashSet<string> HostReadOnlyClassificationCatalogFields = new(StringComparer.Ordinal)
    {
        "ClassificationCatalogSummary/Context_Name",
        "ClassificationCatalogSummary/Context_Filename",
        "ClassificationCatalogSummary/Context_Layer_DateCreated",
        "ClassificationCatalogSummary/Context_Layer_DateLastModified",
        "ClassificationLayerSummary/ClassificationLayer_Name",
        "ClassificationLayerSummary/ClassificationLayer_ClassificationLayer_DateCreated",
        "ClassificationLayerSummary/ClassificationLayer_ClassificationLayer_DateLastModified",
        "ClassificationLayerSummary/ClassificationLayer_ClassificationLayer_LoadedFileVersion",
        "ClassificationLayerSummary/ClassificationLayer_ClassificationLayer_LoadedSoftwareVersion",
        "ClassificationLayerSummary/ClassificationLayer_ClassificationLayer_Description"
    };

    public static async Task<ClassificationManningAcceptanceReport> RunClassificationManningAcceptanceAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Classification/Manning acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Classification/Manning acceptance requires a new output directory; existing data is never overwritten.");
        string fixtureDirectory = Path.Combine(outputDirectory, "project-fixture");
        Directory.CreateDirectory(fixtureDirectory);
        ClassificationManningFixture fixture = CreateFixture(fixtureDirectory);
        string manifestPath = Path.Combine(outputDirectory, "classification-manning-manifest.json");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        string projectXml = await File.ReadAllTextAsync(fixture.ProjectPath, cancellation);
        string projectRevision = FileSHA256(fixture.ProjectPath);
        ModelSnapshot classification = await LoadSurfaceAsync(client, fixture.ClassificationPath,
            fixture.ProjectPath, projectXml, cancellation);
        ModelSnapshot nvalue = await LoadSurfaceAsync(client, fixture.NValuePath,
            fixture.ProjectPath, projectXml, cancellation);
        string initialClassificationRevision = classification.Revision;
        string initialNValueRevision = nvalue.Revision;
        string initialClassificationSHA = FileSHA256(fixture.ClassificationPath);
        string initialNValueSHA = FileSHA256(fixture.NValuePath);

        ClassificationInputData rasterInput = classification.Classification?.Inputs
            .SingleOrDefault(input => input.Type == "Raster")
            ?? throw new InvalidDataException("The fixture classification model did not load its real raster mapping input.");
        if (string.IsNullOrEmpty(rasterInput.TableId) || rasterInput.Rows.Length < 3)
            throw new InvalidDataException("The classification fixture did not expose its expected source mapping rows.");
        ModelRow[] changedMapping = rasterInput.Rows.Select(row =>
        {
            Dictionary<string, string> values = new(row.Values, StringComparer.Ordinal);
            if (values.TryGetValue("Value", out string? value) && value == "Editable class")
                values["Value"] = "Edited class";
            return row with { Values = values };
        }).ToArray();
        if (!changedMapping.Any(row => row.Values.GetValueOrDefault("Value") == "Edited class"))
            throw new InvalidDataException("The fixture did not retain the editable raster classification value.");

        Dictionary<string, ModelRow[]> mappingRows = ClassificationRows(classification);
        mappingRows[rasterInput.TableId!] = changedMapping;
        ModelSnapshot mappedDraft = await TransformClassificationAsync(client, classification,
            fixture.ClassificationPath, fixture.ProjectPath, projectXml, mappingRows, "refresh", null, cancellation);
        ModelRow mappedRow = FindClassificationInput(mappedDraft, rasterInput.Id).Rows
            .Single(row => row.Values.GetValueOrDefault("Value") == "Edited class");
        ModelRow originalIdentityRow = changedMapping.Single(row => row.Values.GetValueOrDefault("Value") == "Edited class");
        bool mappingValueRoundTrip = mappedRow.Id == originalIdentityRow.Id
            && mappedRow.Values.GetValueOrDefault("Key") == originalIdentityRow.Values.GetValueOrDefault("Key");
        if (!mappingValueRoundTrip)
            throw new InvalidDataException("The classification mapping transform did not return the edited value with its original key.");

        // The command's rows are part of the ModelSaveRequest, so build invalid
        // candidates explicitly rather than relying on a UI-only guard.
        bool keyMutationRejected = await RejectClassificationRowsAsync(client, mappedDraft, fixture,
            projectXml, invalidKind: "key", cancellation);
        bool insertionRejected = await RejectClassificationRowsAsync(client, mappedDraft, fixture,
            projectXml, invalidKind: "insert", cancellation);
        bool removalRejected = await RejectClassificationRowsAsync(client, mappedDraft, fixture,
            projectXml, invalidKind: "remove", cancellation);
        bool mappingStructureRejected = keyMutationRejected && insertionRejected && removalRejected;

        ModelSnapshot createdPolygon = await TransformClassificationGeometryAsync(client, mappedDraft,
            fixture.ClassificationPath, fixture.ProjectPath, projectXml, "create", cancellation);
        int polygonCountAfterCreate = ClassificationPolygons(createdPolygon).Sum(input => input.Polygons.Length);
        ModelSnapshot undonePolygon = await TransformClassificationGeometryAsync(client, createdPolygon,
            fixture.ClassificationPath, fixture.ProjectPath, projectXml, "undo", cancellation);
        int polygonCountAfterUndo = ClassificationPolygons(undonePolygon).Sum(input => input.Polygons.Length);
        ModelSnapshot redonePolygon = await TransformClassificationGeometryAsync(client, undonePolygon,
            fixture.ClassificationPath, fixture.ProjectPath, projectXml, "redo", cancellation);
        int polygonCountAfterRedo = ClassificationPolygons(redonePolygon).Sum(input => input.Polygons.Length);
        bool polygonUndoRedoReadback = polygonCountAfterCreate == 1 && polygonCountAfterUndo == 0
            && polygonCountAfterRedo == 1 && redonePolygon.Classification?.CanUndoGeometry == true;
        if (!polygonUndoRedoReadback)
            throw new InvalidDataException("Classification polygon create/undo/redo did not round-trip its source draft state.");

        bool staleRevisionRejected = await OperationRejectedAsync(client, "model.classificationTransform",
            new ClassificationTransformRequest(CreateModelRequest(mappedDraft, fixture.ClassificationPath,
                fixture.ProjectPath, projectXml, ClassificationRows(mappedDraft)) with { Revision = "stale-classification-revision" }, "refresh"), cancellation);
        if (!staleRevisionRejected)
            throw new InvalidDataException("The classification transform accepted a stale source revision.");

        bool sourceHdfUnchangedBeforeSave = FileSHA256(fixture.ClassificationPath) == initialClassificationSHA
            && FileSHA256(fixture.NValuePath) == initialNValueSHA;
        bool sourceRasterUnchanged = FileSHA256(fixture.ClassificationRasterPath) == fixture.ClassificationRasterRevision;
        if (!sourceHdfUnchangedBeforeSave || !sourceRasterUnchanged)
            throw new InvalidDataException("Classification or Manning draft operations changed source files before the parent save.");

        JsonElement savedClassificationJson = (await SubmitOperationAsync(client, "model.save",
            CreateModelRequest(redonePolygon, fixture.ClassificationPath, fixture.ProjectPath,
                projectXml, ClassificationRows(redonePolygon)), cancellation)).Result;
        ModelSnapshot savedClassification = DeserializeModelSnapshot(savedClassificationJson);
        string classificationSavedSHA = FileSHA256(fixture.ClassificationPath);
        if (!string.Equals(savedClassification.Revision, classificationSavedSHA, StringComparison.OrdinalIgnoreCase)
            || string.Equals(classificationSavedSHA, initialClassificationSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The classification save did not produce a new source HDF revision matching its receipt.");

        ModelSnapshot reopenedClassification = await LoadSurfaceAsync(client, fixture.ClassificationPath,
            fixture.ProjectPath, projectXml, cancellation);
        ClassificationInputData reopenedRaster = reopenedClassification.Classification?.Inputs
            .SingleOrDefault(input => input.Type == "Raster" && input.Name == "验收分类栅格")
            ?? throw new InvalidDataException("Reopening the saved classification layer did not preserve its raster input.");
        bool classificationSavedReadback = reopenedRaster.Rows
            .Any(row => row.Values.GetValueOrDefault("Value") == "Edited class")
            && ClassificationPolygons(reopenedClassification).Sum(input => input.Polygons.Length) == 1;
        if (!classificationSavedReadback)
            throw new InvalidDataException("Reopening the saved classification layer did not recover its mapping edit and polygon.");

        string nvalueProjectRevision = FileSHA256(fixture.ProjectPath);
        JsonElement preview = (await SubmitOperationAsync(client, "nvalue.preview", new NValuePreviewRequest(
            fixture.ProjectPath, fixture.NValuePath, nvalueProjectRevision, initialNValueSHA), cancellation)).Result;
        bool nvaluePreviewReadback = StringProperty(preview, "mode") == "ClassificationLayer"
            && PropertyOrNull(preview, "raster").ValueKind == JsonValueKind.Object
            && PropertyOrNull(preview, "histogram").ValueKind == JsonValueKind.Object;
        if (!nvaluePreviewReadback)
            throw new InvalidDataException("The real N-value preview did not return its associated classification raster and histogram.");

        nvalue = await LoadSurfaceAsync(client, fixture.NValuePath, fixture.ProjectPath,
            projectXml, cancellation);
        JsonElement transformedManningJson = (await SubmitOperationAsync(client, "model.manningTransform",
            new ManningTransformRequest(CreateModelRequest(nvalue, fixture.NValuePath,
                fixture.ProjectPath, projectXml, ManningRows(nvalue)), "setNLCD"), cancellation)).Result;
        ModelSnapshot manningDraft = DeserializeModelSnapshot(transformedManningJson);
        Dictionary<string, string> manningValues = ManningValues(manningDraft);
        bool manningDefaultsReadback = manningValues.GetValueOrDefault("Open Water") == "0.035"
            && manningValues.GetValueOrDefault("Deciduous Forest") == "0.15"
            && manningValues.GetValueOrDefault("Custom land") == "0.333";
        if (!manningDefaultsReadback)
            throw new InvalidDataException("The Manning editor did not apply the original NLCD values while preserving the custom class value.");

        bool staleManningRejected = await OperationRejectedAsync(client, "model.manningTransform",
            new ManningTransformRequest(CreateModelRequest(nvalue, fixture.NValuePath,
                fixture.ProjectPath, projectXml, ManningRows(nvalue)) with { Revision = "stale-nvalue-revision" }, "setNLCD"), cancellation);
        if (!staleManningRejected)
            throw new InvalidDataException("The Manning transform accepted a stale N-value source revision.");

        if (FileSHA256(fixture.ClassificationPath) != classificationSavedSHA
            || FileSHA256(fixture.NValuePath) != initialNValueSHA)
            throw new InvalidDataException("A Manning draft changed source HDF bytes before its parent save.");

        JsonElement savedManningJson = (await SubmitOperationAsync(client, "model.save",
            CreateModelRequest(manningDraft, fixture.NValuePath, fixture.ProjectPath,
                projectXml, ManningRows(manningDraft)), cancellation)).Result;
        ModelSnapshot savedManning = DeserializeModelSnapshot(savedManningJson);
        string nvalueSavedSHA = FileSHA256(fixture.NValuePath);
        if (!string.Equals(savedManning.Revision, nvalueSavedSHA, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nvalueSavedSHA, initialNValueSHA, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Manning save did not produce a new source HDF revision matching its receipt.");

        ModelSnapshot reopenedManning = await LoadSurfaceAsync(client, fixture.NValuePath,
            fixture.ProjectPath, projectXml, cancellation);
        Dictionary<string, string> reopenedManningValues = ManningValues(reopenedManning);
        bool manningSavedReadback = reopenedManningValues.GetValueOrDefault("Open Water") == "0.035"
            && reopenedManningValues.GetValueOrDefault("Deciduous Forest") == "0.15"
            && reopenedManningValues.GetValueOrDefault("Custom land") == "0.333";
        if (!manningSavedReadback)
            throw new InvalidDataException("Reopening the saved N-value layer did not recover its default and custom Manning values.");

        var manifest = new
        {
            schema = "hecras-classification-manning-http-e2e/v1",
            fixtureDirectory,
            projectPath = fixture.ProjectPath,
            projectRevision,
            classificationPath = fixture.ClassificationPath,
            classificationInitialRevision = initialClassificationRevision,
            classificationInitialSHA256 = initialClassificationSHA,
            classificationSavedRevision = classificationSavedSHA,
            nvaluePath = fixture.NValuePath,
            nvalueInitialRevision = initialNValueRevision,
            nvalueInitialSHA256 = initialNValueSHA,
            nvalueSavedRevision = nvalueSavedSHA,
            classificationRasterPath = fixture.ClassificationRasterPath,
            classificationRasterRevision = fixture.ClassificationRasterRevision,
            mappingValueRoundTrip,
            mappingStructureRejected,
            staleRevisionRejected,
            staleManningRejected,
            polygonUndoRedoReadback,
            polygonCountAfterCreate,
            polygonCountAfterUndo,
            polygonCountAfterRedo,
            nvaluePreviewReadback,
            manningDefaultsReadback,
            sourceHdfUnchangedBeforeSave,
            sourceRasterUnchanged,
            classificationSavedReadback,
            manningSavedReadback
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellation);
        return new("passed", fixtureDirectory, manifestPath, fixture.ProjectPath, projectRevision,
            fixture.ClassificationPath, initialClassificationRevision, classificationSavedSHA,
            fixture.NValuePath, initialNValueRevision, nvalueSavedSHA,
            fixture.ClassificationRasterPath, fixture.ClassificationRasterRevision,
            mappingValueRoundTrip, mappingStructureRejected, polygonUndoRedoReadback,
            manningDefaultsReadback, nvaluePreviewReadback, sourceHdfUnchangedBeforeSave,
            sourceRasterUnchanged, classificationSavedReadback, manningSavedReadback);
    }

    private sealed record ClassificationManningFixture(string ProjectPath, string ClassificationPath,
        string NValuePath, string ClassificationRasterPath, string ClassificationRasterRevision);

    private static ClassificationManningFixture CreateFixture(string root)
    {
        RasRuntimeBootstrap.Configure();
        string projectPath = Path.Combine(root, "Classification Manning Acceptance.ras");
        string rasterPath = Path.Combine(root, "classification-source.tif");
        CreateClassificationRaster(rasterPath);

        Project project = Project.CreateNew(projectPath, UnitSystem.SI);
        string classificationPath;
        string nvaluePath;
        try
        {
            ClassificationLayer classification = project.AddNewClassification("分类验收");
            Classification classes = new();
            classes.Set(1, "Open Water");
            classes.Set(2, "Deciduous Forest");
            classes.Set(3, "Custom land");
            classes.Set(4, "Editable class");
            RasterClassificationInput raster = classification.AddNewRasterLayer(rasterPath, classes);
            raster.Name = "验收分类栅格";

            NValueLayer nvalue = project.AddNewNValueLayer("糙率验收");
            nvalue.Mode = NValueSource.ClassificationLayer;
            nvalue.AssociatedClassificationLayer = classification;
            nvalue.InterpretationTable.Set("Open Water", .5f);
            nvalue.InterpretationTable.Set("Deciduous Forest", .4f);
            nvalue.InterpretationTable.Set("Custom land", .333f);
            nvalue.InterpretationTable.Set("Editable class", .321f);
            // Keep the new mapping available to the read-only preview after
            // the classification draft is saved, before Manning reconciliation.
            nvalue.InterpretationTable.Set("Edited class", .321f);

            foreach (CoreLayerBase layer in project.Terrains.Cast<CoreLayerBase>()
                .Concat(project.NValues).Concat(project.ClassificationLayers))
                layer.Save();
            project.Save();
            classificationPath = Path.GetFullPath(classification.Filename);
            nvaluePath = Path.GetFullPath(nvalue.Filename);
        }
        finally
        {
            DisposeClassificationManningProject(project);
        }

        Project reopened = new(projectPath);
        try
        {
            ClassificationLayer classification = reopened.ClassificationLayers.Single();
            NValueLayer nvalue = reopened.NValues.Single();
            if (classification.AllRasterInputs().Count != 1 || nvalue.Mode != NValueSource.ClassificationLayer
                || nvalue.AssociatedClassificationLayer == null
                || !string.Equals(Path.GetFullPath(nvalue.AssociatedClassificationLayer.Filename),
                    Path.GetFullPath(classification.Filename), StringComparison.Ordinal))
                throw new InvalidDataException("The generated project did not retain its classification and Manning associations.");
        }
        finally { DisposeClassificationManningProject(reopened); }

        return new(projectPath, classificationPath, nvaluePath, rasterPath, FileSHA256(rasterPath));
    }

    private static void CreateClassificationRaster(string path)
    {
        const int width = 16, height = 16;
        using Driver driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The GDAL GeoTIFF driver is unavailable.");
        // RasterClassificationInput uses the original tiled TIFF reader, which
        // rejects GDAL's default stripped layout. Keep this fixture a real
        // single-band Int32 GeoTIFF while matching that source contract.
        using Dataset dataset = driver.Create(path, width, height, 1, DataType.GDT_Int32,
            ["TILED=YES", "BLOCKXSIZE=16", "BLOCKYSIZE=16"])
            ?? throw new InvalidOperationException("The classification raster fixture could not be created.");
        dataset.SetGeoTransform([0, 1, 0, height, 0, -1]);
        using Band band = dataset.GetRasterBand(1);
        int[] values = Enumerable.Range(0, width * height).Select(index => index % 4 + 1).ToArray();
        CPLErr status = band.WriteRaster(0, 0, width, height, values, width, height, 0, 0);
        if (status != CPLErr.CE_None)
            throw new IOException("The classification raster samples could not be written: " + status);
        band.FlushCache();
        dataset.FlushCache();
    }

    private static void DisposeClassificationManningProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
            .Concat(project.Geometries).Concat(project.Terrains).Concat(project.BoundaryConditions)
            .Concat(project.NValues).Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (var result in project.Results) result.Dispose();
    }

    private static async Task<ModelSnapshot> LoadSurfaceAsync(HttpClient client, string assetPath,
        string projectPath, string projectXml, CancellationToken cancellation)
    {
        JsonElement json = (await SubmitOperationAsync(client, "model.load", new
        {
            assetPath,
            kind = "Surface Layers",
            projectPath,
            projectXml
        }, cancellation)).Result;
        return DeserializeModelSnapshot(json);
    }

    private static ModelSnapshot DeserializeModelSnapshot(JsonElement json) =>
        json.Deserialize<ModelSnapshot>(BackendJson.Options)
        ?? throw new InvalidDataException("The source model operation returned an empty model snapshot.");

    private static Dictionary<string, ModelRow[]> ClassificationRows(ModelSnapshot snapshot) =>
        (snapshot.Classification?.Inputs ?? []).Where(input => input.TableId is not null)
            .ToDictionary(input => input.TableId!, input => input.Rows, StringComparer.Ordinal);

    private static Dictionary<string, ModelRow[]> ManningRows(ModelSnapshot snapshot) =>
        snapshot.Manning is null ? new(StringComparer.Ordinal)
            : new(StringComparer.Ordinal) { [ManningTable] = snapshot.Manning.Rows };

    private static ModelSaveRequest CreateModelRequest(ModelSnapshot snapshot, string assetPath,
        string projectPath, string projectXml, Dictionary<string, ModelRow[]> rows) =>
        new(assetPath, "Surface Layers", projectPath, snapshot.Revision,
            WritableRequestFields(snapshot), snapshot.Images,
            projectXml, snapshot.SourceState, rows);

    private static Dictionary<string, string> WritableRequestFields(ModelSnapshot snapshot)
    {
        // Mirror EditorWorkbench's changed-field request shape: snapshot values
        // are read baselines, not requested writes. Filter both service-declared
        // read-only fields and schema catalog fields whose read-only metadata is
        // not carried in ModelSnapshot (for example Context_Name).
        HashSet<string> readOnly = (snapshot.ReadOnlyFields ?? []).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> candidate = new(snapshot.Fields, StringComparer.Ordinal);
        Dictionary<string, string> fields = candidate
            .Where(pair => !readOnly.Contains(pair.Key)
                && !HostReadOnlyClassificationCatalogFields.Contains(pair.Key)
                && (!snapshot.Fields.TryGetValue(pair.Key, out string? baseline) || pair.Value != baseline))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        // Classification selection is navigation state, but the parent UI
        // intentionally includes it in transform/save calls even when unchanged.
        const string selectionField = "ClassificationLayersEditor/ClassificationLayer_SelectedClassificationInput";
        if (snapshot.Classification is { } classification
            && !readOnly.Contains(selectionField)
            && !HostReadOnlyClassificationCatalogFields.Contains(selectionField))
            fields[selectionField] = classification.SelectedId ?? "";
        return fields;
    }

    private static async Task<ModelSnapshot> TransformClassificationAsync(HttpClient client,
        ModelSnapshot snapshot, string assetPath, string projectPath, string projectXml,
        Dictionary<string, ModelRow[]> rows, string operation,
        ClassificationGeometryMutation? geometry, CancellationToken cancellation)
    {
        ModelSaveRequest model = CreateModelRequest(snapshot, assetPath, projectPath, projectXml, rows);
        JsonElement result = (await SubmitOperationAsync(client, "model.classificationTransform",
            new ClassificationTransformRequest(model, operation, Geometry: geometry), cancellation)).Result;
        return DeserializeModelSnapshot(result);
    }

    private static Task<ModelSnapshot> TransformClassificationGeometryAsync(HttpClient client,
        ModelSnapshot snapshot, string assetPath, string projectPath, string projectXml, string operation,
        CancellationToken cancellation)
    {
        ClassificationGeometryMutation mutation = operation == "create"
            ? new("create", Vertices:
            [
                new ClassificationVertex(1, 1), new ClassificationVertex(5, 1),
                new ClassificationVertex(3, 5), new ClassificationVertex(1, 1)
            ], Classification: "Open Water")
            : new(operation);
        return TransformClassificationAsync(client, snapshot, assetPath, projectPath, projectXml,
            ClassificationRows(snapshot), "geometry", mutation, cancellation);
    }

    private static async Task<bool> RejectClassificationRowsAsync(HttpClient client,
        ModelSnapshot snapshot, ClassificationManningFixture fixture, string projectXml,
        string invalidKind, CancellationToken cancellation)
    {
        Dictionary<string, ModelRow[]> rows = ClassificationRows(snapshot);
        string tableId = snapshot.Classification?.Inputs.Single(input => input.Type == "Raster").TableId
            ?? throw new InvalidDataException("The classification mapping table is missing.");
        ModelRow[] current = rows[tableId];
        if (invalidKind == "key")
        {
            ModelRow target = current[0];
            Dictionary<string, string> values = new(target.Values, StringComparer.Ordinal);
            values["Key"] = (int.Parse(values["Key"], CultureInfo.InvariantCulture) + 100).ToString(CultureInfo.InvariantCulture);
            rows[tableId] = current.Select(row => row.Id == target.Id ? row with { Values = values } : row).ToArray();
        }
        else if (invalidKind == "insert")
        {
            rows[tableId] = current.Append(new ModelRow("forged-row-id",
                new(StringComparer.Ordinal) { ["Key"] = "887", ["Value"] = "Forged" })).ToArray();
        }
        else if (invalidKind == "remove")
        {
            rows[tableId] = current.SkipLast(1).ToArray();
        }
        else throw new ArgumentOutOfRangeException(nameof(invalidKind));

        ModelSaveRequest model = CreateModelRequest(snapshot, fixture.ClassificationPath,
            fixture.ProjectPath, projectXml, rows);
        return await OperationRejectedAsync(client, "model.classificationTransform",
            new ClassificationTransformRequest(model, "refresh"), cancellation);
    }

    private static ClassificationInputData FindClassificationInput(ModelSnapshot snapshot, string id) =>
        snapshot.Classification?.Inputs.SingleOrDefault(input => input.Id == id)
        ?? throw new InvalidDataException("A classification transform lost an existing input identity.");

    private static ClassificationInputData[] ClassificationPolygons(ModelSnapshot snapshot) =>
        (snapshot.Classification?.Inputs ?? []).Where(input => input.Type == PolygonClassificationInput.TypeNameStatic).ToArray();

    private static Dictionary<string, string> ManningValues(ModelSnapshot snapshot) =>
        (snapshot.Manning?.Rows ?? []).ToDictionary(
            row => row.Values.GetValueOrDefault("Key") ?? "",
            row => row.Values.GetValueOrDefault("Value") ?? "",
            StringComparer.Ordinal);
}

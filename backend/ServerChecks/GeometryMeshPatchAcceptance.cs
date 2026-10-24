using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Geospatial.Vectors;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Ras.Layers;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record GeometryMeshPatchAcceptanceReport(
    string State,
    string FixtureDirectory,
    string ProjectPath,
    string GeometryPath,
    int CartesianRegionFID,
    string SourceRevision,
    string SavedRevision,
    double InitialPrimarySize,
    double InitialSecondarySize,
    double AppliedSize,
    bool SingleMutationRecorded,
    bool DraftUndoReadback,
    bool DraftRedoReadback,
    bool InvalidInputsRejected,
    bool StaleRevisionRejected,
    bool SourceHdfUnchangedBeforeParentSave,
    bool ParentSaveReadback,
    bool OuterArcsFollowSourceAutoPopulate);

/// <summary>
/// Exercises the Cartesian combined-size child-draft mutation through the
/// actual HTTP Host. The fixture is generated in a caller-selected fresh
/// directory and its geometry HDF is modified only inside that isolated copy.
/// </summary>
internal static partial class ServerAcceptance
{
    public static async Task<GeometryMeshPatchAcceptanceReport> RunGeometryMeshPatchAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Geometry mesh patch acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("Geometry mesh patch acceptance requires a new output directory; existing files are never overwritten.");

        FixtureManifest fixture = PlanComputeFixture.CreateMap(outputDirectory);
        ConfigureCartesianPatch(fixture);
        string projectXml = await File.ReadAllTextAsync(fixture.ProjectPath);
        string initialGeometrySHA = FileSHA256(fixture.GeometryPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The C# server did not report ready.");

        object model = new
        {
            assetPath = fixture.GeometryPath,
            kind = "Geometry",
            projectPath = fixture.ProjectPath,
            projectXml
        };
        JsonElement modelSnapshot = (await SubmitOperationAsync(client, "model.load",
            new { assetPath = fixture.GeometryPath, kind = "Geometry", projectPath = fixture.ProjectPath, projectXml },
            cancellation)).Result;
        JsonElement initial = (await SubmitOperationAsync(client, "geometry.mesh.load",
            new { model }, cancellation)).Result;
        JsonElement initialRow = CartesianRegion(initial);
        int regionFID = IntProperty(initialRow, "fid");
        double initialPrimary = RegionValue(initialRow, "ConstantSize");
        double initialSecondary = RegionValue(initialRow, "SecondaryConstantSize");
        if (initialPrimary == initialSecondary)
            throw new InvalidDataException("The acceptance Cartesian patch must start with distinct primary and secondary sizes.");

        string sourceRevision = StringProperty(initial, "revision");
        string initialDraft = StringProperty(initial, "draftState");
        string appliedText = "7.25";
        const double appliedSize = 7.25;
        JsonElement edited = (await SubmitOperationAsync(client, "geometry.mesh.edit", new
        {
            model,
            revision = sourceRevision,
            draftState = initialDraft,
            topologyDraftState = (string?)null,
            selectedRegionIDs = new[] { regionFID },
            mutation = new
            {
                operation = "setCartesianCombinedCellSize",
                featureIDs = new[] { regionFID },
                value = appliedText
            }
        }, cancellation)).Result;
        JsonElement editedRow = CartesianRegion(edited);
        if (RegionValue(editedRow, "ConstantSize") != appliedSize ||
            RegionValue(editedRow, "SecondaryConstantSize") != appliedSize)
            throw new InvalidDataException("The Cartesian combined-size edit did not update both region sizes to the exact requested value.");

        using JsonDocument draftDocument = JsonDocument.Parse(StringProperty(edited, "draftState"));
        JsonElement ledger = draftDocument.RootElement;
        JsonElement mutations = Property(ledger, "mutations");
        bool singleMutationRecorded = mutations.GetArrayLength() == 1 &&
            StringProperty(mutations[0], "operation") == "setCartesianCombinedCellSize";
        if (!singleMutationRecorded)
            throw new InvalidDataException("The Cartesian combined-size action did not produce exactly one replayable mesh-draft mutation.");

        JsonElement undone = (await SubmitOperationAsync(client, "geometry.mesh.load", new
        {
            model,
            revision = sourceRevision,
            draftState = initialDraft,
            topologyDraftState = (string?)null,
            selectedRegionIDs = new[] { regionFID }
        }, cancellation)).Result;
        JsonElement undoneRow = CartesianRegion(undone);
        bool draftUndoReadback = RegionValue(undoneRow, "ConstantSize") == initialPrimary &&
            RegionValue(undoneRow, "SecondaryConstantSize") == initialSecondary &&
            SelectedRegionFID(undone) == regionFID;
        if (!draftUndoReadback) throw new InvalidDataException("Loading the parent mesh draft before the mutation did not restore the captured Cartesian sizes and selection.");

        JsonElement redone = (await SubmitOperationAsync(client, "geometry.mesh.load", new
        {
            model,
            revision = sourceRevision,
            draftState = StringProperty(edited, "draftState"),
            topologyDraftState = (string?)null,
            selectedRegionIDs = new[] { regionFID }
        }, cancellation)).Result;
        JsonElement redoneRow = CartesianRegion(redone);
        bool draftRedoReadback = RegionValue(redoneRow, "ConstantSize") == appliedSize &&
            RegionValue(redoneRow, "SecondaryConstantSize") == appliedSize &&
            SelectedRegionFID(redone) == regionFID;
        if (!draftRedoReadback) throw new InvalidDataException("Loading the accepted mesh draft again did not restore the Cartesian sizes and selection.");

        bool invalidInputsRejected = true;
        foreach (string invalidValue in new[] { "0", "-1", "NaN", "Infinity" })
        {
            bool rejected = await OperationRejectedAsync(client, "geometry.mesh.edit", new
            {
                model,
                revision = sourceRevision,
                draftState = StringProperty(edited, "draftState"),
                topologyDraftState = (string?)null,
                selectedRegionIDs = new[] { regionFID },
                mutation = new
                {
                    operation = "setCartesianCombinedCellSize",
                    featureIDs = new[] { regionFID },
                    value = invalidValue
                }
            }, cancellation);
            if (!rejected) { invalidInputsRejected = false; break; }
        }
        if (!invalidInputsRejected)
            throw new InvalidDataException("A zero, negative, or non-finite Cartesian combined size was accepted.");

        bool staleRevisionRejected = await OperationRejectedAsync(client, "geometry.mesh.edit", new
        {
            model,
            revision = sourceRevision + "stale",
            draftState = StringProperty(edited, "draftState"),
            topologyDraftState = (string?)null,
            selectedRegionIDs = new[] { regionFID },
            mutation = new
            {
                operation = "setCartesianCombinedCellSize",
                featureIDs = new[] { regionFID },
                value = "8"
            }
        }, cancellation);
        if (!staleRevisionRejected) throw new InvalidDataException("A mesh edit with a stale source revision was not rejected.");
        bool sourceHdfUnchangedBeforeParentSave = FileSHA256(fixture.GeometryPath) == initialGeometrySHA;
        if (!sourceHdfUnchangedBeforeParentSave)
            throw new InvalidDataException("The Cartesian child draft changed the source HDF before the parent save.");

        JsonElement saved = (await SubmitOperationAsync(client, "model.save", new
        {
            assetPath = fixture.GeometryPath,
            kind = "Geometry",
            projectPath = fixture.ProjectPath,
            revision = sourceRevision,
            // A Geometry catalog snapshot contains read-only summary fields.
            // The mesh draft is the only edit in this acceptance, so do not
            // echo those display-only values back as ordinary model edits.
            fields = new Dictionary<string, string>(),
            images = Property(modelSnapshot, "images"),
            projectXml,
            sourceState = OptionalString(modelSnapshot, "sourceState"),
            geometryMeshState = StringProperty(edited, "draftState")
        }, cancellation)).Result;
        string savedRevision = StringProperty(saved, "revision");
        JsonElement reopened = (await SubmitOperationAsync(client, "geometry.mesh.load", new
        {
            model,
            revision = savedRevision,
            selectedRegionIDs = new[] { regionFID }
        }, cancellation)).Result;
        JsonElement reopenedRow = CartesianRegion(reopened);
        bool parentSaveReadback = RegionValue(reopenedRow, "ConstantSize") == appliedSize &&
            RegionValue(reopenedRow, "SecondaryConstantSize") == appliedSize;
        if (!parentSaveReadback)
            throw new InvalidDataException("The parent ModelSave did not persist and read back both Cartesian combined-size fields.");

        JsonElement topology = (await SubmitOperationAsync(client, "topology.load",
            new { model, revision = savedRevision }, cancellation)).Result;
        double[] outerArcSizes = Property(Property(topology, "arcs"), "rows").EnumerateArray()
            .Select(row => StringValue(Property(row, "values"), "ConstantSize"))
            .Where(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
        if (outerArcSizes.Length != 4)
            throw new InvalidDataException("The saved Cartesian rectangle did not reopen with its four source arcs.");
        bool outerArcsFollowSourceAutoPopulate = Geospatial.Rendering.Editing.TopologyEditOperation.AutoPopulateMeshValues
            ? outerArcSizes.All(value => value == appliedSize)
            : outerArcSizes.All(value => value == 2);
        if (!outerArcsFollowSourceAutoPopulate)
            throw new InvalidDataException("The combined size's eligible outer-arc values do not match the source AutoPopulateMeshValues behavior.");

        Directory.CreateDirectory(outputDirectory);
        string manifestPath = Path.Combine(outputDirectory, "geometry-mesh-patch-http-e2e-manifest.json");
        var manifest = new
        {
            schema = "hecras-geometry-mesh-patch-http-e2e/v1",
            projectPath = fixture.ProjectPath,
            geometryPath = fixture.GeometryPath,
            sourceRevision,
            savedRevision,
            cartesianRegionFID = regionFID,
            initial = new { constantSize = initialPrimary, secondaryConstantSize = initialSecondary },
            appliedSize,
            sourceSHA256 = initialGeometrySHA,
            sourceSHA256BeforeParentSave = initialGeometrySHA,
            sourceSHA256AfterParentSave = FileSHA256(fixture.GeometryPath),
            operation = "geometry.mesh.edit / setCartesianCombinedCellSize"
        };
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), cancellation);
        return new GeometryMeshPatchAcceptanceReport("passed", outputDirectory, fixture.ProjectPath,
            fixture.GeometryPath, regionFID, sourceRevision, savedRevision, initialPrimary, initialSecondary,
            appliedSize, singleMutationRecorded, draftUndoReadback, draftRedoReadback, invalidInputsRejected,
            staleRevisionRejected, sourceHdfUnchangedBeforeParentSave, parentSaveReadback,
            outerArcsFollowSourceAutoPopulate);
    }

    private static void ConfigureCartesianPatch(FixtureManifest fixture)
    {
        RasRuntimeBootstrap.Configure();
        Project project = new(fixture.ProjectPath);
        try
        {
            Geometry geometry = project.Geometries.Single();
            Topology topology = Topology.FromExtent(new Extent(0, 4, 0, 4));
            topology.EnsureMeshMetadataColumns();
            topology.SetControlPointData(0, PatchType.Cartesian, SizeFunction.Constant,
                2f, stretchRate: 0.1f, secondaryCellSize: 5f, rotationDegrees: 0f,
                recipeSource: "Default()", multipointPrecision: 0f, sizeTolerance: 1f);
            for (int arcID = 0; arcID < topology.Arcs.Count; arcID++)
                topology.SetArcData(arcID, LinearSizeFunction.Constant, 2f, 4, 1f);
            geometry.ConceptualMeshLayer.Topology = topology;
            geometry.Save();
        }
        finally
        {
            foreach (Result result in project.Results) result.Dispose();
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                         .Concat(project.Geometries).Concat(project.BoundaryConditions)
                         .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
                layer.Dispose();
        }
    }

    private static JsonElement CartesianRegion(JsonElement snapshot)
    {
        JsonElement row = Property(Property(snapshot, "regions"), "rows").EnumerateArray()
            .SingleOrDefault(item => StringValue(Property(item, "values"), "PatchType") == "Cartesian");
        return row.ValueKind == JsonValueKind.Object
            ? row
            : throw new InvalidDataException("The geometry mesh snapshot omitted the exact Cartesian region row.");
    }

    private static double RegionValue(JsonElement row, string field) =>
        double.Parse(StringValue(Property(row, "values"), field), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static int SelectedRegionFID(JsonElement snapshot)
    {
        JsonElement selected = Property(snapshot, "selectedRegionIDs");
        return selected.GetArrayLength() == 1
            ? selected[0].GetInt32()
            : throw new InvalidDataException("The source mesh snapshot lost its single selected Cartesian region.");
    }

    private static string StringValue(JsonElement values, string field)
    {
        JsonElement value = PropertyOrNull(values, field);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new InvalidDataException("The source region row omitted its " + field + " value.")
        };
    }

    private static async Task<bool> OperationRejectedAsync(HttpClient client, string method,
        object parameters, CancellationToken cancellation)
    {
        try
        {
            _ = await SubmitOperationAsync(client, method, parameters, cancellation);
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private static string FileSHA256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Hydraulics.Culverts;
using Ras.Layers;
using Ras.Layers.Terrains;
using Ras.Observables;
using Utility.Validation;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    public static async Task<GeometryCulvertSnapshot> LoadGeometryCulvertsAsync(GeometryCulvertLoadRequest request)
    {
        VerifyGeometryCulvertModel(request.Model);
        string revision = Revision(request.Model.AssetPath);
        if (request.Revision is not null && !string.Equals(request.Revision, revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(request.Model);
        GeometryCulvertDraftState state = request.DraftState is null
            ? NewGeometryCulvertDraft(request.Model.AssetPath, revision, dependencyRevision)
            : ReadGeometryCulvertDraft(request.DraftState, request.Model.AssetPath, revision, dependencyRevision);
        GeometryCulvertSnapshot result;
        using (var context = new ModelContext(ToModelRequest(request.Model)))
        {
            GeometryCulvertIdentityMap identities = CreateGeometryCulvertIdentityMap(context);
            ApplyGeometryCulvertMutations(context, request.Model.AssetPath, revision, state, identities);
            result = GeometryCulvertSnapshotFor(context, revision, state, identities,
                request.SelectedBarrelIDs ?? [], dependencyRevision);
        }
        if (Revision(request.Model.AssetPath) != revision) throw new SourceFileChangedException();
        VerifyGeometryCulvertDependencyRevision(request.Model, dependencyRevision);
        return await Task.FromResult(result);
    }

    public static async Task<GeometryCulvertSnapshot> EditGeometryCulvertsAsync(GeometryCulvertEditRequest request)
    {
        VerifyGeometryCulvertModel(request.Model);
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(request.Model);
        GeometryCulvertDraftState state = ReadGeometryCulvertDraft(request.DraftState, request.Model.AssetPath,
            request.Revision, dependencyRevision);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        GeometryCulvertSnapshot result;
        using (var context = new ModelContext(ToModelRequest(request.Model)))
        {
            GeometryCulvertIdentityMap identities = CreateGeometryCulvertIdentityMap(context);
            GeometryCulvertMutation mutation = request.Mutation ?? throw new ArgumentException("Missing culvert edit.");
            EnsureGeometryCulvertEditable(context);
            GeometryCulvertDraftState edited;
            if (mutation.Operation == "undo")
            {
                if (state.Mutations.Length == 0) throw new ArgumentException("There is no pending culvert edit to undo.");
                edited = state with { Mutations = state.Mutations[..^1] };
                ApplyGeometryCulvertMutations(context, request.Model.AssetPath, request.Revision, edited, identities);
            }
            else
            {
                ApplyGeometryCulvertMutations(context, request.Model.AssetPath, request.Revision, state, identities);
                bool changed = ApplyGeometryCulvertMutation(context, identities, mutation);
                edited = changed ? state with { Mutations = [.. state.Mutations, mutation] } : state;
            }
            result = GeometryCulvertSnapshotFor(context, request.Revision, edited, identities,
                request.SelectedBarrelIDs ?? [], dependencyRevision);
        }
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        VerifyGeometryCulvertDependencyRevision(request.Model, dependencyRevision);
        return await Task.FromResult(result);
    }

    /// <summary>
    /// Renders the currently associated source terrain with the same raster
    /// renderer and default hillshade symbology used by the retained terrain
    /// preview. The returned preview is bounded to 512 pixels on its longest
    /// side; the source and rendered extents are returned separately.
    /// </summary>
    public static Task<GeometryCulvertTerrainPreview> LoadGeometryCulvertTerrainPreviewAsync(
        GeometryCulvertTerrainPreviewRequest request)
    {
        VerifyGeometryCulvertModel(request.Model);
        string revision = Revision(request.Model.AssetPath);
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(request.Model);
        if (request.ExpectedDependencyRevision is not null
            && !string.Equals(request.ExpectedDependencyRevision, dependencyRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        GeometryCulvertTerrainPreview result;
        using (var context = new ModelContext(ToModelRequest(request.Model)))
        {
            ObservableGeometry geometry = GeometryCulvertGeometry(context);
            ObservableTerrain? terrain = geometry.AssociatedTerrain;
            if (terrain is null)
            {
                result = new(dependencyRevision, null, null, null, 0, 0, []);
            }
            else
            {
                Extent validExtent = terrain.Terrain.ValidExtent;
                GeometryCulvertExtent? sourceExtent = ToGeometryCulvertExtent(validExtent);
                if (sourceExtent is null)
                {
                    result = new(dependencyRevision, terrain.Name, null, null, 0, 0, []);
                }
                else
                {
                    (int width, int height) = TerrainPreviewDimensions(validExtent, 512);
                    var query = new RasterDefinition(validExtent, height, width);
                    var layer = new RasterRenderLayer(terrain.Terrain);
                    layer.SetDefaultTerrainHistoSymbology();
                    using var plot = new PlotRequest(query);
                    layer.Render(plot);
                    ReadOnlySpan<int> argb = plot.ColorBufferARGB.Span;
                    if (argb.Length != checked(width * height))
                        throw new InvalidOperationException("The source terrain renderer returned an incomplete image.");
                    var rgba = new byte[checked(argb.Length * 4)];
                    for (int index = 0; index < argb.Length; index++)
                    {
                        int pixel = argb[index];
                        int byteIndex = index * 4;
                        rgba[byteIndex] = (byte)(pixel >> 16);
                        rgba[byteIndex + 1] = (byte)(pixel >> 8);
                        rgba[byteIndex + 2] = (byte)pixel;
                        rgba[byteIndex + 3] = (byte)(pixel >> 24);
                    }
                    result = new(dependencyRevision, terrain.Name, sourceExtent,
                        ToGeometryCulvertExtent(query.Extent), width, height, rgba);
                }
            }
        }

        if (Revision(request.Model.AssetPath) != revision) throw new SourceFileChangedException();
        VerifyGeometryCulvertDependencyRevision(request.Model, dependencyRevision);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Applies pending culvert edits to the parent's already-open staged HDF.
    /// The parent owns the only Save and publish operation.
    /// </summary>
    private static void ApplyGeometryCulvertDraft(ModelContext context, ModelSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GeometryCulvertState)) return;
        var model = new GeometryCulvertModelRequest(request.AssetPath, request.Kind, request.ProjectPath,
            request.ProjectXml, request.CoreLayerContext);
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(model);
        GeometryCulvertDraftState state = ReadGeometryCulvertDraft(request.GeometryCulvertState,
            request.AssetPath, request.Revision, dependencyRevision);
        if (Revision(request.AssetPath) != request.Revision) throw new SourceFileChangedException();
        GeometryCulvertIdentityMap identities = CreateGeometryCulvertIdentityMap(context);
        ApplyGeometryCulvertMutations(context, request.AssetPath, request.Revision, state, identities);
        VerifyGeometryCulvertDependencyRevision(model, dependencyRevision);
    }

    private static ModelRequest ToModelRequest(GeometryCulvertModelRequest model) =>
        new(model.AssetPath, model.Kind, model.ProjectPath, model.ProjectXml,
            CoreLayerContext: model.CoreLayerContext);

    private static void VerifyGeometryCulvertModel(GeometryCulvertModelRequest model)
    {
        if (model.Kind != "Geometry") throw new ArgumentException("Culvert editing requires a geometry model.");
        if (string.IsNullOrWhiteSpace(model.AssetPath) || !File.Exists(model.AssetPath))
            throw new FileNotFoundException("The geometry source file was not found.", model.AssetPath);
    }

    private static GeometryCulvertDraftState NewGeometryCulvertDraft(string assetPath, string revision,
        string dependencyRevision) => new(SourceFilePaths.Canonical(assetPath), revision, dependencyRevision, []);

    private static GeometryCulvertDraftState ReadGeometryCulvertDraft(string encoded, string assetPath, string revision,
        string dependencyRevision)
    {
        if (string.IsNullOrWhiteSpace(encoded)) throw new ArgumentException("Missing culvert draft state.");
        GeometryCulvertDraftState state;
        try
        {
            state = JsonSerializer.Deserialize<GeometryCulvertDraftState>(encoded, BackendJson.Options)
                ?? throw new ArgumentException("Invalid culvert draft state.");
        }
        catch (JsonException exception) { throw new ArgumentException("Invalid culvert draft state.", exception); }
        if (!string.Equals(state.AssetPath, SourceFilePaths.Canonical(assetPath), StringComparison.Ordinal)
            || !string.Equals(state.Revision, revision, StringComparison.Ordinal)
            || !string.Equals(state.DependencyRevision, dependencyRevision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (state.Mutations is null) throw new ArgumentException("The culvert draft state is incomplete.");
        return state;
    }

    /// <summary>
    /// Binds culvert profiles and their replayable draft to the actual terrain
    /// chosen for this geometry in the current project context. The terrain
    /// HDF and every raster input that can contribute to its sampled surface
    /// are hashed before and after profile reads and again before parent save.
    /// </summary>
    private static string CaptureGeometryCulvertDependencyRevision(GeometryCulvertModelRequest model)
    {
        var dependencies = new Dictionary<string, string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void AddDependency(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string canonicalPath = SourceFilePaths.Canonical(path);
            dependencies[canonicalPath] = File.Exists(canonicalPath)
                ? "sha256:" + Revision(canonicalPath)
                : "missing";
        }

        string? projectIdentity = null;
        if (!string.IsNullOrWhiteSpace(model.ProjectPath))
        {
            if (!Path.IsPathFullyQualified(model.ProjectPath))
                throw new ArgumentException("Culvert profile context requires an absolute project path.");
            projectIdentity = SourceFilePaths.Canonical(model.ProjectPath);
            var project = CoreLayerImportService.OpenProject(Path.GetFullPath(model.ProjectPath),
                model.ProjectXml, model.CoreLayerContext);
            try
            {
                Geometry? geometry = project.Geometries.FirstOrDefault(candidate =>
                    SourceFilePaths.SameSourceFile(candidate.Filename, model.AssetPath));
                if (geometry is not null)
                {
                    GeometryAssociation association = project.GetGeometryAssociation(geometry);
                    // Keep the unresolved name in the identity too: a missing
                    // terrain and an unassociated geometry are distinct states.
                    dependencies["@terrain-name"] = association.TerrainName;
                    if (!string.IsNullOrWhiteSpace(association.TerrainName))
                    {
                        Terrain? terrain = association.Terrain;
                        string terrainPath = terrain?.Filename ?? Terrain.MakeFilename(
                            project.ProjectDirectory, association.TerrainName);
                        AddDependency(terrainPath);
                        if (terrain is not null)
                            foreach (RasterTerrainInput input in terrain.AllRasterInputs())
                                AddDependency(input.Filename);
                    }
                }
            }
            finally { DisposeProjectLayers(project); }
        }

        AddDependency(model.AssetPath);
        string[] ordered = dependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "\0" + pair.Value).ToArray();
        string payload = (projectIdentity ?? "") + "\0" + string.Join("\n", ordered);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static void VerifyGeometryCulvertDependencyRevision(GeometryCulvertModelRequest model, string expected)
    {
        if (!string.Equals(CaptureGeometryCulvertDependencyRevision(model), expected, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    // Recheck after the parent writer closes, immediately before publication.
    // Sampling dependencies can change while a large staged geometry is saved.
    private static void VerifyGeometryCulvertSaveDependencies(ModelSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GeometryCulvertState)) return;
        var model = new GeometryCulvertModelRequest(request.AssetPath, request.Kind, request.ProjectPath,
            request.ProjectXml, request.CoreLayerContext);
        string dependencyRevision = CaptureGeometryCulvertDependencyRevision(model);
        _ = ReadGeometryCulvertDraft(request.GeometryCulvertState, request.AssetPath, request.Revision, dependencyRevision);
    }

    private sealed class GeometryCulvertIdentityMap
    {
        public Dictionary<string, ObservableCulvertBarrel> Barrels { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ObservableBarrelProperties> BarrelProperties { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ObservableOpeningProperties> OpeningProperties { get; } = new(StringComparer.Ordinal);
    }

    private static GeometryCulvertIdentityMap CreateGeometryCulvertIdentityMap(ModelContext context)
    {
        ObservableGeometry geometry = GeometryCulvertGeometry(context);
        var result = new GeometryCulvertIdentityMap();
        AddSourceRows(result.Barrels, "barrel", geometry.CulvertBarrelLayer.Features);
        AddSourceRows(result.BarrelProperties, "barrelProperties", geometry.BarrelPropertiesLayer.Features);
        AddSourceRows(result.OpeningProperties, "openingProperties", geometry.OpeningPropertiesLayer.Features);
        return result;
    }

    private static void AddSourceRows<TObservable>(Dictionary<string, TObservable> target, string featureKind, IReadOnlyList<TObservable> features)
    {
        for (int fid = 0; fid < features.Count; fid++) target.Add(SourceFeatureID(featureKind, fid), features[fid]);
    }

    private static string SourceFeatureID(string featureKind, int fid) => featureKind + ":source:" + fid.ToString(CultureInfo.InvariantCulture);
    private static string DraftFeatureID(string featureKind, string draftID) => featureKind + ":draft:" + draftID;

    private static void ApplyGeometryCulvertMutations(ModelContext context, string assetPath, string revision,
        GeometryCulvertDraftState state, GeometryCulvertIdentityMap identities)
    {
        if (Revision(assetPath) != revision) throw new SourceFileChangedException();
        foreach (GeometryCulvertMutation mutation in state.Mutations)
            ApplyGeometryCulvertMutation(context, identities, mutation);
    }

    private static bool ApplyGeometryCulvertMutation(ModelContext context, GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        EnsureGeometryCulvertEditable(context);
        if (mutation.FeatureIDs is null) throw new ArgumentException("The culvert edit has no feature identity list.");
        switch (mutation.Operation)
        {
            case "add": return AddGeometryCulvertPropertySet(context, identities, mutation);
            case "addBarrel": return AddGeometryCulvertBarrel(context, identities, mutation);
            case "split":
            case "merge":
            case "offset": return ApplyGeometryCulvertMapCommand(context, identities, mutation);
            case "moveBarrels": return MoveGeometryCulvertBarrels(identities, mutation);
            case "reverseBarrel": return ReverseGeometryCulvertBarrel(identities, mutation);
            case "filterBarrel": return FilterGeometryCulvertBarrel(identities, mutation);
            case "smoothBarrel": return SmoothGeometryCulvertBarrel(identities, mutation);
            case "pasteBarrels": return PasteGeometryCulvertBarrels(context, identities, mutation);
            case "delete": return DeleteGeometryCulvertFeatures(context, identities, mutation);
            case "resetOpeningCoefficient": return ResetOpeningCoefficient(identities, mutation);
            case "forceSetOpeningType": return ForceSetOpeningType(identities, mutation);
            case "setField": return SetGeometryCulvertField(context, identities, mutation);
            case "setPolyline": return SetGeometryCulvertPolyline(identities, mutation);
            default: throw new ArgumentException("Unsupported culvert operation: " + mutation.Operation);
        }
    }

    private static bool AddGeometryCulvertBarrel(ModelContext context, GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "barrel" || mutation.FeatureIDs.Length != 0)
            throw new ArgumentException("A new culvert line requires the barrel table and no source row IDs.");
        string draftID = RequireDraftID(mutation.DraftID);
        string id = DraftFeatureID("barrel", draftID);
        if (identities.Barrels.ContainsKey(id)) throw new ArgumentException("The culvert barrel draft identity already exists.");
        Polyline polyline = ToValidCulvertPolyline(mutation.Polyline);
        ObservableCulvertBarrel created = new(new CulvertBarrel { Name = "Culvert Barrel", Polyline = polyline });
        GeometryCulvertGeometry(context).CulvertBarrelLayer.AddFeature(created);
        identities.Barrels.Add(id, created);
        return true;
    }

    private static bool SetGeometryCulvertPolyline(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "barrel" || mutation.FeatureIDs.Length != 1)
            throw new ArgumentException("Culvert line edits require exactly one barrel row ID.");
        ObservableCulvertBarrel barrel = ResolveRows(identities.Barrels, mutation.FeatureIDs).Single();
        barrel.Polyline = ToValidCulvertPolyline(mutation.Polyline);
        return true;
    }

    private static bool MoveGeometryCulvertBarrels(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "barrel" || mutation.FeatureIDs.Length == 0
            || mutation.DeltaX is not double deltaX || mutation.DeltaY is not double deltaY
            || !double.IsFinite(deltaX) || !double.IsFinite(deltaY)
            || (deltaX == 0d && deltaY == 0d))
            throw new ArgumentException("Moving culvert barrels requires selected source rows and a finite nonzero offset.");

        ObservableCulvertBarrel[] barrels = ResolveRows(identities.Barrels, mutation.FeatureIDs);
        foreach (ObservableCulvertBarrel barrel in barrels)
        {
            if (barrel.Polyline is null || barrel.Polyline.Count < 2)
                throw new ArgumentException("A selected culvert barrel has no movable line geometry.");
            GeometryCulvertCoordinate[] translated = CoordinatesFor(barrel.Polyline)
                .Select(point => point with { X = point.X + deltaX, Y = point.Y + deltaY }).ToArray();
            if (translated.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new ArgumentException("Moving the selected culvert barrels would create non-finite coordinates.");
            barrel.Polyline = ToValidCulvertPolyline(translated);
        }
        return barrels.Length > 0;
    }

    /// <summary>
    /// Mirrors ReversePolylineHandler: clone one source line, reverse its
    /// PointCollection (including the parallel Z/M arrays), and replace only
    /// the geometry on the same barrel row. The six CulvertBarrel fields and
    /// the source-row identity remain untouched.
    /// </summary>
    private static bool ReverseGeometryCulvertBarrel(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.Tolerance is not null || mutation.WindowSize is not null || mutation.Passes is not null)
            throw new ArgumentException("Reverse does not accept filter or smoothing parameters.");
        ObservableCulvertBarrel barrel = RequireSingleBarrel(identities, mutation, "Reversing");
        Polyline source = RequireUsableBarrelLine(barrel, "reversing");
        Polyline reversed = source.Clone();
        reversed.Reverse();
        barrel.Polyline = reversed;
        return true;
    }

    /// <summary>
    /// Uses the same Douglas-Peucker reduction and epsilon as
    /// FilterPolylineHandler. The retained handler creates an XY-only
    /// Polyline, so dimensional lines are rejected rather than silently
    /// losing their Z/M arrays. Only the existing row's Polyline is changed.
    /// </summary>
    private static bool FilterGeometryCulvertBarrel(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.WindowSize is not null || mutation.Passes is not null)
            throw new ArgumentException("Filter accepts a tolerance but not smoothing parameters.");
        ObservableCulvertBarrel barrel = RequireSingleBarrel(identities, mutation, "Filtering");
        Polyline source = RequireXYOnlyBarrelLine(barrel, "filtering");
        if (mutation.Tolerance is not double tolerance || !double.IsFinite(tolerance) || tolerance <= 0d)
            throw new ArgumentException("Polyline filter tolerance must be finite and greater than zero.");
        Polyline filtered = new(source.DouglasPeuckerReduction(tolerance));
        EnsureUsableTransformedBarrelLine(filtered, "filtering");
        barrel.Polyline = filtered;
        return true;
    }

    /// <summary>
    /// Uses Polyline.CloneWithMovingAverageSmoothing from the retained source.
    /// That source algorithm creates XY-only output, so dimensional lines are
    /// rejected to prevent an implicit Z/M loss in this native editor.
    /// </summary>
    private static bool SmoothGeometryCulvertBarrel(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.Tolerance is not null)
            throw new ArgumentException("Smoothing accepts a window size and pass count but not a filter tolerance.");
        ObservableCulvertBarrel barrel = RequireSingleBarrel(identities, mutation, "Smoothing");
        Polyline source = RequireXYOnlyBarrelLine(barrel, "smoothing");
        if (mutation.WindowSize is not int windowSize || windowSize < 3
            || mutation.Passes is not int passes || passes < 1)
            throw new ArgumentException("Polyline smoothing requires a window size of at least three and one or more passes.");
        Polyline smoothed = source.CloneWithMovingAverageSmoothing(windowSize, passes);
        EnsureUsableTransformedBarrelLine(smoothed, "smoothing");
        barrel.Polyline = smoothed;
        return true;
    }

    private static ObservableCulvertBarrel RequireSingleBarrel(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation, string action)
    {
        if (mutation.FeatureKind != "barrel" || mutation.FeatureIDs is not { Length: 1 }
            || mutation.Polyline is not null || mutation.DraftID is not null
            || mutation.DeltaX is not null || mutation.DeltaY is not null
            || mutation.CopiedBarrels is not null || mutation.FieldID is not null || mutation.Value is not null)
            throw new ArgumentException(action + " a culvert line requires exactly one source barrel identity.");
        return ResolveRows(identities.Barrels, mutation.FeatureIDs).Single();
    }

    private static Polyline RequireUsableBarrelLine(ObservableCulvertBarrel barrel, string action)
    {
        if (barrel.Polyline is not Polyline line || line.PointCount < 2
            || !double.IsFinite(line.Length) || line.Length <= 0d)
            throw new ArgumentException("The selected culvert line has no usable geometry for " + action + ".");
        return line;
    }

    private static Polyline RequireXYOnlyBarrelLine(ObservableCulvertBarrel barrel, string action)
    {
        Polyline line = RequireUsableBarrelLine(barrel, action);
        if (line.HasZ || line.HasM)
            throw new ArgumentException("The retained source " + action + " operation creates XY-only geometry; select a line without Z or M values.");
        return line;
    }

    private static void EnsureUsableTransformedBarrelLine(Polyline line, string action)
    {
        if (line.PointCount < 2 || line.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            || !double.IsFinite(line.Length) || line.Length <= 0d)
            throw new ArgumentException("The source " + action + " operation would produce an unusable culvert line.");
    }

    private static bool PasteGeometryCulvertBarrels(ModelContext context,
        GeometryCulvertIdentityMap identities, GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "barrel" || mutation.FeatureIDs.Length != 0
            || mutation.CopiedBarrels is not { Length: > 0 } copiedBarrels)
            throw new ArgumentException("Pasting culvert barrels requires copied source features and no destination row IDs.");

        var geometry = GeometryCulvertGeometry(context);
        var draftIDs = new HashSet<string>(StringComparer.Ordinal);
        foreach (GeometryCulvertCopiedBarrel copied in copiedBarrels)
        {
            string draftID = RequireDraftID(copied.DraftID);
            string id = DraftFeatureID("barrel", draftID);
            if (!draftIDs.Add(draftID) || identities.Barrels.ContainsKey(id))
                throw new ArgumentException("The pasted culvert feature identities must be unique.");
            Polyline polyline = ToValidCulvertPolyline(copied.Polyline);
            var created = new ObservableCulvertBarrel(new CulvertBarrel
            {
                Name = copied.Name,
                Polyline = polyline,
                BarrelPropertyName = copied.BarrelPropertyName,
                UpstreamInvert = ParseClipboardNumber(copied.UpstreamInvert),
                DownstreamInvert = ParseClipboardNumber(copied.DownstreamInvert),
                UpstreamOpeningName = copied.UpstreamOpeningName,
                DownstreamOpeningName = copied.DownstreamOpeningName
            });
            geometry.CulvertBarrelLayer.AddFeature(created);
            identities.Barrels.Add(id, created);
        }
        return true;
    }

    private static double ParseClipboardNumber(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            throw new ArgumentException("A copied culvert numeric value is invalid.");
        return parsed;
    }

    /// <summary>
    /// Mirrors OpeningPropertiesDetailEditor.ForceResetKIn/ForceResetKOut.
    /// This operation is deliberately separate from setField so ordinary
    /// numeric input continues to reject NaN and infinity.
    /// </summary>
    private static bool ResetOpeningCoefficient(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "openingProperties" || mutation.FeatureIDs.Length == 0
            || mutation.FieldID is not ("KIn" or "KOut") || mutation.Value is not null)
            throw new ArgumentException("Resetting a culvert opening coefficient requires selected opening-property rows and KIn or KOut.");

        ObservableOpeningProperties[] rows = ResolveRows(identities.OpeningProperties, mutation.FeatureIDs);
        foreach (ObservableOpeningProperties row in rows)
        {
            if (mutation.FieldID == "KIn") row.KIn = double.NaN;
            else row.KOut = double.NaN;
        }
        return rows.Length > 0;
    }

    /// <summary>
    /// Mirrors OpeningPropertiesDetailEditor.ForceSetOpeningTypeCommand. The
    /// source ComboBox invokes this command when a mixed selection chooses a
    /// concrete opening type; only the captured source row identities change.
    /// </summary>
    private static bool ForceSetOpeningType(GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureKind != "openingProperties" || mutation.FeatureIDs.Length == 0
            || mutation.FieldID != "OpeningType" || mutation.Value is null)
            throw new ArgumentException("Forcing an opening type requires selected opening-property rows and a source opening type.");

        BarrelOpeningType openingType = ParseOpeningType(mutation.Value, "OpeningType");
        ObservableOpeningProperties[] rows = ResolveRows(identities.OpeningProperties, mutation.FeatureIDs);
        foreach (ObservableOpeningProperties row in rows) row.OpeningType = openingType;
        return rows.Length > 0;
    }

    private static Polyline ToValidCulvertPolyline(GeometryCulvertCoordinate[]? coordinates)
    {
        if (coordinates is null || coordinates.Length < 2
            || coordinates.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("A culvert line requires at least two finite points.");
        bool hasZ = coordinates.Any(point => point.Z is not null);
        bool hasM = coordinates.Any(point => point.M is not null);
        if ((hasZ && coordinates.Any(point => point.Z is null))
            || (hasM && coordinates.Any(point => point.M is null)))
            throw new ArgumentException("A culvert line must provide Z and M values for every vertex when those dimensions are present.");

        List<Point> points = coordinates.Select(point => new Point(point.X, point.Y)).ToList();
        List<double>? z = hasZ ? coordinates.Select(point => ParseClipboardNumber(point.Z!)).ToList() : null;
        List<double>? m = hasM ? coordinates.Select(point => ParseClipboardNumber(point.M!)).ToList() : null;
        var polyline = new Polyline(points);
        if (z is not null) polyline.TrackZ(z);
        if (m is not null) polyline.TrackM(m);
        if (!double.IsFinite(polyline.Length) || polyline.Length <= 0d)
            throw new ArgumentException("A culvert line must have nonzero length.");
        return polyline;
    }

    private static GeometryCulvertCoordinate[] CoordinatesFor(Polyline polyline) =>
        polyline.Points.Select((point, index) => new GeometryCulvertCoordinate(point.X, point.Y,
            polyline.HasZ ? Format(polyline.Z[index]) : null,
            polyline.HasM ? Format(polyline.M[index]) : null)).ToArray();

    private static bool AddGeometryCulvertPropertySet(ModelContext context, GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureIDs.Length != 0) throw new ArgumentException("Adding a culvert property set does not take source rows.");
        string draftID = RequireDraftID(mutation.DraftID);
        switch (mutation.FeatureKind)
        {
            case "barrelProperties":
            {
                string id = DraftFeatureID(mutation.FeatureKind, draftID);
                if (identities.BarrelProperties.ContainsKey(id)) throw new ArgumentException("The culvert property identity already exists.");
                ObservableBarrelProperties created = GeometryCulvertGeometry(context).BarrelPropertiesLayer.AddNamedFeature("Barrel Properties");
                identities.BarrelProperties.Add(id, created);
                return true;
            }
            case "openingProperties":
            {
                string id = DraftFeatureID(mutation.FeatureKind, draftID);
                if (identities.OpeningProperties.ContainsKey(id)) throw new ArgumentException("The culvert property identity already exists.");
                ObservableOpeningProperties created = GeometryCulvertGeometry(context).OpeningPropertiesLayer.AddNamedFeature("Opening Properties");
                identities.OpeningProperties.Add(id, created);
                return true;
            }
            default: throw new ArgumentException("Only source property-set tables support Add.");
        }
    }

    private static string RequireDraftID(string? draftID)
    {
        if (draftID is null || !Guid.TryParseExact(draftID, "N", out _))
            throw new ArgumentException("A new culvert property set requires a lowercase or uppercase 32-character GUID identity.");
        return draftID.ToLowerInvariant();
    }

    private static bool DeleteGeometryCulvertFeatures(ModelContext context, GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureIDs.Length == 0) throw new ArgumentException("Select at least one culvert row to delete.");
        var geometry = GeometryCulvertGeometry(context);
        switch (mutation.FeatureKind)
        {
            case "barrel":
            {
                ObservableCulvertBarrel[] rows = ResolveRows(identities.Barrels, mutation.FeatureIDs);
                foreach (ObservableCulvertBarrel row in rows)
                {
                    string id = IdentityFor(identities.Barrels, row);
                    geometry.CulvertBarrelLayer.DeleteFeature(row);
                    identities.Barrels.Remove(id);
                }
                return rows.Length > 0;
            }
            case "barrelProperties":
            {
                ObservableBarrelProperties[] rows = ResolveRows(identities.BarrelProperties, mutation.FeatureIDs);
                foreach (ObservableBarrelProperties row in rows)
                {
                    string id = IdentityFor(identities.BarrelProperties, row);
                    geometry.BarrelPropertiesLayer.DeleteFeature(row);
                    identities.BarrelProperties.Remove(id);
                }
                return rows.Length > 0;
            }
            case "openingProperties":
            {
                ObservableOpeningProperties[] rows = ResolveRows(identities.OpeningProperties, mutation.FeatureIDs);
                foreach (ObservableOpeningProperties row in rows)
                {
                    string id = IdentityFor(identities.OpeningProperties, row);
                    geometry.OpeningPropertiesLayer.DeleteFeature(row);
                    identities.OpeningProperties.Remove(id);
                }
                return rows.Length > 0;
            }
            default: throw new ArgumentException("Unknown culvert feature table: " + mutation.FeatureKind);
        }
    }

    private static T[] ResolveRows<T>(Dictionary<string, T> rows, string[] ids)
    {
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("The culvert edit contains duplicate row identities.");
        return ids.Select(id => rows.TryGetValue(id, out T? row) ? row : throw new ArgumentException("The culvert row no longer exists: " + id)).ToArray();
    }

    private static string IdentityFor<T>(Dictionary<string, T> rows, T row) =>
        rows.First(pair => ReferenceEquals(pair.Value, row)).Key;

    private static bool SetGeometryCulvertField(ModelContext context, GeometryCulvertIdentityMap identities,
        GeometryCulvertMutation mutation)
    {
        if (mutation.FeatureIDs.Length == 0) throw new ArgumentException("Select at least one culvert row to edit.");
        string fieldID = mutation.FieldID ?? throw new ArgumentException("Missing culvert field ID.");
        string value = mutation.Value ?? throw new ArgumentException("Missing culvert field value.");
        var geometry = GeometryCulvertGeometry(context);
        switch (mutation.FeatureKind)
        {
            case "barrel":
                foreach (ObservableCulvertBarrel row in ResolveRows(identities.Barrels, mutation.FeatureIDs))
                    SetBarrelField(geometry, row, fieldID, value);
                return true;
            case "barrelProperties":
                foreach (ObservableBarrelProperties row in ResolveRows(identities.BarrelProperties, mutation.FeatureIDs))
                    SetBarrelPropertiesField(row, fieldID, value);
                return true;
            case "openingProperties":
                foreach (ObservableOpeningProperties row in ResolveRows(identities.OpeningProperties, mutation.FeatureIDs))
                    SetOpeningPropertiesField(row, fieldID, value);
                return true;
            default: throw new ArgumentException("Unknown culvert feature table: " + mutation.FeatureKind);
        }
    }

    private static void SetBarrelField(ObservableGeometry geometry, ObservableCulvertBarrel row, string fieldID, string value)
    {
        switch (fieldID)
        {
            case "Name": row.Name = RequireName(value); break;
            case "BarrelPropertyName":
                RequireAssociation(value, geometry.BarrelPropertiesLayer.Features.Select(feature => feature.Name), "barrel properties");
                row.BarrelPropertyName = value;
                break;
            case "UpstreamInvert": row.UpstreamInvert = ParseNumber(value, fieldID); break;
            case "DownstreamInvert": row.DownstreamInvert = ParseNumber(value, fieldID); break;
            case "UpstreamOpeningName":
                RequireAssociation(value, geometry.OpeningPropertiesLayer.Features.Select(feature => feature.Name), "opening properties");
                row.UpstreamOpeningName = value;
                break;
            case "DownstreamOpeningName":
                RequireAssociation(value, geometry.OpeningPropertiesLayer.Features.Select(feature => feature.Name), "opening properties");
                row.DownstreamOpeningName = value;
                break;
            default: throw new ArgumentException("Unsupported culvert barrel field: " + fieldID);
        }
    }

    private static void SetBarrelPropertiesField(ObservableBarrelProperties row, string fieldID, string value)
    {
        switch (fieldID)
        {
            case "Name": row.Name = RequireName(value); break;
            case "Rise": row.Rise = ParsePositiveNumber(value, fieldID); break;
            case "Span": row.Span = ParsePositiveNumber(value, fieldID); break;
            case "Shape": row.Shape = ParseBarrelShape(value, fieldID); break;
            case "Mannings": row.Mannings = ParseNumber(value, fieldID); break;
            case "ManningsBot": row.ManningsBot = ParseOptionalNumber(value, fieldID); break;
            case "ManningsSwitchDepth": row.ManningsSwitchDepth = ParseNumber(value, fieldID); break;
            case "DepthBlocked": row.DepthBlocked = ParseNumber(value, fieldID); break;
            default: throw new ArgumentException("Unsupported barrel property field: " + fieldID);
        }
    }

    private static void SetOpeningPropertiesField(ObservableOpeningProperties row, string fieldID, string value)
    {
        switch (fieldID)
        {
            case "Name": row.Name = RequireName(value); break;
            case "KIn": row.KIn = ParseNumber(value, fieldID); break;
            case "KOut": row.KOut = ParseNumber(value, fieldID); break;
            case "OpeningType": row.OpeningType = ParseOpeningType(value, fieldID); break;
            default: throw new ArgumentException("Unsupported opening property field: " + fieldID);
        }
    }

    private static string RequireName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new ArgumentException("Culvert names must contain visible text and cannot include control characters.");
        return value;
    }

    private static void RequireAssociation(string value, IEnumerable<string> names, string label)
    {
        if (value.Length == 0) return;
        if (!names.Contains(value, StringComparer.Ordinal)) throw new ArgumentException("The selected " + label + " association does not exist.");
    }

    private static double ParseNumber(string raw, string fieldID)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new ArgumentException("The culvert field must contain a finite number: " + fieldID);
        return value;
    }

    private static double ParsePositiveNumber(string raw, string fieldID)
    {
        double value = ParseNumber(raw, fieldID);
        if (value <= 0d) throw new ArgumentOutOfRangeException(fieldID, "The value must be greater than zero.");
        return value;
    }

    private static double ParseOptionalNumber(string raw, string fieldID)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "NaN", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        return ParseNumber(raw, fieldID);
    }

    private static TEnum ParseEnum<TEnum>(string raw, string fieldID) where TEnum : struct, Enum =>
        Enum.TryParse(raw, ignoreCase: false, out TEnum value) && Enum.IsDefined(value)
            ? value
            : throw new ArgumentException("Unknown value for culvert field " + fieldID + ": " + raw);

    private static BarrelShape ParseBarrelShape(string raw, string fieldID)
    {
        BarrelShape shape = ParseEnum<BarrelShape>(raw, fieldID);
        if (shape == BarrelShape.Custom) throw new ArgumentException("Custom barrel shapes are not supported by the source editor.");
        return shape;
    }

    private static BarrelOpeningType ParseOpeningType(string raw, string fieldID)
    {
        BarrelOpeningType opening = ParseEnum<BarrelOpeningType>(raw, fieldID);
        if (opening is BarrelOpeningType.Custom or BarrelOpeningType.None)
            throw new ArgumentException("Custom and None opening types are not supported by the source editor.");
        return opening;
    }

    private static void EnsureGeometryCulvertEditable(ModelContext context)
    {
        if (context.Layer is not Geometry || context.Observable is not ObservableGeometry geometry)
            throw new ArgumentException("The source model is not a geometry.");
        if (!geometry.CanMutate) throw new NotSupportedException("This geometry is a result or is currently computing property tables and cannot be edited.");
    }

    private static ObservableGeometry GeometryCulvertGeometry(ModelContext context)
    {
        if (context.Layer is not Geometry || context.Observable is not ObservableGeometry geometry)
            throw new ArgumentException("The source model is not a geometry.");
        return geometry;
    }

    private static GeometryCulvertSnapshot GeometryCulvertSnapshotFor(ModelContext context, string revision,
        GeometryCulvertDraftState state, GeometryCulvertIdentityMap identities, string[] selectedBarrelIDs,
        string dependencyRevision)
    {
        ObservableGeometry geometry = GeometryCulvertGeometry(context);
        bool canEdit = geometry.CanMutate;
        GeometryCulvertFeatureTable barrels = BarrelTable(geometry, identities);
        GeometryCulvertFeatureTable barrelProperties = BarrelPropertiesTable(geometry, identities);
        GeometryCulvertFeatureTable openingProperties = OpeningPropertiesTable(geometry, identities);
        string[] selection = selectedBarrelIDs.Distinct(StringComparer.Ordinal)
            .Where(identities.Barrels.ContainsKey).ToArray();
        ObservableCulvertBarrel[] selected = ResolveRows(identities.Barrels, selection);
        ObservableTerrain? terrain = geometry.AssociatedTerrain;
        GeometryCulvertProfile[] profiles = selected.Select(barrel => ProfileFor(barrel, terrain, IdentityFor(identities.Barrels, barrel))).ToArray();
        GeometryCulvertExtent? terrainExtent = TerrainExtent(terrain);
        return new GeometryCulvertSnapshot(revision, JsonSerializer.Serialize(state, BackendJson.Options), canEdit,
            state.Mutations.Length > 0, barrels, barrelProperties, openingProperties, terrain?.Name,
            profiles, terrainExtent, dependencyRevision);
    }

    private static GeometryCulvertExtent? TerrainExtent(ObservableTerrain? terrain)
    {
        if (terrain is null) return null;
        return ToGeometryCulvertExtent(terrain.Terrain.ValidExtent);
    }

    private static GeometryCulvertExtent? ToGeometryCulvertExtent(Extent extent) =>
        extent.IsFiniteAndValid()
            ? new GeometryCulvertExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY)
            : null;

    private static (int Width, int Height) TerrainPreviewDimensions(Extent extent, int maxSide)
    {
        double aspect = extent.Width / extent.Height;
        if (!double.IsFinite(aspect) || aspect <= 0d)
            throw new ArgumentException("The associated terrain extent cannot be rendered.");
        if (aspect >= 1d)
            return (maxSide, Math.Clamp((int)Math.Round(maxSide / aspect), 1, maxSide));
        return (Math.Clamp((int)Math.Round(maxSide * aspect), 1, maxSide), maxSide);
    }

    private static GeometryCulvertFeatureTable BarrelTable(ObservableGeometry geometry, GeometryCulvertIdentityMap identities)
    {
        string[] barrelChoices = geometry.BarrelPropertiesLayer.Features.Select(feature => feature.Name).ToArray();
        string[] openingChoices = geometry.OpeningPropertiesLayer.Features.Select(feature => feature.Name).ToArray();
        GeometryCulvertField[] fields =
        [
            new("Name", "Name", "string", []),
            new("BarrelPropertyName", "Barrel Properties", "string", barrelChoices),
            new("UpstreamSampledTerrainElevationDisplay", "Upstream Sampled Terrain", "string", [], true),
            new("UpstreamInvert", "Upstream Barrel Invert", "double", []),
            new("DownstreamSampledTerrainElevationDisplay", "Downstream Sampled Terrain", "string", [], true),
            new("DownstreamInvert", "Downstream Invert", "double", []),
            new("UpstreamOpeningName", "Upstream Opening", "string", openingChoices),
            new("DownstreamOpeningName", "Downstream Opening", "string", openingChoices)
        ];
        GeometryCulvertColumn[] columns =
        [
            new("FID", "FID", "int"), new("Name", "Name", "string"),
            new("BarrelPropertyName", "Barrel Properties", "string"),
            new("UpstreamInvert", "Upstream Invert", "double"),
            new("DownstreamInvert", "Downstream Invert", "double"),
            new("Length", "Length", "double"), new("VertexCount", "Vertices", "int")
        ];
        var rows = geometry.CulvertBarrelLayer.Features.Select((barrel, fid) =>
        {
            string id = IdentityFor(identities.Barrels, barrel);
            Polyline? polyline = barrel.Polyline;
            Point[] points = polyline?.Points.ToArray() ?? [];
            Dictionary<string, string> values = new(StringComparer.Ordinal)
            {
                ["FID"] = fid.ToString(CultureInfo.InvariantCulture), ["Name"] = barrel.Name ?? string.Empty,
                ["BarrelPropertyName"] = barrel.BarrelPropertyName ?? string.Empty, ["UpstreamInvert"] = Format(barrel.UpstreamInvert),
                ["DownstreamInvert"] = Format(barrel.DownstreamInvert), ["Length"] = Format(barrel.Length),
                ["VertexCount"] = points.Length.ToString(CultureInfo.InvariantCulture),
                ["UpstreamOpeningName"] = barrel.UpstreamOpeningName ?? string.Empty,
                ["DownstreamOpeningName"] = barrel.DownstreamOpeningName ?? string.Empty
            };
            if (geometry.AssociatedTerrain is { } terrain && points.Length > 0)
            {
                float[] elevations = SampleTerrain(terrain, [points[0], points[^1]]);
                values["UpstreamSampledTerrainElevationDisplay"] = SampleDisplay(elevations.ElementAtOrDefault(0), terrain.Terrain.NoDataValue);
                values["DownstreamSampledTerrainElevationDisplay"] = SampleDisplay(elevations.ElementAtOrDefault(1), terrain.Terrain.NoDataValue);
            }
            else
            {
                values["UpstreamSampledTerrainElevationDisplay"] = string.Empty;
                values["DownstreamSampledTerrainElevationDisplay"] = string.Empty;
            }
            return new GeometryCulvertFeatureRow(id, fid, values,
                polyline is null ? [] : CoordinatesFor(polyline));
        }).ToArray();
        return new GeometryCulvertFeatureTable("barrel", columns, fields, rows);
    }

    private static GeometryCulvertFeatureTable BarrelPropertiesTable(ObservableGeometry geometry, GeometryCulvertIdentityMap identities)
    {
        GeometryCulvertField[] fields =
        [
            new("Name", "Name", "string", []), new("Rise", "Rise", "double", [], Minimum: 0),
            new("Span", "Span", "double", [], Minimum: 0),
            new("Shape", "Shape", "BarrelShape", Enum.GetNames<BarrelShape>().Where(name => name != nameof(BarrelShape.Custom)).ToArray()),
            new("Mannings", "n Value", "double", []), new("ManningsBot", "n Value (Bottom)", "double", []),
            new("ManningsSwitchDepth", "Depth n Value (Bottom)", "double", []),
            new("DepthBlocked", "Depth Blocked", "double", [])
        ];
        GeometryCulvertColumn[] columns = fields.Select(field => new GeometryCulvertColumn(field.ID, field.Title, field.SourceType)).ToArray();
        var rows = geometry.BarrelPropertiesLayer.Features.Select((properties, fid) => new GeometryCulvertFeatureRow(
            IdentityFor(identities.BarrelProperties, properties), fid,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Name"] = properties.Name, ["Rise"] = Format(properties.Rise), ["Span"] = Format(properties.Span),
                ["Shape"] = properties.Shape.ToString(), ["Mannings"] = Format(properties.Mannings),
                ["ManningsBot"] = Format(properties.ManningsBot), ["ManningsSwitchDepth"] = Format(properties.ManningsSwitchDepth),
                ["DepthBlocked"] = Format(properties.DepthBlocked), ["UseTwoPhaseMannings"] = Format(properties.UseTwoPhaseMannings)
            }, [])).ToArray();
        return new GeometryCulvertFeatureTable("barrelProperties", columns, fields, rows);
    }

    private static GeometryCulvertFeatureTable OpeningPropertiesTable(ObservableGeometry geometry, GeometryCulvertIdentityMap identities)
    {
        GeometryCulvertField[] fields =
        [
            new("Name", "Name", "string", [], ErrorValidationSource: "OpeningPropertiesDetailEditor", ErrorValidationPropertyName: "Name"),
            new("KIn", "K Entrance", "double", [], ShowClearButton: true,
                ClearCommand: "resetOpeningCoefficient", ErrorValidationSource: "OpeningPropertiesDetailEditor", ErrorValidationPropertyName: "KInString"),
            new("KOut", "K Exit", "double", [], ShowClearButton: true,
                ClearCommand: "resetOpeningCoefficient", ErrorValidationSource: "OpeningPropertiesDetailEditor", ErrorValidationPropertyName: "KOutString"),
            new("OpeningType", "Opening Type", "BarrelOpeningType",
                Enum.GetNames<BarrelOpeningType>().Where(name => name is not (nameof(BarrelOpeningType.Custom) or nameof(BarrelOpeningType.None))).ToArray(),
                ForceSetCommand: "forceSetOpeningType")
        ];
        GeometryCulvertColumn[] columns = fields.Select(field => new GeometryCulvertColumn(field.ID, field.Title, field.SourceType)).ToArray();
        var rows = geometry.OpeningPropertiesLayer.Features.Select((properties, fid) => new GeometryCulvertFeatureRow(
            IdentityFor(identities.OpeningProperties, properties), fid,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Name"] = properties.Name, ["KIn"] = OpeningCoefficientText(properties.KIn),
                ["KOut"] = OpeningCoefficientText(properties.KOut),
                ["OpeningType"] = properties.OpeningType.ToString()
            }, [], OpeningPropertyErrors(properties))).ToArray();
        return new GeometryCulvertFeatureTable("openingProperties", columns, fields, rows);
    }

    private static Dictionary<string, string[]> OpeningPropertyErrors(ObservableOpeningProperties properties) =>
        new(StringComparer.Ordinal)
        {
            ["Name"] = OpeningPropertyValidationMessages(properties, "Name", properties.Name),
            // These keys match the ErrorValidationPropertyName values on the
            // source OpeningPropertiesDetailEditor. Its ValidateableTextBox
            // evaluates forwarded attributes, not ObservableValidator.GetErrors:
            // these retained setters do not request automatic validation.
            ["KInString"] = OpeningPropertyValidationMessages(properties, "KIn", OpeningPropertyValidationText(properties.KIn)),
            ["KOutString"] = OpeningPropertyValidationMessages(properties, "KOut", OpeningPropertyValidationText(properties.KOut))
        };

    // OpeningPropertiesDetailEditor formats its textbox with double.ToString().
    // The wire snapshot stays invariant, but retained validation parses the
    // current culture and must receive text produced in that same culture.
    private static string OpeningPropertyValidationText(double value) =>
        double.IsNaN(value) ? "" : value.ToString(CultureInfo.CurrentCulture);

    private static string[] OpeningPropertyValidationMessages(ObservableOpeningProperties properties,
        string propertyName, string text)
    {
        // ValidateableTextBox.GetValidationPropInfo skips empty input by default;
        // the source clear command represents inherited coefficients as NaN/"".
        if (string.IsNullOrEmpty(text)) return [];
        var forwarded = new ForwardedValidationAttribute(typeof(ObservableOpeningProperties), propertyName);
        return ValidationMessages(forwarded.ValidateValues(text, new ValidationContext(properties)));
    }

    private static string[] ValidationMessages(System.Collections.IEnumerable? errors)
    {
        if (errors is null) return [];
        var messages = new List<string>();
        foreach (object? error in errors)
        {
            string? message = error is ValidationResult validation ? validation.ErrorMessage : error?.ToString();
            if (!string.IsNullOrWhiteSpace(message) && !messages.Contains(message, StringComparer.Ordinal))
                messages.Add(message);
        }
        return messages.ToArray();
    }

    private static GeometryCulvertProfile ProfileFor(ObservableCulvertBarrel barrel, ObservableTerrain? terrain, string id)
    {
        if (barrel.Polyline is null || barrel.Polyline.Count < 2 || barrel.Length <= 0d || terrain?.Terrain is null)
            return new GeometryCulvertProfile(id, barrel.Name, barrel.Length, []);
        Polyline line = new(barrel.Polyline);
        line.Densify(line.Length / 256d);
        Point[] points = line.Points.ToArray();
        float[] elevations = SampleTerrain(terrain, points);
        var samples = new List<GeometryCulvertProfileSample>(Math.Min(points.Length, elevations.Length));
        double distance = 0d;
        for (int index = 0; index < Math.Min(points.Length, elevations.Length); index++)
        {
            if (index > 0) distance += Point.Distance(points[index - 1].X, points[index - 1].Y, points[index].X, points[index].Y);
            float elevation = elevations[index];
            if (IsTerrainSample(elevation, terrain.Terrain.NoDataValue))
                samples.Add(new GeometryCulvertProfileSample(distance, points[index].X, points[index].Y, elevation));
        }
        return new GeometryCulvertProfile(id, barrel.Name, line.Length, samples.ToArray());
    }

    private static float[] SampleTerrain(ObservableTerrain terrain, Point[] points)
    {
        var elevations = new float[points.Length];
        terrain.Terrain.SamplePoints(points.AsMemory(), elevations.AsMemory());
        return elevations;
    }

    private static bool IsTerrainSample(float elevation, float? noData) => float.IsFinite(elevation)
        && (!noData.HasValue || elevation != noData.Value);

    private static string SampleDisplay(float elevation, float? noData) => IsTerrainSample(elevation, noData)
        ? elevation.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string OpeningCoefficientText(double value) => double.IsNaN(value) ? string.Empty : Format(value);
    private static string Format(bool value) => value ? "true" : "false";
}

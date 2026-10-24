using System.Globalization;
using Geospatial.Helpers;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Observables;
using Ras.Rendering;
using Utility.Files;

namespace Ras.Backend;

/// <summary>
/// Replays the source vector renderer's hit-test and hover callback for the
/// model layers currently drawn by the native map. It never estimates a
/// feature index from canvas pixels or a display name.
/// </summary>
public static partial class ModelEditorAdapter
{
    internal static async Task<MapHoverIdentifyEntry?> SampleMapHoverModelVectorAsync(
        MapHoverIdentifyRequest request, MapHoverLayerCapture layerCapture)
    {
        MapHoverModelVectorCapture capture = layerCapture.ModelVector
            ?? throw new ArgumentException("The model-vector capture is incomplete.");
        ValidateEnvelope(request, layerCapture, capture);
        return capture.RendererKind switch
        {
            "culvertBarrel" => await SampleCulvertBarrelAsync(request, capture),
            "boundaryConditionLine" => await SampleBoundaryConditionLineAsync(request, capture),
            _ => throw new ArgumentException("The model-vector renderer kind is not supported.")
        };
    }

    private static void ValidateEnvelope(MapHoverIdentifyRequest request,
        MapHoverLayerCapture layerCapture, MapHoverModelVectorCapture capture)
    {
        if (layerCapture.Kind != "modelVector" || layerCapture.Node is null || capture.Node is null ||
            !MapResultRenderService.SameLocator(layerCapture.Node, capture.Node) ||
            string.IsNullOrWhiteSpace(capture.RendererKind) ||
            string.IsNullOrWhiteSpace(capture.SourcePath) ||
            !MapHoverIdentifyService.IsSHA256(capture.SourceRevision) ||
            capture.OwnerMode is not ("savedSource" or "parentDraft" or "resultSource"))
            throw new SourceFileChangedException();

        if (capture.ResultOwner is { } owner)
        {
            if (capture.OwnerMode != "resultSource" || owner.SelectionIndex != 0 ||
                !MapHoverIdentifyService.SamePath(owner.SelectedResultPath, capture.SourcePath) ||
                !owner.SelectedResultRevision.Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
                capture.Model is not null || capture.CulvertDraftState is not null ||
                capture.CulvertDraftSHA256 is not null)
                throw new SourceFileChangedException();
        }
        else if (capture.OwnerMode == "resultSource")
        {
            throw new SourceFileChangedException();
        }

        if (capture.OwnerMode == "parentDraft")
        {
            if (capture.RendererKind != "culvertBarrel" || capture.ResultOwner is not null ||
                string.IsNullOrWhiteSpace(capture.CulvertDraftState) ||
                !MapHoverIdentifyService.IsSHA256(capture.CulvertDraftSHA256) ||
                !string.Equals(Sha256(capture.CulvertDraftState), capture.CulvertDraftSHA256,
                    StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        }
        else if (capture.CulvertDraftState is not null || capture.CulvertDraftSHA256 is not null)
        {
            throw new SourceFileChangedException();
        }
    }

    private static async Task<MapHoverIdentifyEntry?> SampleCulvertBarrelAsync(
        MapHoverIdentifyRequest request, MapHoverModelVectorCapture capture)
    {
        if (capture.Node.NodeType != "CulvertBarrelLayer" || capture.GeometryNode is not { } geometryNode ||
            geometryNode.NodeType != "Geometry" || string.IsNullOrWhiteSpace(capture.GeometryContextToken) ||
            capture.BoundarySourceState is not null || capture.BoundaryLines is not null ||
            capture.BoundaryContextToken is not null)
            throw new SourceFileChangedException();

        MapGeometrySnapshot geometrySnapshot;
        MapResultGeometrySnapshot? resultSnapshot = null;
        if (capture.ResultOwner is { } resultOwner)
        {
            resultSnapshot = await LoadMapResultGeometryAsync(new MapResultGeometryRequest(
                request.Map, resultOwner, geometryNode, capture.GeometryContextToken,
                PageSize: 1, SkipTopologyPage: true, SkipFlowAreasPage: true, SkipCulvertsPage: true));
            geometrySnapshot = resultSnapshot.Geometry;
        }
        else
        {
            geometrySnapshot = await LoadMapGeometryAsync(new MapGeometryRequest(
                request.Map, geometryNode, PageSize: 1,
                ContextToken: capture.GeometryContextToken,
                SkipTopologyPage: true, SkipFlowAreasPage: true, SkipCulvertsPage: true,
                PlanNodes: capture.PlanNodes));
        }

        if (geometrySnapshot.CulvertsState.Locator is not { } culvertLocator ||
            !MapResultRenderService.SameLocator(culvertLocator, capture.Node) ||
            !geometrySnapshot.GeometryChecked || !geometrySnapshot.GeometryEffectiveVisible ||
            !geometrySnapshot.CulvertsState.IsChecked || !geometrySnapshot.CulvertsState.EffectiveVisible ||
            !MapHoverIdentifyService.SamePath(geometrySnapshot.GeometryCanonicalPath, capture.SourcePath) ||
            !geometrySnapshot.GeometryRevision.Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !MapResultRenderService.SameLocator(geometrySnapshot.GeometryNode, geometryNode) ||
            !string.Equals(geometrySnapshot.ContextToken, capture.GeometryContextToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        ModelContext? modelContext = null;
        ResolvedMapResultContext? resultContext = null;
        try
        {
            ObservableGeometry geometry;
            if (resultSnapshot is not null)
            {
                MapResultGeometryOwner owner = capture.ResultOwner!;
                resultContext = MapResultChartService.ResolveContext(request.Map,
                    owner.SelectionOverrides, owner.ContextToken);
                ResolvedMapResultNode selected = ResolveMapResultOwner(resultContext,
                    owner.ResultNode, owner.SelectionIndex);
                RequireExactSelectedResult(selected, owner.SelectedResultPath, owner.SelectedResultRevision);
                var project = new ObservableProject(resultContext.Project,
                    Utility.SynchronizationContext.None);
                geometry = new ObservableResult(project, selected.Result).Geometry;
                if (!MapHoverIdentifyService.SamePath(selected.ResultPath, capture.SourcePath) ||
                    !selected.Revision.Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase))
                    throw new SourceFileChangedException();
            }
            else
            {
                ModelRequest model = capture.Model
                    ?? throw new ArgumentException("A saved Geometry hover capture has no model request.");
                VerifyExactModelRequest(request, capture, model, "Geometry");
                if (!string.Equals(Revision(model.AssetPath), capture.SourceRevision,
                    StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
                modelContext = new ModelContext(model);
                if (capture.OwnerMode == "parentDraft")
                {
                    ModelSaveRequest draftRequest = new(model.AssetPath, "Geometry", model.ProjectPath,
                        capture.SourceRevision, new Dictionary<string, string>(),
                        new Dictionary<string, ImageData[]>(), model.ProjectXml, model.SourceState,
                        GeometryCulvertState: capture.CulvertDraftState,
                        CoreLayerContext: model.CoreLayerContext);
                    ApplyGeometryCulvertDraft(modelContext, draftRequest);
                }
                geometry = modelContext.Observable as ObservableGeometry
                    ?? throw new SourceFileChangedException();
            }

            ObservableCulvertBarrelLayer? barrels = geometry.CulvertBarrelLayer;
            MapHoverIdentifyEntry? entry = null;
            if (barrels is { Features.Count: > 0 })
            {
                var renderer = new PolylineRenderLayer(barrels.AsPolylineCollection());
                renderer.AddHoverHighlight(fid => barrels.Features[fid].Name);
                entry = SourceVectorEntry(renderer, request, capture);
            }
            if (resultContext is not null)
            {
                if (!string.Equals(Revision(capture.SourcePath), capture.SourceRevision,
                    StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
                resultContext.EnsureUnchanged();
            }
            else if (modelContext is not null &&
                     !string.Equals(Revision(modelContext.Layer.Filename), capture.SourceRevision,
                         StringComparison.OrdinalIgnoreCase))
            {
                throw new SourceFileChangedException();
            }
            return entry;
        }
        finally
        {
            modelContext?.Dispose();
            resultContext?.Dispose();
        }
    }

    private static async Task<MapHoverIdentifyEntry?> SampleBoundaryConditionLineAsync(
        MapHoverIdentifyRequest request, MapHoverModelVectorCapture capture)
    {
        if (capture.Node.NodeType != "BoundaryCondition" ||
            capture.BoundarySourceState is not { Length: > 0 } sourceState ||
            capture.BoundaryContextToken is not { Length: > 0 } boundaryContextToken ||
            capture.BoundaryLines is null || capture.GeometryNode is not null ||
            capture.GeometryContextToken is not null || capture.PlanNodes is { Length: > 0 } ||
            capture.CulvertDraftState is not null || capture.CulvertDraftSHA256 is not null)
            throw new SourceFileChangedException();

        ModelRequest? model = capture.Model;
        if (capture.ResultOwner is null)
        {
            if (model is null)
                throw new ArgumentException("A direct Boundary hover capture has no model request.");
            if (capture.OwnerMode != "savedSource" || model.SourceState != sourceState)
                throw new SourceFileChangedException();
            VerifyExactModelRequest(request, capture, model, "Boundary Conditions");
        }
        else if (model is not null)
        {
            throw new SourceFileChangedException();
        }

        MapBoundaryGeometrySnapshot boundarySnapshot = await LoadMapBoundaryGeometryAsync(
            new MapBoundaryGeometryRequest(request.Map, capture.Node, capture.SourceRevision,
                sourceState, boundaryContextToken, capture.ResultOwner));
        if (!MapHoverIdentifyService.SamePath(boundarySnapshot.BoundaryPath, capture.SourcePath) ||
            !boundarySnapshot.BoundaryRevision.Equals(capture.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
            !MapResultRenderService.SameLocator(boundarySnapshot.BoundaryNode, capture.Node) ||
            !boundarySnapshot.BoundaryChecked || !boundarySnapshot.BoundaryEffectiveVisible ||
            !string.Equals(boundarySnapshot.BoundarySourceState, sourceState, StringComparison.Ordinal) ||
            !SameBoundaryLines(boundarySnapshot.Lines, capture.BoundaryLines))
            throw new SourceFileChangedException();

        if (capture.ResultOwner is { } owner)
        {
            using ResolvedMapResultContext resultContext = MapResultChartService.ResolveContext(
                request.Map, owner.SelectionOverrides, owner.ContextToken);
            ResolvedMapResultNode selected = ResolveMapResultOwner(resultContext,
                owner.ResultNode, owner.SelectionIndex);
            RequireExactSelectedResult(selected, owner.SelectedResultPath, owner.SelectedResultRevision);
            BoundaryCondition condition = selected.Result.BoundaryConditions
                ?? throw new SourceFileChangedException();
            MapHoverIdentifyEntry? entry = SourceBoundaryEntry(condition.BoundaryConditionLines,
                request, capture);
            if (!string.Equals(Revision(capture.SourcePath), capture.SourceRevision,
                StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
            resultContext.EnsureUnchanged();
            return entry;
        }

        ModelRequest directModel = model!;
        if (!string.Equals(Revision(directModel.AssetPath), capture.SourceRevision,
            StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
        using (var modelContext = new ModelContext(directModel))
        {
            BoundaryCondition condition = modelContext.Layer as BoundaryCondition
                ?? throw new SourceFileChangedException();
            MapHoverIdentifyEntry? entry = SourceBoundaryEntry(condition.BoundaryConditionLines,
                request, capture);
            if (!string.Equals(Revision(directModel.AssetPath), capture.SourceRevision,
                StringComparison.OrdinalIgnoreCase)) throw new SourceFileChangedException();
            return entry;
        }
    }

    private static MapHoverIdentifyEntry? SourceBoundaryEntry(
        Ras.Layers.BoundaryConditions.BoundaryConditionLineLayer lines,
        MapHoverIdentifyRequest request, MapHoverModelVectorCapture capture)
    {
        var renderer = new BoundaryConditionLineRenderLayer(lines);
        renderer.AddHoverHighlight(fid => "BCL: " + fid.ToString(CultureInfo.InvariantCulture));
        return SourceVectorEntry(renderer, request, capture);
    }

    private static MapHoverIdentifyEntry? SourceVectorEntry(
        VectorRenderLayer renderer, MapHoverIdentifyRequest request,
        MapHoverModelVectorCapture capture)
    {
        HitTestArgs args = HitTestArgs.Generate(new Point(request.Point.X, request.Point.Y),
            request.WorldUnitsPerScreenPoint);
        int ordinal = renderer.HitTest(args, HitTestSortOrder.Distance).FirstOrDefault(-1);
        if (ordinal < 0) return null;
        object cache = new();
        MapHover? hover = renderer.MapHover(args, ref cache)?.FirstOrDefault();
        if (hover is null || string.IsNullOrWhiteSpace(hover.Label)) return null;
        return new MapHoverIdentifyEntry(capture.Node, "modelVector", hover.Label,
            ordinal.ToString(CultureInfo.InvariantCulture), capture.RendererKind,
            capture.SourcePath, capture.SourceRevision, null, null, null, ordinal);
    }

    private static bool SameBoundaryLines(MapBoundaryLineData[] actual,
        MapHoverModelVectorLine[] expected)
    {
        MapBoundaryLineData[] visible = actual.Where(line => line.Coordinates is { Length: >= 2 }).ToArray();
        if (visible.Length != expected.Length) return false;
        for (int index = 0; index < visible.Length; index++)
        {
            MapBoundaryLineData left = visible[index];
            MapHoverModelVectorLine right = expected[index];
            if (left.SourceOrdinal != right.SourceOrdinal || left.Id != right.ID || left.Name != right.Name ||
                left.Coordinates is not { } coordinates || coordinates.Length != right.Coordinates.Length)
                return false;
            for (int point = 0; point < coordinates.Length; point++)
                if (coordinates[point].X != right.Coordinates[point].X ||
                    coordinates[point].Y != right.Coordinates[point].Y)
                    return false;
        }
        return true;
    }

    private static void VerifyExactModelRequest(MapHoverIdentifyRequest request,
        MapHoverModelVectorCapture capture, ModelRequest model, string kind)
    {
        if (model.Kind != kind || string.IsNullOrWhiteSpace(model.AssetPath) ||
            string.IsNullOrWhiteSpace(model.ProjectPath) ||
            !MapHoverIdentifyService.SamePath(model.AssetPath, capture.SourcePath) ||
            !MapHoverIdentifyService.SamePath(model.ProjectPath, request.Map.ProjectPath) ||
            !string.Equals(Sha256(model.ProjectXml ?? string.Empty), request.Map.ProjectXmlSHA,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(model.CoreLayerContext?.IdentityToken,
                request.Map.CoreLayerContext?.IdentityToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

}

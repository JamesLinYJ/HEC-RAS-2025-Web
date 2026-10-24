using System.Globalization;
using Ras.Engine.Structures.Culverts;
using Ras.Layers;
using Ras.Output;
using Utility.Files;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    /// <summary>
    /// Reads the stored culvert profile from the exact selected Result HDF
    /// and its embedded Geometry. MultiResult uses the current keyframe in
    /// slot zero, matching the retained animation context.
    /// </summary>
    public static MapResultCulvertProfileSnapshot LoadMapResultCulvertProfile(
        MapResultCulvertProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMapResultCulvertProfileRequest(request);

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(request.Map,
            request.SelectionOverrides, request.ContextToken);
        ResolvedMapResultNode selected = ResolveMapResultOwner(context, request.ResultNode,
            request.SelectionIndex);
        CatalogTreeNode resultRow = ResolveExactMapNode(context.Tree.Roots, request.ResultNode);
        CatalogTreeNode geometryRow = FindSingleResultGeometryRow(resultRow, selected);

        Geometry geometry = selected.Result.Geometry ?? throw new InvalidDataException(
            "The selected Result has no embedded Geometry.");
        if (!string.Equals(geometry.Name, geometryRow.Name, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string resultPath = SourceFilePaths.Canonical(selected.ResultPath);
        string revision = selected.Revision;
        string? selectionMode = selected.ActiveNode.Selections
            .SingleOrDefault(slot => slot.SelectionIndex == 0)?.SelectionMode;
        if (selectionMode is null) throw new SourceFileChangedException();

        MapResultCulvertBarrel[] barrels = [];
        bool geometryAvailable = false;
        string? diagnostic = null;
        CulvertBarrelLayer? sourceBarrels = geometry.CulvertBarrelLayer;
        if (sourceBarrels is null)
        {
            diagnostic = "The selected Result has no culvert barrel geometry.";
        }
        else
        {
            geometryAvailable = true;
            barrels = sourceBarrels.Features.Select((barrel, ordinal) =>
            {
                if (barrel.Polyline is null || barrel.Polyline.Points.Count < 2 ||
                    barrel.Polyline.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                    throw new InvalidDataException("The selected Result contains invalid culvert barrel geometry.");
                return new MapResultCulvertBarrel(
                    "barrel:source:" + ordinal.ToString(CultureInfo.InvariantCulture), ordinal,
                    barrel.Name ?? string.Empty,
                    barrel.Polyline.Points.Select(point => new MapResultCulvertCoordinate(point.X, point.Y)).ToArray(),
                    barrel.BarrelPropertyName ?? string.Empty,
                    barrel.UpstreamInvert.ToString("R", CultureInfo.InvariantCulture),
                    barrel.DownstreamInvert.ToString("R", CultureInfo.InvariantCulture),
                    barrel.UpstreamOpeningName ?? string.Empty,
                    barrel.DownstreamOpeningName ?? string.Empty);
            }).ToArray();
        }

        int profileCount = 0;
        string[] profileNames = [];
        double[] flow = [];
        double[] outsideDepth = [];
        double[] outletVelocity = [];
        bool outputAvailable = false;
        if (selected.Result.BaseOutputBlock is OutputBlock output)
        {
            profileCount = output.ProfileCount;
            profileNames = output.ProfileNames?.ToArray() ?? [];
            if (profileNames.Length != profileCount)
                throw new InvalidDataException("The selected Result contains inconsistent culvert profile metadata.");
            if (request.ProfileIndex >= profileCount)
                throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex),
                    "The requested culvert profile is outside the selected Result's stored profile range.");
            if (output.TryReadCulvertData(request.ProfileIndex, out CulvertTimestepData? data) && data is not null)
            {
                if (data.Flow is null || data.OutsideDepth is null || data.OutletVelocity is null ||
                    data.Flow.Length == 0 || data.OutsideDepth.Length != data.Flow.Length * 2 ||
                    data.OutletVelocity.Length != data.Flow.Length ||
                    (geometryAvailable && data.Flow.Length != barrels.Length))
                {
                    diagnostic = "The stored culvert output does not match the embedded barrel geometry.";
                }
                else if (data.Flow.Any(value => !float.IsFinite(value)) ||
                         data.OutsideDepth.Any(value => !float.IsFinite(value)) ||
                         data.OutletVelocity.Any(value => !float.IsFinite(value)))
                {
                    diagnostic = "The selected Result contains non-finite culvert output values.";
                }
                else
                {
                    outputAvailable = true;
                    flow = data.Flow.Select(value => (double)value).ToArray();
                    outsideDepth = data.OutsideDepth.Select(value => (double)value).ToArray();
                    outletVelocity = data.OutletVelocity.Select(value => (double)value).ToArray();
                }
            }
            else if (diagnostic is null)
            {
                diagnostic = "The selected Result has no stored culvert profile data.";
            }
        }
        else if (diagnostic is null)
        {
            diagnostic = "The selected Result has no stored culvert profile data.";
        }

        context.EnsureUnchanged();
        _ = ResolveMapResultOwner(context, request.ResultNode, request.SelectionIndex);
        _ = FindSingleResultGeometryRow(
            ResolveExactMapNode(context.Tree.Roots, request.ResultNode), selected);
        if (Revision(resultPath) != revision) throw new SourceFileChangedException();

        return new MapResultCulvertProfileSnapshot(
            SourceFilePaths.Canonical(context.ProjectPath), context.ProjectRevision,
            context.ProjectXmlSHA, context.VisualizationRevision, context.Sources.MapID,
            context.Sources.MapIndex, context.Sources.MapName, context.Sources.MapType,
            context.Sources.ContextToken, request.ResultNode, request.SelectionIndex,
            selectionMode, resultPath, revision, selected.SelectedMapType,
            request.ProfileIndex, profileCount, profileNames, geometry.Name,
            resultPath, revision, geometryRow.Locator, geometryAvailable,
            outputAvailable, diagnostic, barrels, flow, outsideDepth, outletVelocity);
    }

    private static void ValidateMapResultCulvertProfileRequest(MapResultCulvertProfileRequest request)
    {
        if (request.Map is null || request.ResultNode is null ||
            string.IsNullOrWhiteSpace(request.ContextToken))
            throw new ArgumentException("A saved map, active Result context and exact Result row are required.");
        if (request.SelectionIndex != 0 || request.ResultNode.NodeType is not ("Result" or "MultiResult"))
            throw new ArgumentException("Stored culvert profiles use the exact current Result keyframe at selection slot zero.");
        if (request.ProfileIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex));
    }

    private static CatalogTreeNode FindSingleResultGeometryRow(CatalogTreeNode resultRow,
        ResolvedMapResultNode selected)
    {
        CatalogTreeNode[] rows = (resultRow.Children ?? []).Where(child =>
            child.Locator?.NodeType == "Geometry").ToArray();
        if (rows.Length != 1) throw new SourceFileChangedException();
        return ResolveResultOwnedChild(resultRow, rows[0].Locator!, selected.ResultPath,
            selected.Revision, "Geometry");
    }
}

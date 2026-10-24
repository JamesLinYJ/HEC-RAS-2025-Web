using System.Globalization;
using Ras.Engine.Structures.Culverts;
using Ras.Hydraulics.Culverts;
using Ras.Layers;
using Ras.Output;
using Utility.Files;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    private const double CulvertTimeSeriesNoDataValue = -9999.0;
    private static readonly string[] CulvertTimeSeriesPalette =
        ["FF0000FF", "FFFF0000", "FF008000", "FFFFA500", "FF800080", "FF008080", "FFA52A2A", "FFFF00FF"];

    /// <summary>
    /// Returns the retained Result OutputBlock time axis and the selected
    /// source-ordinal barrel's stored Flow or OutletVelocity at every time.
    /// Values and units are passed through unchanged; -9999 remains the
    /// source chart's NoData sentinel.
    /// </summary>
    public static MapResultCulvertTimeSeriesResult LoadMapResultCulvertTimeSeries(
        MapResultCulvertTimeSeriesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMapResultCulvertTimeSeriesRequest(request);

        string projectPath = SourceFilePaths.Canonical(request.Map.ProjectPath);
        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(
            request.Map, projectPath);
        if (readLease?.IsProtected(request.ResultOwner.SelectedResultPath) == true)
            throw new SourceFileChangedException();

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, request.ResultOwner.SelectionOverrides, request.ResultOwner.ContextToken);
        ResolvedMapResultNode selected = ResolveMapResultOwner(context,
            request.ResultOwner.ResultNode, request.ResultOwner.SelectionIndex);
        RequireExactSelectedResult(selected, request.ResultOwner.SelectedResultPath,
            request.ResultOwner.SelectedResultRevision);
        if (readLease?.IsProtected(selected.ResultPath) == true)
            throw new SourceFileChangedException();

        CatalogTreeNode resultRow = ResolveExactMapNode(context.Tree.Roots,
            request.ResultOwner.ResultNode);
        CatalogTreeNode geometryRow = FindSingleResultGeometryRow(resultRow, selected);
        Geometry geometry = selected.Result.Geometry ?? throw new InvalidDataException(
            "The selected Result has no embedded Geometry.");
        if (!string.Equals(geometry.Name, geometryRow.Name, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        CulvertBarrelLayer layer = geometry.CulvertBarrelLayer
            ?? throw new InvalidDataException("The selected Result has no culvert barrel geometry.");
        CulvertBarrel[] barrels = layer.Features.ToArray();
        if (barrels.Length == 0 || barrels.Any(barrel => barrel.Polyline is null ||
            barrel.Polyline.Points.Count < 2 ||
            barrel.Polyline.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))))
            throw new InvalidDataException("The selected Result contains no usable culvert barrel geometry.");

        OutputBlock output = selected.Result.BaseOutputBlock as OutputBlock
            ?? throw new InvalidDataException("The selected Result has no readable stored culvert OutputBlock.");
        DefaultUnits units = DefaultUnits.FromDefault(selected.Result.EmbeddedUnitSystem);
        string valueUnits = request.Variable == "Flow" ? units.FlowUnits : units.ShortVelocityUnits;
        string resultPath = SourceFilePaths.Canonical(selected.ResultPath);
        string resultRevision = selected.Revision;
        MapResultChartService.EnsureUnchanged(resultPath, resultRevision);
        context.EnsureUnchanged();
        ValidateCoreLayerSourceContext(request.Map.CoreLayerContext, context.ProjectPath);

        IReadOnlyList<DateTime> sourceTimes = output.Times;
        if (sourceTimes.Count == 0 || sourceTimes.Count != output.ProfileCount ||
            output.CulvertBarrelCount != barrels.Length)
            throw new InvalidDataException("The stored culvert time axis and source barrel arrays do not match.");

        var selectedBarrels = request.BarrelIDs.Select(id =>
        {
            int ordinal = ParseBarrelID(id);
            if (ordinal >= barrels.Length ||
                !string.Equals(id, "barrel:source:" + ordinal.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
                throw new ArgumentException("A selected culvert barrel no longer matches its source ordinal.",
                    nameof(request.BarrelIDs));
            return (ID: id, Ordinal: ordinal, Barrel: barrels[ordinal]);
        }).ToArray();

        var valuesByBarrel = selectedBarrels.Select(_ => new double[sourceTimes.Count]).ToArray();
        for (int timeIndex = 0; timeIndex < sourceTimes.Count; timeIndex++)
        {
            if (!output.TryReadCulvertData(timeIndex, out CulvertTimestepData? timestep) || timestep is null)
                throw new InvalidDataException("The selected Result contains an unreadable stored culvert timestep.");
            float[] sourceValues = request.Variable == "Flow" ? timestep.Flow : timestep.OutletVelocity;
            if (sourceValues is null || sourceValues.Length != barrels.Length ||
                sourceValues.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("The stored culvert output does not match the source barrel geometry.");
            for (int seriesIndex = 0; seriesIndex < selectedBarrels.Length; seriesIndex++)
                valuesByBarrel[seriesIndex][timeIndex] = sourceValues[selectedBarrels[seriesIndex].Ordinal];
        }

        ValidateCoreLayerSourceContext(request.Map.CoreLayerContext, context.ProjectPath);
        context.EnsureUnchanged();
        _ = ResolveMapResultOwner(context, request.ResultOwner.ResultNode,
            request.ResultOwner.SelectionIndex);
        CatalogTreeNode finalResultRow = ResolveExactMapNode(context.Tree.Roots,
            request.ResultOwner.ResultNode);
        _ = FindSingleResultGeometryRow(finalResultRow, selected);
        MapResultChartService.EnsureUnchanged(resultPath, resultRevision);

        return new MapResultCulvertTimeSeriesResult(
            SourceFilePaths.Canonical(context.ProjectPath), context.ProjectRevision,
            context.ProjectXmlSHA, context.VisualizationRevision,
            request.Map.CoreLayerContext?.IdentityToken ?? string.Empty,
            context.Sources.MapID, context.Sources.MapIndex, context.Sources.MapName,
            context.Sources.MapType, context.Sources.ContextToken,
            request.ResultOwner.ResultNode, request.ResultOwner.SelectionIndex,
            resultPath, resultRevision, request.Variable, valueUnits,
            sourceTimes.Select(time => time.ToString("O", CultureInfo.InvariantCulture)).ToArray(),
            CulvertTimeSeriesNoDataValue,
            selectedBarrels.Select((item, index) => new MapResultCulvertTimeSeriesDataset(
                item.ID, item.Barrel.Name ?? string.Empty, item.Ordinal,
                CulvertTimeSeriesPalette[item.Ordinal % CulvertTimeSeriesPalette.Length],
                2.0, valuesByBarrel[index])).ToArray());
    }

    private static void ValidateMapResultCulvertTimeSeriesRequest(
        MapResultCulvertTimeSeriesRequest request)
    {
        if (request.Map is null || request.ResultOwner is null ||
            string.IsNullOrWhiteSpace(request.ResultOwner.ContextToken) ||
            string.IsNullOrWhiteSpace(request.ResultOwner.SelectedResultPath) ||
            string.IsNullOrWhiteSpace(request.ResultOwner.SelectedResultRevision))
            throw new ArgumentException("An exact saved map and Result owner are required.");
        if (request.ResultOwner.SelectionIndex != 0 ||
            request.ResultOwner.ResultNode.NodeType is not ("Result" or "MultiResult"))
            throw new ArgumentException("Culvert time series use the exact current Result keyframe in selection slot zero.");
        if (request.Variable is not ("Flow" or "Velocity"))
            throw new ArgumentException("Culvert time series support only source Flow or OutletVelocity values.");
        if (request.BarrelIDs is null || request.BarrelIDs.Length == 0 ||
            request.BarrelIDs.Any(string.IsNullOrWhiteSpace) ||
            request.BarrelIDs.Distinct(StringComparer.Ordinal).Count() != request.BarrelIDs.Length)
            throw new ArgumentException("Choose one or more distinct source-ordinal culvert barrels.",
                nameof(request.BarrelIDs));
    }

    private static int ParseBarrelID(string id)
    {
        const string prefix = "barrel:source:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out int ordinal) || ordinal < 0)
            throw new ArgumentException("A selected culvert barrel ID is invalid.", nameof(id));
        return ordinal;
    }

    private static void ValidateCoreLayerSourceContext(CoreLayerRequestContext? context,
        string projectPath)
    {
        if (context is not null)
            CoreLayerImportService.ValidateContext(context, projectPath);
    }
}

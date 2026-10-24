using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Transformers;
using Geospatial.Vectors;

namespace Ras.Backend;

public sealed record RasterMapSourceIdentity(
    string Id, string ProjectPath, string VisualizationPath, string VisualizationRevision,
    int MapIndex, string MapName, string MapType, int[] NodePath, string NodeName,
    string MapLayerType, string? Filename, string? FilenameRelative, string SourcePath,
    string? SourceMapID = null, string? SourceRevision = null,
    GdbRasterSymbologyIdentity? GdbSource = null);

/// <summary>
/// Exact source owner for a raster stored as a subdataset in a file geodatabase.
/// The directory manifest and persisted map-tree locator are required alongside
/// the outer raster descriptor; a connection string alone is not an identity.
/// </summary>
public sealed record GdbRasterSymbologyIdentity(
    MapTreeRequest Map,
    CatalogTreeNodeLocator Node,
    GdbMapSourceIdentity Source);

public sealed record RasterMapViewExtent(double MinX, double MinY, double MaxX, double MaxY,
    int PixelWidth = 1024, int PixelHeight = 1024);
public sealed record RasterSymbologyColorStop(Guid Id, string Color, double Value);
public sealed record RasterHillshadePropertiesContract(bool IsEnabled, float ZFactor, float Azimuth, float Zenith);
public sealed record RasterContourPropertiesContract(bool IsEnabled, bool AutoInterval, float Interval,
    int IndexEvery, string Color, int Opacity, bool ShowLabels, float Density);
public sealed record RasterSymbologyDraftContract(string? PaletteID, string Name,
    RasterSymbologyColorStop[] Stops, string ValueSource, bool Grayscale, bool IsEditing,
    bool UpdateHistogramWithView, bool CanComputeHistogram,
    byte? Alpha = null, bool? IsSurfaceFillEnabled = null, float? SmoothingRadius = null,
    float? ResampleCellSizeRatio = null, RasterHillshadePropertiesContract? Hillshade = null,
    RasterContourPropertiesContract? Contours = null);
public sealed record RasterSymbologyPalette(string Id, string Name, string[] Colors,
    double[]? Values = null, bool IsCustom = false);
public sealed record RasterSymbologySnapshotContract(RasterSymbologyDraftContract Draft,
    RasterSymbologyPalette[] Palettes, double[]? SurfaceFillValues,
    RasterPlotOptionDescriptor[] PlotOptions);
public sealed record RasterPlotOptionChoice(string Value, string Label);
public sealed record RasterPlotOptionDescriptor(string PropertyName, string Name, string Label,
    string Description, string Kind, string Value, RasterPlotOptionChoice[] EnumValues,
    bool IsEnabled, string? EnabledIf);
public sealed record RasterMapLayerPayload(string Id, string? SourceMapID, string Name, int Width,
    int Height, byte[] Rgba, double MinX, double MinY, double MaxX, double MaxY);
public sealed record RasterSymbologyInspectResult(string PreviousVisualizationRevision,
    string VisualizationRevision, string SourceRevision, RasterSymbologySnapshotContract Snapshot,
    RasterMapLayerPayload Raster, RasterImportContextSource? RasterInfoStats = null);
public sealed record RasterSymbologySourceExtentResult(string VisualizationRevision,
    string SourceRevision, double[] SourceExtent);
public sealed record RasterSymbologySnapshotResult(string PreviousVisualizationRevision,
    string VisualizationRevision, string SourceRevision, RasterSymbologySnapshotContract Snapshot);
public sealed record RasterSymbologyDraftResult(RasterSymbologyDraftContract Draft);
public sealed record RasterSymbologyApplyResult(string PreviousVisualizationRevision,
    string VisualizationRevision, string SourceRevision, RasterMapLayerPayload Raster);
public sealed record RasterSymbologyPalettesResult(string PreviousVisualizationRevision,
    string VisualizationRevision, string SourceRevision, RasterSymbologyPalette[] Palettes);
public sealed record RasterSymbologyRequest(RasterMapSourceIdentity Source, string? ProjectXml = null);
public sealed record RasterSymbologyHistogramRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, RasterMapViewExtent? Viewport, int Count, bool ForView);
public sealed record RasterSymbologySelectPaletteRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, string PaletteID, RasterSymbologyDraftContract Draft);
public sealed record RasterSymbologyApplyRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, RasterSymbologyDraftContract Draft,
    Dictionary<string, string>? PlotOptions = null);
public sealed record RasterSymbologySavePaletteRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, RasterSymbologyDraftContract Draft, string Name);
public sealed record RasterSymbologyDeletePaletteRequest(RasterMapSourceIdentity Source,
    string? ProjectXml, string PaletteID);

/// <summary>
/// Source-backed operations for the current project's `.vis` single-band raster
/// layers. Every request re-resolves its exact map/node identity from disk before
/// reading a source raster or replacing any visualization XML.
/// </summary>
public static partial class RasterSymbologyService
{
    private const int MaximumRasterSide = 1536;
    private const long MaximumVisualizationBytes = 32L * 1024 * 1024;
    private const string TemporaryPalettePrefix = "__";

    public static RasterSymbologyInspectResult Load(RasterSymbologyRequest request) => Inspect(request, includeRasterInfoStats: true);

    public static RasterSymbologyInspectResult Inspect(RasterSymbologyRequest request) => Inspect(request, includeRasterInfoStats: false);

    /// <summary>Reads physical map bounds without rendering or returning pixels.</summary>
    public static RasterSymbologySourceExtentResult SourceExtent(RasterSymbologyRequest request)
    {
        return WithContext<RasterSymbologySourceExtentResult>(request.Source, request.ProjectXml, context =>
        {
            Extent extent = context.Raster.ValidExtent;
            if (!extent.IsFiniteAndValid())
                throw new NotSupportedException("The raster has no finite map extent.");
            VerifySourceRevision(context);
            return new(context.VisualizationRevision, context.SourceRevision,
                [extent.MinX, extent.MinY, extent.MaxX, extent.MaxY]);
        });
    }

    private static RasterSymbologyInspectResult Inspect(RasterSymbologyRequest request, bool includeRasterInfoStats)
    {
        return WithContext<RasterSymbologyInspectResult>(request.Source, request.ProjectXml, context =>
        {
            var symbology = ReadSymbology(context.SourceNode.Element("RasterSymbology"), context.Raster,
                context.SourceRoot, out string valueSource);
            Dictionary<string, string> plotOptions = ResolveSavedRasterPlotOptions(context, symbology);
            ApplyRasterPlotOptionsToSymbology(context.Raster, symbology, plotOptions);
            var snapshot = MakeSnapshot(context, symbology, valueSource, plotOptions);
            var raster = Render(context, symbology, null, plotOptions);
            RasterImportContextSource? rasterInfoStats = null;
            if (includeRasterInfoStats && context.GdbSource is null)
            {
                rasterInfoStats = RasterImportContextPropertiesService.Inspect(
                    new RasterImportContextPropertiesRequest([context.SourcePath])).Sources.Single();
                if (!PathEquals(rasterInfoStats.Path, context.SourcePath))
                    throw new SourceFileChangedException();
                // Statistics carry the properties helper's root-file SHA. The
                // map source identity uses GDAL's full local dataset manifest,
                // so expose that stable revision after the full post-read check.
                rasterInfoStats = rasterInfoStats with { Revision = context.SourceRevision };
            }
            VerifySourceRevision(context);
            return new(context.PreviousVisualizationRevision, context.VisualizationRevision,
                context.SourceRevision, snapshot, raster, rasterInfoStats);
        });
    }

    public static double[] Histogram(RasterSymbologyHistogramRequest request)
    {
        ValidateCount(request.Count);
        return WithContext<double[]>(request.Source, request.ProjectXml, context =>
        {
            if (request.ForView)
            {
                if (request.Viewport is not RasterMapViewExtent viewport)
                    throw new ArgumentException("A current map extent is required to update the histogram for this view.");
                ValidateExtent(viewport);
                var requestedExtent = new Extent(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY);
                double[] values = InterpolatedSurfaceFill.EstimateColorValuesFrom(context.Raster, requestedExtent, request.Count).ToArray();
                VerifySourceRevision(context);
                return values;
            }
            double[] histogramValues = InterpolatedSurfaceFill.EstimateColorValuesFrom(context.Raster, request.Count).ToArray();
            VerifySourceRevision(context);
            return histogramValues;
        });
    }

    public static RasterSymbologyDraftResult SelectPalette(RasterSymbologySelectPaletteRequest request)
    {
        return WithContext<RasterSymbologyDraftResult>(request.Source, request.ProjectXml, context =>
        {
            var palettes = ReadPalettes(context.SourceRoot);
            var palette = palettes.FirstOrDefault(candidate => candidate.Id == request.PaletteID)
                ?? throw new ArgumentException("The selected raster palette is no longer available.");
            var colors = palette.Colors;
            if (colors.Length == 0) throw new ArgumentException("The selected raster palette has no colors.");
            double[] values;
            if (string.Equals(request.Draft.ValueSource, "Histogram", StringComparison.OrdinalIgnoreCase))
            {
                values = InterpolatedSurfaceFill.EstimateColorValuesFrom(context.Raster, colors.Length).ToArray();
            }
            else if (palette.Values is { Length: > 1 } savedValues && savedValues.Length == colors.Length)
            {
                values = savedValues;
            }
            else if (request.Draft.Stops.Length > 1)
            {
                double first = request.Draft.Stops[0].Value;
                double last = request.Draft.Stops[^1].Value;
                values = Enumerable.Range(0, colors.Length)
                    .Select(index => first + (last - first) * index / (colors.Length - 1d)).ToArray();
            }
            else
            {
                values = InterpolatedSurfaceFill.EstimateColorValuesFrom(context.Raster, colors.Length).ToArray();
            }
            var draft = request.Draft with
            {
                PaletteID = palette.Id,
                Name = palette.Name,
                Stops = colors.Select((color, index) => new RasterSymbologyColorStop(
                    index < request.Draft.Stops.Length ? request.Draft.Stops[index].Id : Guid.NewGuid(),
                    color, values[index])).ToArray(),
                Grayscale = palette.Id.Equals("Grayscale", StringComparison.OrdinalIgnoreCase)
            };
            VerifySourceRevision(context);
            return new(draft);
        });
    }

    public static RasterSymbologySnapshotResult Reset(RasterSymbologyRequest request)
    {
        return WithContext<RasterSymbologySnapshotResult>(request.Source, request.ProjectXml, context =>
        {
            var defaultLayer = new RasterRenderLayer(context.Raster);
            RasterSymbology defaultSymbology = defaultLayer.GetDefaultTerrainHistoSymbology();
            var snapshot = MakeSnapshot(context, defaultSymbology, "Histogram");
            VerifySourceRevision(context);
            return new(context.PreviousVisualizationRevision, context.VisualizationRevision,
                context.SourceRevision, snapshot);
        });
    }

    public static RasterSymbologyApplyResult Apply(RasterSymbologyApplyRequest request)
    {
        ValidateDraft(request.Draft);
        return WithContext<RasterSymbologyApplyResult>(request.Source, request.ProjectXml, context =>
        {
            var symbology = ApplyDraftToCurrent(context.SourceNode.Element("RasterSymbology"), request.Draft,
                context.Raster, context.SourceRoot);
            Dictionary<string, string> plotOptions = request.PlotOptions is null
                ? ResolveSavedRasterPlotOptions(context, symbology)
                : new Dictionary<string, string>(request.PlotOptions, StringComparer.Ordinal);
            ApplyRasterPlotOptionsToSymbology(context.Raster, symbology, plotOptions);
            RasterMapLayerPayload raster = Render(context, symbology, null, plotOptions);
            VerifySourceRevision(context);
            WriteRasterSymbology(context.SourceNode, request.Draft, symbology);
            WriteRasterPlotOptions(context.SourceNode, context.Raster, symbology, plotOptions);
            string newRevision = Commit(context);
            return new(context.PreviousVisualizationRevision, newRevision, context.SourceRevision, raster);
        });
    }

    public static RasterSymbologyPalettesResult SavePalette(RasterSymbologySavePaletteRequest request)
    {
        ValidateDraft(request.Draft);
        string name = ValidatePaletteName(request.Name);
        return WithContext<RasterSymbologyPalettesResult>(request.Source, request.ProjectXml, context =>
        {
            var all = ReadPalettes(context.SourceRoot);
            if (all.Any(palette => !palette.IsCustom && palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A default palette already uses that name.");
            var root = EnsureSymbologyRoot(context.VisualizationRoot);
            root.Elements("CustomSymbology").Where(element =>
                string.Equals(element.Attribute("Name")?.Value, name, StringComparison.OrdinalIgnoreCase)).Remove();
            var paletteNode = new XElement("CustomSymbology",
                new XAttribute("Name", name),
                new XAttribute("ValueSource", NormalizeValueSource(request.Draft.ValueSource)),
                new XAttribute("IsChanged", true));
            for (int index = 0; index < request.Draft.Stops.Length; index++)
            {
                var stop = request.Draft.Stops[index];
                var color = ParseColor(stop.Color);
                var node = new XElement("Color", new XAttribute("R", color.R),
                    new XAttribute("G", color.G), new XAttribute("B", color.B));
                if (ValueIsPersisted(request.Draft.ValueSource, index, request.Draft.Stops.Length))
                    node.SetAttributeValue("Value", stop.Value.ToString("R", CultureInfo.InvariantCulture));
                paletteNode.Add(node);
            }
            root.Add(paletteNode);
            VerifySourceRevision(context);
            string newRevision = Commit(context);
            return new(context.PreviousVisualizationRevision, newRevision, context.SourceRevision,
                ReadPalettes(context.Visualization));
        });
    }

    public static RasterSymbologyPalettesResult DeletePalette(RasterSymbologyDeletePaletteRequest request)
    {
        return WithContext<RasterSymbologyPalettesResult>(request.Source, request.ProjectXml, context =>
        {
            var root = context.VisualizationRoot.Element("Symbology");
            var target = root?.Elements("CustomSymbology").FirstOrDefault(element =>
                PaletteID(element.Attribute("Name")?.Value ?? "") == request.PaletteID ||
                string.Equals(element.Attribute("Name")?.Value, request.PaletteID, StringComparison.Ordinal));
            if (target is null) throw new ArgumentException("Only a saved custom palette can be deleted.");
            target.Remove();
            VerifySourceRevision(context);
            string newRevision = Commit(context);
            return new(context.PreviousVisualizationRevision, newRevision, context.SourceRevision,
                ReadPalettes(context.Visualization));
        });
    }

    private static RasterSymbologySnapshotContract MakeSnapshot(OperationContext context,
        RasterSymbology symbology, string valueSource, Dictionary<string, string>? plotOptions = null)
    {
        var surfaceFill = symbology.SurfaceFill;
        if (surfaceFill is null || surfaceFill.Colors.Count == 0 || surfaceFill.ColorValues.Length != surfaceFill.Colors.Count)
            throw new InvalidDataException("The raster layer has no usable surface-fill values.");
        var palettes = ReadPalettes(context.SourceRoot);
        string? paletteID = palettes.FirstOrDefault(palette => palette.Name.Equals(surfaceFill.Name ?? "", StringComparison.OrdinalIgnoreCase))?.Id;
        var stops = surfaceFill.Colors.Select((color, index) => new RasterSymbologyColorStop(
            Guid.NewGuid(), EncodeColor(color), surfaceFill.ColorValues[index])).ToArray();
        var draft = new RasterSymbologyDraftContract(paletteID, surfaceFill.Name ?? "", stops,
            NormalizeValueSource(valueSource), paletteID == "Grayscale", false, false,
            context.Raster.ValidExtent.IsFiniteAndValid(), symbology.Alpha, symbology.IsSurfaceFillEnabled,
            symbology.SmoothingRadius, symbology.ResampleCellSizeRatio,
            new(symbology.HillshadeProperties.IsEnabled, symbology.HillshadeProperties.ZFactor,
                symbology.HillshadeProperties.Azimuth, symbology.HillshadeProperties.Zenith),
            new(symbology.ContourProperties.IsEnabled, symbology.ContourProperties.AutoInterval,
                symbology.ContourProperties.ContourInterval, symbology.ContourProperties.IndexEvery,
                EncodeColor(symbology.ContourProperties.Color), symbology.ContourProperties.Opacity,
                symbology.ContourProperties.ShowLabels, symbology.ContourProperties.Density));
        return new(draft, palettes, surfaceFill.ColorValues,
            DescribeRasterPlotOptions(context.Raster, symbology, plotOptions));
    }

    private static RasterSymbology ReadSymbology(XElement? node, IResample<float> raster,
        XDocument sourceRoot, out string valueSource)
    {
        var layer = new RasterRenderLayer(raster);
        RasterSymbology result = layer.GetDefaultTerrainHistoSymbology();
        valueSource = "Histogram";
        if (node is null) return result;

        valueSource = node.Attribute("ValueSource")?.Value ?? "Manual";
        var surface = node.Element("SurfaceFill");
        if (surface is not null)
        {
            string? name = surface.Attribute("Name")?.Value;
            var colorNodes = surface.Elements("Color").ToArray();
            if (colorNodes.Length > 0)
            {
                var colors = new List<Color>();
                var values = new List<double>();
                foreach (var colorNode in colorNodes)
                {
                    colors.Add(ParseXmlColor(colorNode));
                    values.Add(ParseDouble(colorNode.Attribute("Value")?.Value) ?? 0d);
                }
                if (values.All(double.IsFinite) && colors.Count > 0)
                    result.SurfaceFill = new InterpolatedSurfaceFill(colors, values, name: name ?? "");
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                var palette = FindPalette(sourceRoot, name);
                if (palette is not null && palette.Colors.Length > 0)
                {
                    double[] values;
                    if (palette.Values is { Length: > 1 } saved && saved.Length == palette.Colors.Length)
                        values = saved;
                    else
                        values = InterpolatedSurfaceFill.EstimateColorValuesFrom(raster, palette.Colors.Length).ToArray();
                    result.SurfaceFill = new InterpolatedSurfaceFill(palette.Colors.Select(ParseColor).ToArray(), values, name: name);
                }
            }
        }
        if (bool.TryParse(node.Attribute("IsSurfaceFillEnabled")?.Value, out bool surfaceEnabled))
            result.IsSurfaceFillEnabled = surfaceEnabled;
        if (byte.TryParse(node.Attribute("Alpha")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte alpha))
            result.Alpha = alpha;
        if (Enum.TryParse(node.Attribute("InterpolationMode")?.Value, true, out InterpolationMode interpolation))
            result.InterpolationMode = interpolation;
        if (float.TryParse(node.Attribute("SmoothingRadius")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float smoothing) && float.IsFinite(smoothing) && smoothing >= 0)
            result.SmoothingRadius = smoothing;
        if (float.TryParse(node.Attribute("ResampleCellSizeRatio")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio) && float.IsFinite(ratio) && ratio >= 0)
            result.ResampleCellSizeRatio = ratio;
        var hillshade = node.Element("Hillshade");
        if (hillshade is not null)
        {
            if (bool.TryParse(hillshade.Attribute("IsEnabled")?.Value, out bool enabled)) result.HillshadeProperties.IsEnabled = enabled;
            if (float.TryParse(hillshade.Attribute("ZFactor")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z) && float.IsFinite(z) && z >= 0) result.HillshadeProperties.ZFactor = z;
            if (float.TryParse(hillshade.Attribute("Azimuth")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float azimuth) && float.IsFinite(azimuth)) result.HillshadeProperties.Azimuth = azimuth;
            if (float.TryParse(hillshade.Attribute("Zenith")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float zenith) && float.IsFinite(zenith)) result.HillshadeProperties.Zenith = zenith;
        }
        var contours = node.Element("Contours");
        if (contours is not null)
        {
            if (bool.TryParse(contours.Attribute("IsEnabled")?.Value, out bool enabled)) result.ContourProperties.IsEnabled = enabled;
            if (bool.TryParse(contours.Attribute("Auto")?.Value, out bool autoInterval)) result.ContourProperties.AutoInterval = autoInterval;
            if (float.TryParse(contours.Attribute("Interval")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float interval) && float.IsFinite(interval) && interval > 0) result.ContourProperties.ContourInterval = interval;
            if (int.TryParse(contours.Attribute("IndexEvery")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int indexEvery)) result.ContourProperties.IndexEvery = indexEvery;
            if (int.TryParse(contours.Attribute("Color")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int color)) result.ContourProperties.Color = Color.FromArgb(color);
            if (int.TryParse(contours.Attribute("Opacity")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) result.ContourProperties.Opacity = opacity;
            if (bool.TryParse(contours.Attribute("ShowLabels")?.Value, out bool showLabels)) result.ContourProperties.ShowLabels = showLabels;
            if (float.TryParse(contours.Attribute("Density")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float density) && float.IsFinite(density)) result.ContourProperties.Density = density;
        }
        return result;
    }

    private static RasterSymbology ApplyDraftToCurrent(XElement? node, RasterSymbologyDraftContract draft,
        IResample<float> raster, XDocument sourceRoot)
    {
        var result = ReadSymbology(node, raster, sourceRoot, out _);
        var colors = draft.Stops.Select(stop => ParseColor(stop.Color)).ToArray();
        var values = draft.Stops.Select(stop => stop.Value).ToArray();
        string name = draft.Name;
        if (draft.Grayscale)
        {
            colors = colors.Select((_, index) =>
            {
                int component = colors.Length <= 1 ? 0 : (int)Math.Round(255d * index / (colors.Length - 1));
                return Color.FromArgb(255, component, component, component);
            }).ToArray();
            name = "Grayscale";
        }
        result.SurfaceFill = new InterpolatedSurfaceFill(colors, values, name: name);
        if (draft.Alpha is byte alpha) result.Alpha = alpha;
        if (draft.IsSurfaceFillEnabled is bool isSurfaceFillEnabled) result.IsSurfaceFillEnabled = isSurfaceFillEnabled;
        if (draft.SmoothingRadius is float smoothingRadius) result.SmoothingRadius = smoothingRadius;
        if (draft.ResampleCellSizeRatio is float resampleCellSizeRatio) result.ResampleCellSizeRatio = resampleCellSizeRatio;
        if (draft.Hillshade is { } hillshade)
        {
            result.HillshadeProperties.IsEnabled = hillshade.IsEnabled;
            result.HillshadeProperties.ZFactor = hillshade.ZFactor;
            result.HillshadeProperties.Azimuth = hillshade.Azimuth;
            result.HillshadeProperties.Zenith = hillshade.Zenith;
        }
        if (draft.Contours is { } contours)
        {
            result.ContourProperties.IsEnabled = contours.IsEnabled;
            result.ContourProperties.AutoInterval = contours.AutoInterval;
            result.ContourProperties.ContourInterval = contours.Interval;
            result.ContourProperties.IndexEvery = contours.IndexEvery;
            result.ContourProperties.Color = ParseColor(contours.Color);
            result.ContourProperties.Opacity = contours.Opacity;
            result.ContourProperties.ShowLabels = contours.ShowLabels;
            result.ContourProperties.Density = contours.Density;
        }
        return result;
    }

    private static void WriteRasterSymbology(XElement mapLayer, RasterSymbologyDraftContract draft,
        RasterSymbology symbology)
    {
        var node = mapLayer.Element("RasterSymbology");
        if (node is null) { node = new XElement("RasterSymbology"); mapLayer.Add(node); }
        string valueSource = NormalizeValueSource(draft.ValueSource);
        SetDefaulted(node, "IsSurfaceFillEnabled", symbology.IsSurfaceFillEnabled ? "true" : "false", "true");
        SetDefaulted(node, "Alpha", symbology.Alpha.ToString(CultureInfo.InvariantCulture), byte.MaxValue.ToString(CultureInfo.InvariantCulture));
        SetDefaulted(node, "InterpolationMode", symbology.InterpolationMode.ToString(), InterpolationMode.Linear.ToString());
        SetDefaulted(node, "SmoothingRadius", FormatInvariant(symbology.SmoothingRadius), "15");
        SetDefaulted(node, "ResampleCellSizeRatio", FormatInvariant(symbology.ResampleCellSizeRatio), "1");
        SetDefaulted(node, "ValueSource", valueSource, "Manual");
        var fill = node.Element("SurfaceFill");
        if (fill is null) { fill = new XElement("SurfaceFill"); node.AddFirst(fill); }
        fill.SetAttributeValue("Name", draft.Grayscale ? "Grayscale" : draft.Name);
        fill.Elements("Color").Remove();
        foreach (var stop in draft.Stops)
        {
            var color = ParseColor(stop.Color);
            fill.Add(new XElement("Color", new XAttribute("R", color.R), new XAttribute("G", color.G),
                new XAttribute("B", color.B), new XAttribute("Value", stop.Value.ToString("R", CultureInfo.InvariantCulture))));
        }

        XElement? hillshade = node.Element("Hillshade");
        PatchSymbologyChild(node, ref hillshade, "Hillshade", new Dictionary<string, string>
        {
            ["IsEnabled"] = symbology.HillshadeProperties.IsEnabled ? "true" : "false",
            ["ZFactor"] = FormatInvariant(symbology.HillshadeProperties.ZFactor),
            ["Azimuth"] = FormatInvariant(symbology.HillshadeProperties.Azimuth),
            ["Zenith"] = FormatInvariant(symbology.HillshadeProperties.Zenith)
        }, new Dictionary<string, string>
        {
            ["IsEnabled"] = "false", ["ZFactor"] = "3", ["Azimuth"] = "315", ["Zenith"] = "45"
        });

        XElement? contours = node.Element("Contours");
        PatchSymbologyChild(node, ref contours, "Contours", new Dictionary<string, string>
        {
            ["IsEnabled"] = symbology.ContourProperties.IsEnabled ? "true" : "false",
            ["Auto"] = symbology.ContourProperties.AutoInterval ? "true" : "false",
            ["Interval"] = FormatInvariant(symbology.ContourProperties.ContourInterval),
            ["IndexEvery"] = symbology.ContourProperties.IndexEvery.ToString(CultureInfo.InvariantCulture),
            ["Color"] = symbology.ContourProperties.Color.ToArgb().ToString(CultureInfo.InvariantCulture),
            ["Opacity"] = symbology.ContourProperties.Opacity.ToString(CultureInfo.InvariantCulture),
            ["ShowLabels"] = symbology.ContourProperties.ShowLabels ? "true" : "false",
            ["Density"] = FormatInvariant(symbology.ContourProperties.Density)
        }, new Dictionary<string, string>
        {
            ["IsEnabled"] = "false", ["Auto"] = "true", ["Interval"] = "10", ["IndexEvery"] = "5",
            ["Color"] = "-16777216", ["Opacity"] = "70", ["ShowLabels"] = "true", ["Density"] = "1"
        });
    }

    private static Dictionary<string, string> ResolveSavedRasterPlotOptions(OperationContext context,
        RasterSymbology symbology)
    {
        Dictionary<string, string> saved = ReadRasterPlotOptions(context.SourceNode);
        var probe = new RasterRenderLayer(context.Raster, symbology);
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions = probe.GetPlotOptions()
            .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
        var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in saved)
        {
            if (!sourceOptions.TryGetValue(name, out var sourceOption) ||
                !TryParseRasterPlotOption(sourceOption.Property.PropertyType, value, out _)) continue;
            candidates.Add(name, value);
        }

        // Settings saved by a newer renderer or a previous condition must not
        // make an otherwise readable .vis fail to load. Apply only the valid,
        // currently enabled subset; the original XML remains untouched.
        while (true)
        {
            var renderer = new RasterRenderLayer(context.Raster, symbology);
            Dictionary<string, (PlotOption Option, PropertyInfo Property)> currentOptions = renderer.GetPlotOptions()
                .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
            ApplyRasterPlotOptionValues(renderer, currentOptions, candidates);
            string[] disabled = candidates.Keys.Where(name =>
                !IsRasterPlotOptionEnabled(renderer, currentOptions[name].Option)).ToArray();
            if (disabled.Length == 0) return candidates;
            foreach (string name in disabled) candidates.Remove(name);
        }
    }

    private static Dictionary<string, string> ReadRasterPlotOptions(XElement mapLayer)
    {
        XElement? saved = mapLayer.Elements("RasterPlotOptions").FirstOrDefault(element =>
            element.Attribute("Version")?.Value is null or "1");
        if (saved is null) return new(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement option in saved.Elements("Option"))
        {
            string? name = option.Attribute("PropertyName")?.Value;
            string? value = option.Attribute("Value")?.Value;
            if (string.IsNullOrWhiteSpace(name) || value is null || result.ContainsKey(name)) continue;
            result.Add(name, value);
        }
        return result;
    }

    private static void WriteRasterPlotOptions(XElement mapLayer, IResample<float> raster,
        RasterSymbology symbology, Dictionary<string, string> values)
    {
        var baseline = new RasterRenderLayer(raster, symbology);
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions = baseline.GetPlotOptions()
            .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
        var normalizedValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in values)
        {
            if (!sourceOptions.TryGetValue(name, out var sourceOption))
            {
                var aliases = sourceOptions.Values.Where(candidate =>
                    string.Equals(candidate.Property.Name, name, StringComparison.Ordinal) ||
                    string.Equals(candidate.Option.Name, name, StringComparison.Ordinal)).Take(2).ToArray();
                if (aliases.Length == 0)
                    throw new ArgumentException($"The RasterRenderLayer has no PlotOption named '{name}'.");
                if (aliases.Length > 1)
                    throw new ArgumentException($"PlotOption name '{name}' is ambiguous for this renderer.");
                sourceOption = aliases[0];
            }
            if (!normalizedValues.TryAdd(sourceOption.Property.Name, value))
                throw new ArgumentException($"PlotOption '{name}' duplicates another renderer property.");
        }
        var effective = new RasterRenderLayer(raster, symbology);
        ApplyRasterPlotOptions(effective, sourceOptions, normalizedValues);

        var persisted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in normalizedValues)
        {
            var sourceOption = sourceOptions[name];
            // ResampleMode is the renderer's public alias for the legacy
            // RasterSymbology.InterpolationMode .vis attribute.
            if (sourceOption.Property.Name == nameof(RasterRenderLayer.ResampleMode)) continue;
            object? current = sourceOption.Property.GetValue(effective);
            object? defaultValue = sourceOption.Property.GetValue(baseline);
            if (!string.Equals(FormatPlotOptionValue(current), FormatPlotOptionValue(defaultValue),
                    StringComparison.Ordinal))
                persisted.Add(name, value);
        }

        XElement? container = mapLayer.Elements("RasterPlotOptions").FirstOrDefault(element =>
            element.Attribute("Version")?.Value is null or "1");
        if (container is null && persisted.Count > 0)
        {
            container = new XElement("RasterPlotOptions", new XAttribute("Version", "1"));
            mapLayer.Add(container);
        }
        if (container is null) return;
        container.SetAttributeValue("Version", "1");

        foreach (string knownName in sourceOptions.Keys)
        {
            XElement[] existing = container.Elements("Option")
                .Where(option => string.Equals(option.Attribute("PropertyName")?.Value, knownName,
                    StringComparison.Ordinal)).ToArray();
            if (persisted.TryGetValue(knownName, out string? storedValue))
            {
                XElement target = existing.FirstOrDefault() ?? new XElement("Option");
                target.SetAttributeValue("PropertyName", knownName);
                target.SetAttributeValue("Value", storedValue);
                if (target.Parent is null) container.Add(target);
                foreach (XElement duplicate in existing.Skip(1)) duplicate.Remove();
            }
            else
            {
                foreach (XElement option in existing) option.Remove();
            }
        }

        bool hasUnknownContent = container.Attributes().Any(attribute => attribute.Name != "Version") ||
            container.Nodes().Any(node => node is not XElement element || element.Name != "Option" ||
                !sourceOptions.ContainsKey(element.Attribute("PropertyName")?.Value ?? ""));
        bool hasKnownOptions = container.Elements("Option").Any(option =>
            sourceOptions.ContainsKey(option.Attribute("PropertyName")?.Value ?? ""));
        if (!hasUnknownContent && !hasKnownOptions) container.Remove();
    }

    private static void PatchSymbologyChild(XElement parent, ref XElement? child, string elementName,
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> defaults)
    {
        bool differsFromDefaults = values.Any(pair => !string.Equals(pair.Value, defaults[pair.Key],
            StringComparison.Ordinal));
        if (child is null && !differsFromDefaults) return;
        if (child is null)
        {
            child = new XElement(elementName);
            parent.Add(child);
        }
        foreach ((string name, string value) in values)
            SetDefaulted(child, name, value, defaults[name]);
        if (!child.HasAttributes && !child.HasElements && !child.Nodes().Any()) child.Remove();
    }

    private static void SetDefaulted(XElement element, string name, string value, string defaultValue)
    {
        if (string.Equals(value, defaultValue, StringComparison.Ordinal)) element.Attribute(name)?.Remove();
        else element.SetAttributeValue(name, value);
    }

    private static string FormatInvariant(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatPlotOptionValue(object? value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        Enum enumeration => enumeration.ToString(),
        _ => string.Empty
    };

    private static RasterMapLayerPayload Render(OperationContext context, RasterSymbology symbology,
        RasterMapViewExtent? viewport, Dictionary<string, string>? plotOptions = null)
    {
        Extent bounds = context.Raster.ValidExtent;
        if (!bounds.IsFiniteAndValid()) throw new NotSupportedException("The raster has no finite map extent.");
        if (viewport is not null)
        {
            ValidateExtent(viewport);
            bounds = bounds.Intersection(new Extent(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY));
            if (!bounds.IsFiniteAndValid()) throw new ArgumentException("The visible map extent does not overlap the raster.");
        }
        int width, height;
        if (viewport is not null)
        {
            width = Math.Clamp(viewport.PixelWidth, 1, MaximumRasterSide);
            height = Math.Clamp(viewport.PixelHeight, 1, MaximumRasterSide);
        }
        else
        {
            var baseDef = context.SourceRasterDefinition;
            double factor = Math.Min(1d, MaximumRasterSide / (double)Math.Max(baseDef.Rows, baseDef.Cols));
            width = Math.Clamp((int)Math.Round(baseDef.Cols * factor), 1, MaximumRasterSide);
            height = Math.Clamp((int)Math.Round(baseDef.Rows * factor), 1, MaximumRasterSide);
        }
        var definition = new RasterDefinition(bounds, height, width);
        var renderer = new RasterRenderLayer(context.Raster, symbology);
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options = renderer.GetPlotOptions()
            .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
        ApplyRasterPlotOptions(renderer, options, plotOptions ?? []);
        using PlotRequest plot = renderer.Render(definition);
        Span<int> colors = plot.ColorBufferARGB.Span;
        if (colors.Length != definition.CellCount)
            throw new InvalidDataException("The retained raster renderer returned an incomplete pixel buffer.");
        byte[] rgba = new byte[checked(colors.Length * 4)];
        for (int index = 0; index < colors.Length; index++)
        {
            uint argb = unchecked((uint)colors[index]);
            rgba[index * 4] = (byte)(argb >> 16);
            rgba[index * 4 + 1] = (byte)(argb >> 8);
            rgba[index * 4 + 2] = (byte)argb;
            rgba[index * 4 + 3] = (byte)(argb >> 24);
        }
        return new(context.Source.Id, context.Source.SourceMapID, context.Source.NodeName,
            width, height, rgba, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
    }

    private static RasterPlotOptionDescriptor[] DescribeRasterPlotOptions(IResample<float> raster,
        RasterSymbology symbology, Dictionary<string, string>? values = null)
    {
        var renderer = new RasterRenderLayer(raster, symbology);
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options = renderer.GetPlotOptions()
            .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
        ApplyRasterPlotOptions(renderer, options, values ?? []);
        return options.Select(pair =>
        {
            PlotOption sourceOption = pair.Value.Option;
            PropertyInfo property = pair.Value.Property;
            bool enabled = IsRasterPlotOptionEnabled(renderer, sourceOption);
            object? current = property.GetValue(renderer);
            string value = current switch
            {
                bool boolean => boolean ? "true" : "false",
                Enum enumeration => enumeration.ToString(),
                _ => throw new InvalidDataException("The retained raster renderer exposed an unsupported PlotOption type.")
            };
            if (property.PropertyType == typeof(bool))
                return new RasterPlotOptionDescriptor(property.Name, sourceOption.Name, sourceOption.Label,
                    sourceOption.Description, "bool", value, [], IsEnabled: enabled, EnabledIf: sourceOption.EnabledIf);
            RasterPlotOptionChoice[] choices = Enum.GetNames(property.PropertyType).Select(name =>
            {
                MemberInfo member = property.PropertyType.GetMember(name).First();
                return new RasterPlotOptionChoice(name,
                    member.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? name);
            }).ToArray();
            return new RasterPlotOptionDescriptor(property.Name, sourceOption.Name, sourceOption.Label,
                sourceOption.Description, "enum", value, choices, IsEnabled: enabled, EnabledIf: sourceOption.EnabledIf);
        }).OrderBy(option => option.PropertyName, StringComparer.Ordinal).ToArray();
    }

    private static void ApplyRasterPlotOptions(RasterRenderLayer renderer,
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions,
        Dictionary<string, string> values)
    {
        var resolvedOptions = new List<(string Name, (PlotOption Option, PropertyInfo Property) SourceOption, string Value)>();
        foreach ((string name, string value) in values)
        {
            if (!sourceOptions.TryGetValue(name, out var sourceOption))
            {
                var aliases = sourceOptions.Values.Where(candidate =>
                    string.Equals(candidate.Property.Name, name, StringComparison.Ordinal) ||
                    string.Equals(candidate.Option.Name, name, StringComparison.Ordinal)).Take(2).ToArray();
                if (aliases.Length == 0)
                    throw new ArgumentException($"The RasterRenderLayer has no PlotOption named '{name}'.");
                if (aliases.Length > 1)
                    throw new ArgumentException($"PlotOption name '{name}' is ambiguous for this renderer.");
                sourceOption = aliases[0];
            }
            if (!TryParseRasterPlotOption(sourceOption.Property.PropertyType, value, out object? parsed) || parsed is null)
                throw new ArgumentException($"PlotOption '{name}' has an unsupported value.");
            sourceOption.Property.SetValue(renderer, parsed);
            if (resolvedOptions.Any(existing => string.Equals(existing.SourceOption.Property.Name,
                    sourceOption.Property.Name, StringComparison.Ordinal)))
                throw new ArgumentException($"PlotOption '{name}' duplicates another renderer property.");
            resolvedOptions.Add((name, sourceOption, value));
        }
        foreach (var resolved in resolvedOptions)
        {
            PlotOption option = resolved.SourceOption.Option;
            if (string.IsNullOrWhiteSpace(option.EnabledIf)) continue;
            PropertyInfo? gate = renderer.GetType().GetProperty(option.EnabledIf,
                BindingFlags.Instance | BindingFlags.Public);
            if (gate?.PropertyType != typeof(bool) || gate.GetValue(renderer) is not true)
                throw new ArgumentException($"PlotOption '{resolved.Name}' is unavailable for the current renderer state.");
        }
    }

    private static bool TryParseRasterPlotOption(Type type, string value, out object? parsed)
    {
        parsed = null;
        if (type == typeof(bool) && bool.TryParse(value, out bool boolean))
        {
            parsed = boolean;
            return true;
        }
        if (!type.IsEnum || !Enum.TryParse(type, value, ignoreCase: false, out object? enumeration) ||
            enumeration is null || !Enum.IsDefined(type, enumeration)) return false;
        parsed = enumeration;
        return true;
    }

    private static void ApplyRasterPlotOptionValues(RasterRenderLayer renderer,
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> sourceOptions,
        Dictionary<string, string> values)
    {
        foreach ((string name, string value) in values)
        {
            if (sourceOptions.TryGetValue(name, out var sourceOption) &&
                TryParseRasterPlotOption(sourceOption.Property.PropertyType, value, out object? parsed) && parsed is not null)
                sourceOption.Property.SetValue(renderer, parsed);
        }
    }

    private static void ApplyRasterPlotOptionsToSymbology(IResample<float> raster,
        RasterSymbology symbology, Dictionary<string, string> values)
    {
        var renderer = new RasterRenderLayer(raster, symbology);
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options = renderer.GetPlotOptions()
            .ToDictionary(pair => pair.Value.Name, pair => (pair.Key, pair.Value), StringComparer.Ordinal);
        ApplyRasterPlotOptions(renderer, options, values);
    }

    private static bool IsRasterPlotOptionEnabled(RasterRenderLayer renderer, PlotOption option)
    {
        if (string.IsNullOrWhiteSpace(option.EnabledIf)) return true;
        PropertyInfo? gate = renderer.GetType().GetProperty(option.EnabledIf,
            BindingFlags.Instance | BindingFlags.Public);
        if (gate?.PropertyType != typeof(bool))
            throw new InvalidOperationException($"Raster PlotOption condition '{option.EnabledIf}' is not a Boolean source property.");
        return gate.GetValue(renderer) is true;
    }

    private static OperationContext Open(RasterMapSourceIdentity source, string? projectXml)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.ProjectPath) || string.IsNullOrWhiteSpace(source.VisualizationPath) ||
            string.IsNullOrWhiteSpace(source.SourcePath) || source.MapLayerType != "SingleBandRasterMapLayer" ||
            source.NodePath.Length == 0 || source.NodePath.Any(index => index < 0))
            throw new ArgumentException("The raster map source identity is incomplete.");
        string projectPath = CanonicalPath(source.ProjectPath);
        string visPath = CanonicalPath(source.VisualizationPath);
        string expectedVisPath = Path.ChangeExtension(projectPath, ".vis");
        if (!PathEquals(visPath, expectedVisPath)) throw new ArgumentException("The visualization file does not belong to this project.");
        if (!File.Exists(visPath)) throw new FileNotFoundException("The current project's visualization file was not found.", visPath);
        var visFile = new FileInfo(visPath);
        if (visFile.Length > MaximumVisualizationBytes) throw new NotSupportedException("The visualization file is too large to edit.");
        byte[] originalBytes = File.ReadAllBytes(visPath);
        string revision = Revision(originalBytes);
        if (!revision.Equals(source.VisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        var visualization = ParseXml(originalBytes, MaximumVisualizationBytes);
        var root = visualization.Root;
        if (root?.Name != "Ras") throw new InvalidDataException("The visualization file has an invalid Ras root element.");
        var maps = root.Element("Maps")?.Elements("Map").ToArray() ?? [];
        if (source.MapIndex < 0 || source.MapIndex >= maps.Length) throw new SourceFileChangedException();
        var map = maps[source.MapIndex];
        if (map.Attribute("Name")?.Value != source.MapName || map.Attribute("Type")?.Value != source.MapType)
            throw new SourceFileChangedException();
        var nodes = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        XElement? sourceNode = null;
        foreach (int index in source.NodePath)
        {
            if (index >= nodes.Length) throw new SourceFileChangedException();
            sourceNode = nodes[index];
            nodes = sourceNode.Elements("Node").ToArray();
        }
        if (sourceNode is null || sourceNode.Attribute("Type")?.Value != "MapLayer" ||
            sourceNode.Attribute("MapType")?.Value != "SingleBandRasterMapLayer" ||
            sourceNode.Attribute("Name")?.Value != source.NodeName)
            throw new SourceFileChangedException();
        if (source.Id != SourceID(source.MapIndex, source.NodePath)) throw new ArgumentException("The raster map node identity is invalid.");
        string? actualFilename = sourceNode.Attribute("Filename")?.Value;
        string? actualRelative = sourceNode.Attribute("FilenameRelative")?.Value;
        if (!string.Equals(actualFilename, source.Filename, StringComparison.Ordinal) ||
            !string.Equals(actualRelative, source.FilenameRelative, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string resolved = ResolveRasterPath(projectDirectory, actualRelative, actualFilename);
        if (!PathEquals(CanonicalPath(source.SourcePath), CanonicalPath(resolved)))
            throw new ArgumentException("The raster source path does not match the current visualization node.");
        if (!File.Exists(resolved)) throw new FileNotFoundException("The source raster was not found.", resolved);
        string sourceRevision = MapRasterLayerSourceManifest.CaptureRevision(resolved);
        if (source.SourceRevision is not null && !source.SourceRevision.Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        var projectDocument = ReadProjectDocument(projectPath, projectXml);
        Projection? targetProjection = null;
        var projectionElement = projectDocument.Root?.Element("Projection");
        if (projectionElement is not null)
        {
            var projectionDefinition = Ras.Layers.ProjectionDefinition.Deserialize(projectionElement);
            var result = projectionDefinition.Evaluate(out targetProjection, out _);
            if (!result.Result) throw new ArgumentException("The current project projection could not be evaluated.");
        }
        using var gdal = new GDALRaster(resolved);
        Type dataType = gdal.GetDataType();
        if (gdal.GetBandCount() != 1) throw new NotSupportedException("Only single-band rasters are supported.");
        if (!dataType.IsEquivalentTo(typeof(float)) && !dataType.IsEquivalentTo(typeof(double)))
            throw new NotSupportedException("Only single-band float and double rasters are supported.");
        Projection? sourceProjection = gdal.GetProjection();
        var sourceDefinition = gdal.GetPyramidDefinition().BasePyramidLevel;
        IDisposable? cleanup = null;
        IResample<float>? raster = null;
        try
        {
            if (dataType.IsEquivalentTo(typeof(float)))
                raster = RasterReader.TryGetBestRasterizer<float>(resolved, out _, out cleanup)
                    ?? throw new InvalidDataException("The source raster could not be opened.");
            else
            {
                var doubleRaster = RasterReader.TryGetBestRasterizer<double>(resolved, out _, out cleanup)
                    ?? throw new InvalidDataException("The source raster could not be opened.");
                raster = doubleRaster.ConvertType(ConvertTypeResamplers.DoubleToFloatConverter,
                    (float?)doubleRaster.NoDataValue);
            }
            if (targetProjection is not null && sourceProjection is not null && !targetProjection.IsEqual(sourceProjection))
                raster = raster.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));
            return new OperationContext(source, visualization, revision, visPath, CanonicalPath(resolved), sourceRevision,
                sourceDefinition, raster, cleanup);
        }
        catch
        {
            cleanup?.Dispose();
            throw;
        }
    }

    private static T WithContext<T>(RasterMapSourceIdentity source, string? projectXml,
        Func<OperationContext, T> operation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operation);
        if (source.GdbSource is null)
        {
            using OperationContext context = Open(source, projectXml);
            return operation(context);
        }

        GdbRasterSymbologyIdentity identity = source.GdbSource;
        if (source.MapLayerType != MapRasterLayerRendering.SingleBandMapType)
            throw new NotSupportedException("Continuous raster symbology requires a single float or double band.");
        MapTreeRequest map = GdbRasterSourceService.ValidateRasterIdentity(source, projectXml);
        CatalogTreeNodeLocator node = identity.Node;

        var request = new GdbRasterSourceLoadRequest(map, node, identity.Source);
        (T Result, OperationContext Context) staged = GdbRasterSourceService.WithOpenedRasterSource(
            request, opened =>
            {
                OperationContext context = OpenGdb(source, map, identity, opened);
                try
                {
                    T result = operation(context);
                    return (result, context);
                }
                catch
                {
                    context.Dispose();
                    throw;
                }
            });

        try
        {
            if (staged.Context.PendingVisualizationBytes is not null)
                CommitStagedGdbVisualization(staged.Context);
            return staged.Result;
        }
        finally
        {
            staged.Context.Dispose();
        }
    }

    private static bool MapIDsMatch(string? sourceMapID, string? mapID)
    {
        if (Guid.TryParse(sourceMapID, out Guid sourceGuid) && Guid.TryParse(mapID, out Guid mapGuid))
            return sourceGuid == mapGuid;
        return string.Equals(sourceMapID, mapID, StringComparison.Ordinal);
    }

    private static OperationContext OpenGdb(RasterMapSourceIdentity source, MapTreeRequest map,
        GdbRasterSymbologyIdentity identity, GdbRasterOpenedSource opened)
    {
        if (!SameGdbSource(identity.Source, opened.Capture.Source) ||
            opened.Capture.Map.MapIndex != source.MapIndex ||
            !MapIDsMatch(source.SourceMapID, opened.Capture.Map.MapID) ||
            opened.Capture.Map.MapName != source.MapName || opened.Capture.Map.MapType != source.MapType ||
            !PathEquals(opened.Capture.Map.ProjectPath, source.ProjectPath) ||
            !PathEquals(opened.Capture.Map.VisualizationPath, source.VisualizationPath) ||
            !string.Equals(opened.Capture.Map.VisualizationRevision, source.VisualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        XDocument visualization = opened.VisualizationSnapshot;
        var projectDocument = ReadProjectDocument(source.ProjectPath, map.ProjectXml);
        Projection? targetProjection = null;
        XElement? projectionElement = projectDocument.Root?.Element("Projection");
        if (projectionElement is not null)
        {
            var definition = Ras.Layers.ProjectionDefinition.Deserialize(projectionElement);
            var result = definition.Evaluate(out targetProjection, out _);
            if (!result.Result) throw new ArgumentException("The current project projection could not be evaluated.");
        }

        IResample<float> raster = opened.Raster;
        Projection? sourceProjection = opened.Dataset.GetProjection();
        if (targetProjection is not null && sourceProjection is not null && !targetProjection.IsEqual(sourceProjection))
            raster = raster.WithTransformer(new ReprojectionTransformer(sourceProjection, targetProjection));

        return new OperationContext(source, visualization,
            opened.Capture.Map.VisualizationRevision ?? throw new SourceFileChangedException(),
            CanonicalPath(source.VisualizationPath), opened.Capture.Source.SourcePath,
            opened.Capture.Source.Revision, opened.BaseDefinition, raster, null,
            identity, map, opened.Capture);
    }

    private static bool SameGdbSource(GdbMapSourceIdentity first, GdbMapSourceIdentity second)
    {
        if (!PathEquals(first.SourcePath, second.SourcePath) ||
            !string.Equals(first.Revision, second.Revision, StringComparison.OrdinalIgnoreCase) ||
            first.Files.Length != second.Files.Length)
            return false;
        for (int index = 0; index < first.Files.Length; index++)
        {
            GdbMapSourceFileRevision left = first.Files[index];
            GdbMapSourceFileRevision right = second.Files[index];
            if (!string.Equals(left.RelativePath, right.RelativePath, StringComparison.Ordinal) ||
                left.Length != right.Length ||
                !string.Equals(left.Revision, right.Revision, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static XDocument ReadProjectDocument(string projectPath, string? projectXml)
    {
        byte[] bytes;
        if (projectXml is not null)
        {
            if (Encoding.UTF8.GetByteCount(projectXml) > 4 * 1024 * 1024)
                throw new ArgumentException("The current project XML is too large.");
            bytes = Encoding.UTF8.GetBytes(projectXml);
        }
        else
        {
            if (!File.Exists(projectPath)) throw new FileNotFoundException("The project file was not found.", projectPath);
            if (new FileInfo(projectPath).Length > 4 * 1024 * 1024) throw new NotSupportedException("The project file is too large to inspect.");
            bytes = File.ReadAllBytes(projectPath);
        }
        var document = ParseXml(bytes, 4 * 1024 * 1024);
        if (document.Root?.Name != "Project") throw new InvalidDataException("The current project XML has an invalid Project root element.");
        return document;
    }

    private static XDocument ParseXml(byte[] bytes, long maximumBytes)
    {
        if (bytes.LongLength > maximumBytes) throw new NotSupportedException("The XML document is too large.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = maximumBytes, IgnoreWhitespace = false };
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string ResolveRasterPath(string projectDirectory, string? filenameRelative, string? filename)
    {
        if (!string.IsNullOrWhiteSpace(filenameRelative))
        {
            string candidate = ResolvePath(projectDirectory, filenameRelative);
            if (File.Exists(candidate)) return candidate;
        }
        if (!string.IsNullOrWhiteSpace(filename))
        {
            string candidate = ResolvePath(projectDirectory, filename);
            if (File.Exists(candidate)) return candidate;
        }
        string missing = string.IsNullOrWhiteSpace(filenameRelative) ? filename ?? "" : ResolvePath(projectDirectory, filenameRelative);
        throw new FileNotFoundException("The raster referenced by the visualization file was not found.", missing);
    }

    private static string ResolvePath(string projectDirectory, string value)
    {
        string normalized = value.Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(projectDirectory, normalized));
    }

    private static string Commit(OperationContext context)
    {
        if (context.GdbSource is not null)
        {
            using var memory = new MemoryStream();
            context.Visualization.Save(memory, SaveOptions.DisableFormatting);
            byte[] bytes = memory.ToArray();
            if (bytes.LongLength > MaximumVisualizationBytes)
                throw new NotSupportedException("The visualization file is too large to edit.");
            context.PendingVisualizationBytes = bytes;
            return Revision(bytes);
        }

        string tempPath = context.VisualizationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool replacementCompleted = false;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                context.Visualization.Save(stream, SaveOptions.DisableFormatting);
                stream.Flush(flushToDisk: true);
            }
            VerifySourceRevision(context);
            string receiptRevision = ReplaceStagedVisualizationFile(tempPath,
                context.VisualizationPath, context.PreviousVisualizationRevision);
            replacementCompleted = true;
            return receiptRevision;
        }
        finally
        {
            if (!replacementCompleted && File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void CommitStagedGdbVisualization(OperationContext context)
    {
        byte[] bytes = context.PendingVisualizationBytes ??
            throw new InvalidOperationException("The GDB visualization change was not staged.");
        MapTreeRequest map = context.GdbMap ??
            throw new InvalidOperationException("The GDB map identity is missing.");
        GdbRasterNodeCapture capture = context.GdbCapture ??
            throw new InvalidOperationException("The GDB raster capture is missing.");
        string expectedRevision = Revision(bytes);
        string tempPath = context.VisualizationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool replacementCompleted = false;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            VerifySourceRevision(context);
            if (!RevisionFile(context.VisualizationPath).Equals(
                    context.PreviousVisualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            GdbRasterSourceService.VerifyCapture(map, capture);
            GdbMapSourceService.VerifySource(capture.Source);

            // The mutation result already carries SHA256(staged bytes). Validate
            // the complete read/source/map capture before replacement, then make
            // the rename the final fallible operation. Reading or validating
            // after replacement could turn a committed change into a failed RPC
            // with no receipt for the caller to accept.
            _ = ReplaceStagedVisualizationFile(tempPath, context.VisualizationPath,
                context.PreviousVisualizationRevision, expectedRevision);
            replacementCompleted = true;
        }
        finally
        {
            if (!replacementCompleted && File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Verifies the staged payload and current file before the rename. Once the
    /// move succeeds, the staged-byte SHA is the receipt; no post-commit read or
    /// validation is allowed to turn success into an unreported failure.
    /// </summary>
    internal static string ReplaceStagedVisualizationFile(string tempPath, string visualizationPath,
        string expectedPreviousRevision, string? expectedStagedRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(visualizationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPreviousRevision);

        string stagedRevision = RevisionFile(tempPath);
        if (expectedStagedRevision is not null &&
            !stagedRevision.Equals(expectedStagedRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged visualization bytes do not match their receipt revision.");
        if (!RevisionFile(visualizationPath).Equals(expectedPreviousRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        File.Move(tempPath, visualizationPath, overwrite: true);
        return stagedRevision;
    }

    private static RasterSymbologyPalette[] ReadPalettes(XDocument visualization)
    {
        var result = new List<RasterSymbologyPalette>();
        foreach (var field in typeof(ColorPalettes).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(field => field.FieldType == typeof(ColorPalette)))
        {
            if (field.GetValue(null) is not ColorPalette palette) continue;
            result.Add(new(field.Name, palette.Name ?? field.Name, palette.Colors.Select(EncodeColor).ToArray()));
        }
        var names = result.Select(palette => palette.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var element in visualization.Root?.Element("Symbology")?.Elements("CustomSymbology") ?? [])
        {
            string name = element.Attribute("Name")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(TemporaryPalettePrefix, StringComparison.Ordinal) || names.Contains(name)) continue;
            var colors = element.Elements("Color").Select(ParseXmlColor).ToArray();
            if (colors.Length == 0) continue;
            string source = NormalizeValueSource(element.Attribute("ValueSource")?.Value ?? "Histogram");
            double[]? values = null;
            if (source == "Manual") values = element.Elements("Color").Select(color => ParseDouble(color.Attribute("Value")?.Value) ?? 0d).ToArray();
            else if (source == "Linear")
            {
                var nodes = element.Elements("Color").ToArray();
                double first = ParseDouble(nodes.First().Attribute("Value")?.Value) ?? 0d;
                double last = ParseDouble(nodes.Last().Attribute("Value")?.Value) ?? first;
                values = Enumerable.Range(0, nodes.Length).Select(index => nodes.Length == 1 ? first : first + (last - first) * index / (nodes.Length - 1d)).ToArray();
            }
            result.Add(new(PaletteID(name), name, colors.Select(EncodeColor).ToArray(), values, true));
            names.Add(name);
        }
        return result.ToArray();
    }

    private static RasterSymbologyPalette? FindPalette(XDocument visualization, string name)
    {
        var palettes = ReadPalettes(visualization);
        return palettes.FirstOrDefault(palette => palette.Id.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement EnsureSymbologyRoot(XElement visualizationRoot)
    {
        var root = visualizationRoot.Element("Symbology");
        if (root is not null) return root;
        root = new XElement("Symbology");
        visualizationRoot.AddFirst(root);
        return root;
    }

    private static void ValidateDraft(RasterSymbologyDraftContract draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Stops is null || draft.Stops.Length is < 2 or > 1024)
            throw new ArgumentException("A raster color ramp must contain between 2 and 1024 stops.");
        if (!draft.Stops.All(stop => stop.Id != Guid.Empty && double.IsFinite(stop.Value)) ||
            draft.Stops.Select(stop => stop.Id).Distinct().Count() != draft.Stops.Length)
            throw new ArgumentException("The raster color ramp contains invalid values.");
        for (int index = 0; index < draft.Stops.Length; index++)
        {
            _ = ParseColor(draft.Stops[index].Color);
            if (index > 0 && draft.Stops[index].Value <= draft.Stops[index - 1].Value)
                throw new ArgumentException("Each raster value must be greater than the previous value.");
        }
        _ = NormalizeValueSource(draft.ValueSource);
        if (draft.SmoothingRadius is { } smoothing && (!float.IsFinite(smoothing) || smoothing < 0 || smoothing > 1_000_000) ||
            draft.ResampleCellSizeRatio is { } ratio && (!float.IsFinite(ratio) || ratio < 0 || ratio > 1_000_000))
            throw new ArgumentException("Raster smoothing and resampling settings must be finite nonnegative values.");
        if (draft.Hillshade is { } hillshade && (!float.IsFinite(hillshade.ZFactor) || hillshade.ZFactor < 0 || hillshade.ZFactor > 1_000_000 ||
            !float.IsFinite(hillshade.Azimuth) || Math.Abs(hillshade.Azimuth) > 1_000_000 ||
            !float.IsFinite(hillshade.Zenith) || Math.Abs(hillshade.Zenith) > 1_000_000))
            throw new ArgumentException("Raster hillshade settings are outside the supported finite range.");
        if (draft.Contours is { } contours && (!float.IsFinite(contours.Interval) || contours.Interval <= 0 || contours.Interval > 1_000_000 ||
            contours.IndexEvery < 0 || contours.IndexEvery > 1_000_000 || contours.Opacity is < 0 or > 100 ||
            !float.IsFinite(contours.Density) || contours.Density is < 0.025f or > 4f))
            throw new ArgumentException("Raster contour settings are outside the supported range.");
        if (draft.Contours is { } contourColor) _ = ParseColor(contourColor.Color);
    }

    private static string ValidatePaletteName(string value)
    {
        string name = value.Trim();
        if (name.Length is < 1 or > 128 || name.StartsWith(TemporaryPalettePrefix, StringComparison.Ordinal) ||
            name.Any(char.IsControl)) throw new ArgumentException("Enter a valid custom palette name.");
        return name;
    }

    private static string NormalizeValueSource(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "histogram" => "Histogram", "linear" => "Linear", "manual" => "Manual",
            _ => throw new ArgumentException("Choose a supported raster value source.")
        };
    }

    private static bool ValueIsPersisted(string valueSource, int index, int count) => NormalizeValueSource(valueSource) switch
    {
        "Manual" => true, "Linear" => index == 0 || index == count - 1, _ => false
    };

    private static string PaletteID(string name) => "custom:" + name;
    private static void ValidateCount(int count)
    {
        if (count is < 2 or > 1024) throw new ArgumentException("Raster histogram color count must be between 2 and 1024.");
    }
    private static void ValidateExtent(RasterMapViewExtent extent)
    {
        if (!double.IsFinite(extent.MinX) || !double.IsFinite(extent.MinY) || !double.IsFinite(extent.MaxX) ||
            !double.IsFinite(extent.MaxY) || extent.MaxX <= extent.MinX || extent.MaxY <= extent.MinY ||
            extent.PixelWidth is < 1 or > 4096 || extent.PixelHeight is < 1 or > 4096)
            throw new ArgumentException("The map extent or raster sampling size is invalid.");
    }
    private static Color ParseXmlColor(XElement node)
    {
        if (!byte.TryParse(node.Attribute("R")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(node.Attribute("G")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(node.Attribute("B")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue))
            throw new InvalidDataException("A raster color entry is invalid.");
        return Color.FromArgb(255, red, green, blue);
    }
    private static Color ParseColor(string value)
    {
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 6) hex += "FF";
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba))
            throw new ArgumentException("A raster color is invalid.");
        return Color.FromArgb(255, (int)((rgba >> 24) & 255), (int)((rgba >> 16) & 255), (int)((rgba >> 8) & 255));
    }
    private static string EncodeColor(Color color) => color.R.ToString("X2", CultureInfo.InvariantCulture) +
        color.G.ToString("X2", CultureInfo.InvariantCulture) + color.B.ToString("X2", CultureInfo.InvariantCulture) + "FF";
    private static double? ParseDouble(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && double.IsFinite(result) ? result : null;
    private static string SourceID(int mapIndex, int[] path) => $"vis-map-{mapIndex}-node-{string.Join(".", path)}";
    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string RevisionFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void VerifySourceRevision(OperationContext context)
    {
        if (context.GdbSource is not null)
        {
            GdbRasterNodeCapture capture = context.GdbCapture ??
                throw new InvalidOperationException("The GDB raster capture is missing.");
            MapTreeRequest map = context.GdbMap ??
                throw new InvalidOperationException("The GDB map identity is missing.");
            GdbMapSourceService.VerifySource(context.GdbSource.Source);
            GdbRasterSourceService.VerifyCapture(map, capture);
            return;
        }

        string current = MapRasterLayerSourceManifest.CaptureRevision(context.SourcePath);
        if (!current.Equals(context.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string CanonicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? "";
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            FileSystemInfo? resolved = null;
            try { resolved = info.ResolveLinkTarget(returnFinalTarget: true); }
            catch (IOException) { }
            current = Path.GetFullPath(resolved?.FullName ?? candidate);
        }
        return current;
    }
    private static bool PathEquals(string first, string second) =>
        string.Equals(CanonicalPath(first), CanonicalPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class OperationContext : IDisposable
    {
        public RasterMapSourceIdentity Source { get; }
        public XDocument Visualization { get; }
        public XElement VisualizationRoot => Visualization.Root!;
        public XElement SourceNode { get; }
        public XDocument SourceRoot { get; }
        public string PreviousVisualizationRevision { get; }
        public string VisualizationRevision => PreviousVisualizationRevision;
        public string VisualizationPath { get; }
        public string SourcePath { get; }
        public string SourceRevision { get; }
        public RasterDefinition SourceRasterDefinition { get; }
        public IResample<float> Raster { get; }
        public GdbRasterSymbologyIdentity? GdbSource { get; }
        public MapTreeRequest? GdbMap { get; }
        public GdbRasterNodeCapture? GdbCapture { get; }
        public byte[]? PendingVisualizationBytes { get; set; }
        private readonly IDisposable? cleanup;

        public OperationContext(RasterMapSourceIdentity source, XDocument visualization,
            string revision, string visualizationPath, string sourcePath, string sourceRevision, RasterDefinition sourceRasterDefinition,
            IResample<float> raster, IDisposable? cleanup,
            GdbRasterSymbologyIdentity? gdbSource = null, MapTreeRequest? gdbMap = null,
            GdbRasterNodeCapture? gdbCapture = null)
        {
            Source = source; Visualization = visualization;
            PreviousVisualizationRevision = revision; VisualizationPath = visualizationPath;
            SourcePath = sourcePath;
            SourceRevision = sourceRevision; SourceRasterDefinition = sourceRasterDefinition;
            Raster = raster; this.cleanup = cleanup;
            GdbSource = gdbSource; GdbMap = gdbMap; GdbCapture = gdbCapture;
            var maps = VisualizationRoot.Element("Maps")?.Elements("Map").ToArray() ?? [];
            var map = maps[source.MapIndex];
            var nodes = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
            XElement? current = null;
            foreach (int index in source.NodePath) { current = nodes[index]; nodes = current.Elements("Node").ToArray(); }
            SourceNode = current!;
            SourceRoot = Visualization;
        }
        public void Dispose() => cleanup?.Dispose();
    }
}

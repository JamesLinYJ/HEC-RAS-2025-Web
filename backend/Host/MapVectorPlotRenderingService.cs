using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Geospatial.IO.Vectors;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Rendering.Layers.Attributes;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Attributes;
using Utility.Logging;
using Visual.Mapping.Observables;

namespace Ras.Backend;

/// <summary>
/// Executes the retained CPU vector renderers for one exact saved map node.
/// PlotOptions are applied only to a fresh renderer instance and are never
/// serialized into the .vis document.
/// </summary>
public static class VectorMapPlotRenderingService
{
    private const long MaximumSourceBytes = 512L * 1024 * 1024;
    private const long MaximumPlotPixels = 2_097_152;

    private sealed class ProjectScope(Project project) : IDisposable
    {
        public Project Project { get; } = project;
        public void Dispose() => DisposeProject(Project);
    }

    private sealed record RendererCapture(IRenderLayer Renderer, int[]? SourceFeatureIndices,
        string? GdbVectorType = null);

    public static MapVectorPlotOptionsResponse InspectOptions(MapVectorPlotOptionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommon(request.Map, request.Node, request.LayerID, request.SourceRevision);
        if (request.Node.MapType == "GdbVectorLayer")
        {
            GdbMapRendererCapture gdb = GdbMapSourceService.CreateRendererCapture(
                request.Map, request.Node, request.SourceRevision);
            Dictionary<string, (PlotOption Option, PropertyInfo Property)> gdbOptions = SourceOptions(gdb.Renderer);
            ApplyPlotOptions(gdb.Renderer, gdbOptions, request.PlotOptions ?? []);
            MapVectorPlotOptionDescriptor[] descriptors = DescribeOptions(gdb.Renderer, gdbOptions);
            GdbMapSourceService.VerifyRendererCapture(request.Map, request.Node, gdb);
            return new(request.Map.MapID, request.LayerID, gdb.SourcePath, gdb.SourceRevision,
                gdb.Map.VisualizationRevision!, descriptors);
        }
        using OperationContext context = Open(request.Map, request.Node, request.ExpectedFamily,
            request.SourceRevision);
        using ProjectScope project = OpenProject(request.Map.ProjectPath, request.Map.ProjectXml,
            request.Map.CoreLayerContext);
        RendererCapture renderer = CreateRenderer(context, project.Project);
        var optionMap = SourceOptions(renderer.Renderer);
        ApplyPlotOptions(renderer.Renderer, optionMap, request.PlotOptions ?? []);
        MapVectorPlotOptionDescriptor[] options = DescribeOptions(renderer.Renderer, optionMap);
        Verify(context, request.Map, request.Node);
        return new(request.Map.MapID, request.LayerID, context.SourcePath,
            context.SourceRevision, context.Map.VisualizationRevision!, options);
    }

    public static MapVectorPlotRenderResponse Render(MapVectorPlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCommon(request.Map, request.Node, request.LayerID, request.SourceRevision);
        ValidateViewport(request.Viewport);
        ValidateRenderScale(request.RenderScale);
        if (request.PlotOptions is null || request.Styles is null)
            throw new ArgumentException("The vector plot request is incomplete.");

        if (request.Node.MapType == "GdbVectorLayer")
        {
            GdbMapRendererCapture gdb = GdbMapSourceService.CreateRendererCapture(
                request.Map, request.Node, request.SourceRevision);
            var gdbCapture = new RendererCapture(gdb.Renderer, gdb.SourceFeatureIndices, gdb.GdbVectorType);
            var gdbOptions = SourceOptions(gdbCapture.Renderer);
            ApplyPlotOptions(gdbCapture.Renderer, gdbOptions, request.PlotOptions);
            MapVectorPlotOptionDescriptor[] descriptors = DescribeOptions(gdbCapture.Renderer, gdbOptions);
            ApplyStyles(gdbCapture.Renderer, request.Styles, gdbCapture.SourceFeatureIndices, gdbCapture.GdbVectorType);
            return RenderCaptured(request.Map.MapID, request.LayerID, gdb.SourcePath, gdb.SourceRevision,
                gdb.Map.VisualizationRevision!, request.Viewport, request.RenderScale, gdbCapture, descriptors,
                () => GdbMapSourceService.VerifyRendererCapture(request.Map, request.Node, gdb));
        }

        using OperationContext context = Open(request.Map, request.Node, request.ExpectedFamily,
            request.SourceRevision);
        using ProjectScope project = OpenProject(request.Map.ProjectPath, request.Map.ProjectXml,
            request.Map.CoreLayerContext);
        RendererCapture capture = CreateRenderer(context, project.Project);
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options = SourceOptions(capture.Renderer);
        ApplyPlotOptions(capture.Renderer, options, request.PlotOptions);
        MapVectorPlotOptionDescriptor[] optionDescriptors = DescribeOptions(capture.Renderer, options);
        ApplyStyles(capture.Renderer, request.Styles, capture.SourceFeatureIndices, capture.GdbVectorType);

        return RenderCaptured(request.Map.MapID, request.LayerID, context.SourcePath,
            context.SourceRevision, context.Map.VisualizationRevision!, request.Viewport,
            request.RenderScale, capture, optionDescriptors, () => Verify(context, request.Map, request.Node));
    }

    private static MapVectorPlotRenderResponse RenderCaptured(string mapID, string layerID,
        string sourcePath, string sourceRevision, string visualizationRevision,
        RasterMapViewExtent viewport, double renderScale, RendererCapture capture,
        MapVectorPlotOptionDescriptor[] optionDescriptors, Action verify)
    {
        var extent = new Extent(viewport.MinX, viewport.MaxX, viewport.MinY, viewport.MaxY);
        var raster = new RasterDefinition(extent, viewport.PixelHeight, viewport.PixelWidth);
        if (!raster.IsValid() || raster.CellCount != viewport.PixelWidth * viewport.PixelHeight)
            throw new ArgumentException("The vector plot viewport could not be represented as a source raster.");

        using PlotRequest plot = RenderAtScale(capture.Renderer, raster, renderScale);
        if (plot.ColorBufferARGB.Length != raster.CellCount)
            throw new InvalidDataException("The retained vector renderer returned an incomplete pixel buffer.");
        byte[] rgba = PremultipliedRgba(plot.ColorBufferARGB.Span);
        MapVectorPlotLabel[] labels = (plot.Labels ?? []).Select(label =>
        {
            Point point = label.PointLocation;
            MapVectorPlotCoordinate[] polyline = label.PolylineLocation.ToArray()
                .Select(value => new MapVectorPlotCoordinate(value.X, value.Y)).ToArray();
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                polyline.Any(value => !double.IsFinite(value.X) || !double.IsFinite(value.Y)))
                throw new InvalidDataException("The retained vector renderer returned a non-finite label location.");
            return new MapVectorPlotLabel(label.Text ?? "",
                new MapVectorPlotCoordinate(point.X, point.Y), polyline);
        }).ToArray();

        verify();
        return new MapVectorPlotRenderResponse(mapID, layerID,
            sourcePath, sourceRevision, visualizationRevision, viewport,
            viewport.PixelWidth, viewport.PixelHeight, rgba, labels, optionDescriptors, renderScale);
    }

    private static PlotRequest RenderAtScale(IRenderLayer renderer, RasterDefinition raster, double renderScale)
    {
        double previousRenderScale = VectorManagerOptions.RenderScaling;
        try
        {
            VectorManagerOptions.RenderScaling = renderScale;
            return renderer.Render(raster);
        }
        finally
        {
            VectorManagerOptions.RenderScaling = previousRenderScale;
        }
    }

    private sealed class OperationContext : IDisposable
    {
        public MapTreeRequest TreeRequest { get; }
        public MapSourceNodeCapture Capture { get; }
        public MapSourceMapCapture Map => Capture.Map;
        public string SourcePath => Capture.Family.SourcePath;
        // This exact persisted-row path is only a format hint; readers receive SourcePath for bytes and sidecars.
        public string FormatHintPath { get; }
        public string SourceRevision { get; }

        public OperationContext(MapTreeRequest treeRequest, MapSourceNodeCapture capture,
            string formatHintPath, string sourceRevision)
        {
            TreeRequest = treeRequest;
            Capture = capture;
            FormatHintPath = formatHintPath;
            SourceRevision = sourceRevision;
        }

        public void Dispose() { }
    }

    private static OperationContext Open(MapTreeRequest map, CatalogTreeNodeLocator node,
        MapSourceFamilyCapture? expectedFamily, string expectedSourceRevision)
    {
        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedNode(map, node);
        string path = location.SourcePath;
        string presentationPath = location.PresentationPath ?? path;
        if (node.AssetID is not null && !PathComparer.Equals(
                SourceFilePaths.Canonical(node.AssetID), SourceFilePaths.Canonical(path)))
            throw new SourceFileChangedException();
        MapSourceFamilyCapture family;
        if (Path.GetExtension(path).Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            if (expectedFamily is null)
                throw new SourceFileChangedException();
            MapShapefileImportService.VerifyExpectedFamily(expectedFamily, path);
            family = expectedFamily;
        }
        else if (GeoJsonReader.HasJsonFileExtension(presentationPath))
        {
            if (expectedFamily is not null &&
                (!expectedFamily.Family.Equals("geojson", StringComparison.Ordinal) ||
                 !PathComparer.Equals(expectedFamily.SourcePath, SourceFilePaths.Canonical(path))))
                throw new SourceFileChangedException();
            family = expectedFamily ?? MapSourceIdentity.CaptureFamily("geojson", path, []);
            MapSourceIdentity.VerifyFamily(family);
        }
        else
        {
            throw new NotSupportedException("Only source-backed GeoJSON and Shapefile vector layers can use the retained map renderer.");
        }

        string sourceRevision = SourceRevisionOf(family);
        if (!sourceRevision.Equals(expectedSourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        if (new FileInfo(path).Length > MaximumSourceBytes)
            throw new InvalidDataException("The vector source exceeds the supported retained-renderer size.");

        MapSourceNodeCapture capture = MapSourceIdentity.ResolvePersistedNode(map, node, family);
        MapSourceIdentity.VerifyPersistedNode(map, capture);
        return new OperationContext(map, capture, presentationPath, sourceRevision);
    }

    private static ProjectScope OpenProject(string projectPath, string? projectXml,
        CoreLayerRequestContext? coreLayerContext) =>
        new(CoreLayerImportService.OpenProject(Path.GetFullPath(projectPath), projectXml, coreLayerContext));

    private static RendererCapture CreateRenderer(OperationContext context, Project project)
    {
        string type = context.Capture.Locator.MapType ?? "";
        IRenderLayer renderer;
        switch (type)
        {
            case "PointsVector":
            {
                var operation = PointMapLayerVisualContext.RenderLayerFrom(context.SourcePath, context.FormatHintPath,
                    project.Projection, out _, out _, out PointRenderLayer pointRenderer);
                EnsureRenderLayer(operation, pointRenderer);
                renderer = pointRenderer;
                break;
            }
            case "PolylinesVector":
            {
                var operation = PolylineMapLayerVisualContext.RenderLayerFrom(context.SourcePath, context.FormatHintPath,
                    project.Projection, out _, out _, out PolylineRenderLayer lineRenderer);
                EnsureRenderLayer(operation, lineRenderer);
                renderer = lineRenderer;
                break;
            }
            case "PolygonsVector":
            {
                var operation = PolygonMapLayerVisualContext.RenderLayerFrom(context.SourcePath, context.FormatHintPath,
                    project.Projection, out _, out _, out PolygonRenderLayer polygonRenderer);
                EnsureRenderLayer(operation, polygonRenderer);
                renderer = polygonRenderer;
                break;
            }
            default:
                throw new NotSupportedException("The selected source map node is not a retained point, polyline, or polygon renderer.");
        }

        int[]? sourceIndices = null;
        if (Path.GetExtension(context.SourcePath).Equals(".shp", StringComparison.OrdinalIgnoreCase))
        {
            MapVectorSourceLoadResult source = MapVectorSourceService.Load(new MapVectorSourceLoadRequest(
                context.TreeRequest, context.Capture.Locator, context.Capture.Family));
            string expectedShape = type switch
            {
                "PointsVector" => "Point",
                "PolylinesVector" => "Polyline",
                "PolygonsVector" => "Polygon",
                _ => ""
            };
            if (source.Data.ShapeType == expectedShape)
            {
                int[] indices = source.Data.Features.SelectMany(feature =>
                    Enumerable.Repeat(feature.SourceOrdinal, feature.Parts.Length)).ToArray();
                if (indices.Length == rendererCount(renderer)) sourceIndices = indices;
            }
        }
        else
        {
            GeoJsonFeatureCollection source = GeoJsonReader.Deserialize(File.ReadAllText(context.SourcePath));
            string expectedGeometry = type switch
            {
                "PointsVector" => "Point",
                "PolylinesVector" => "LineString",
                "PolygonsVector" => "Polygon",
                _ => ""
            };
            int[] indices = (source.Features ?? []).Select((feature, index) => (feature, index))
                .Where(pair => pair.feature.Geometry?.Type == expectedGeometry)
                .Select(pair => pair.index).ToArray();
            if (indices.Length == rendererCount(renderer)) sourceIndices = indices;
        }
        return new RendererCapture(renderer, sourceIndices);
    }

    private static int rendererCount(IRenderLayer renderer) => renderer switch
    {
        PointRenderLayer points => points.Count,
        PolylineRenderLayer lines => lines.Count,
        PolygonRenderLayer polygons => polygons.Count,
        ComplexPolygonRenderLayer polygons => polygons.Count,
        _ => -1
    };

    private static void EnsureRenderLayer(Utility.Logging.OperationResult operation, IRenderLayer? renderer)
    {
        if (!operation.Result || renderer is null)
        {
            string detail = operation.GetConcatenatedMessages();
            throw new InvalidDataException(string.IsNullOrWhiteSpace(detail)
                ? "The retained source map renderer could not load this vector layer." : detail);
        }
    }

    private static Dictionary<string, (PlotOption Option, PropertyInfo Property)> SourceOptions(IRenderLayer renderer) =>
        renderer.GetPlotOptions().ToDictionary(pair => pair.Value.Name,
            pair => (pair.Key, pair.Value), StringComparer.Ordinal);

    private static MapVectorPlotOptionDescriptor[] DescribeOptions(IRenderLayer renderer,
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options) => options
        .Select(pair =>
        {
            PlotOption option = pair.Value.Option;
            PropertyInfo property = pair.Value.Property;
            object? current = property.GetValue(renderer);
            string value = current switch
            {
                bool boolean => boolean ? "true" : "false",
                Enum enumeration => enumeration.ToString(),
                _ => throw new InvalidDataException("The retained vector renderer exposed an unsupported PlotOption type.")
            };
            bool implemented = IsPlotOptionImplemented(renderer, property);
            bool enabled = implemented && IsEnabled(renderer, option);
            string description = option.Description;
            if (!implemented)
                description = string.IsNullOrWhiteSpace(description)
                    ? "The retained renderer does not implement the experimental TIN drawing path."
                    : description + "\nThe retained renderer does not implement the experimental TIN drawing path.";
            if (property.PropertyType == typeof(bool))
                return new MapVectorPlotOptionDescriptor(property.Name, option.Name, option.Label,
                    description, "bool", value, [], enabled, option.EnabledIf);
            MapVectorPlotOptionChoice[] values = Enum.GetNames(property.PropertyType).Select(name =>
            {
                MemberInfo member = property.PropertyType.GetMember(name).First();
                string label = member.GetCustomAttribute<EnumDisplayName>()?.DisplayName
                    ?? member.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
                return new MapVectorPlotOptionChoice(name, label);
            }).ToArray();
            return new MapVectorPlotOptionDescriptor(property.Name, option.Name, option.Label,
                description, "enum", value, values, enabled, option.EnabledIf);
        })
        .OrderBy(item => item.PropertyName, StringComparer.Ordinal)
        .ToArray();

    private static void ApplyPlotOptions(IRenderLayer renderer,
        Dictionary<string, (PlotOption Option, PropertyInfo Property)> options,
        IReadOnlyDictionary<string, string> values)
    {
        foreach ((string name, string value) in values)
        {
            if (!options.TryGetValue(name, out var source))
                throw new ArgumentException($"The source {renderer.GetType().Name} has no PlotOption named '{name}'.");
            if (!IsPlotOptionImplemented(renderer, source.Property))
                throw new NotSupportedException("The retained source renderer does not implement this drawing option.");
            Type propertyType = source.Property.PropertyType;
            object parsed;
            if (propertyType == typeof(bool) && bool.TryParse(value, out bool boolean)) parsed = boolean;
            else if (propertyType.IsEnum && Enum.TryParse(propertyType, value, ignoreCase: false, out object? enumeration) &&
                     enumeration is not null && Enum.IsDefined(propertyType, enumeration)) parsed = enumeration;
            else throw new ArgumentException($"The source PlotOption '{name}' has an invalid value.");
            source.Property.SetValue(renderer, parsed);
        }
        foreach (string name in values.Keys)
            if (!IsEnabled(renderer, options[name].Option))
                throw new ArgumentException($"The source PlotOption '{name}' is disabled by its current renderer state.");
    }

    private static bool IsEnabled(IRenderLayer renderer, PlotOption option)
    {
        if (string.IsNullOrWhiteSpace(option.EnabledIf)) return true;
        PropertyInfo? condition = renderer.GetType().GetProperty(option.EnabledIf,
            BindingFlags.Instance | BindingFlags.Public);
        if (condition?.PropertyType != typeof(bool))
            throw new InvalidOperationException($"PlotOption condition '{option.EnabledIf}' is not a Boolean source property.");
        return condition.GetValue(renderer) is true;
    }

    private static bool IsPlotOptionImplemented(IRenderLayer renderer, PropertyInfo property) =>
        renderer is not ComplexPolygonRenderLayer ||
        property.Name != nameof(ComplexPolygonRenderLayer.UseExperimentalTinRenderer);

    private static void ApplyStyles(IRenderLayer renderer, MapVectorPlotStyle[] styles,
        int[]? sourceFeatureIndices, string? gdbVectorType)
    {
        if (renderer is not VectorRenderLayer vector)
            throw new InvalidOperationException("The retained vector renderer does not expose source symbology.");
        string primaryID = gdbVectorType switch
        {
            "Point" or "PointCollection" when renderer is PointRenderLayer => "Gdb_Point_Symbology",
            "Line" or "LineCollection" when renderer is PolylineRenderLayer => "Gdb_Line_Symbology",
            "Polygon" or "PolygonCollection" when renderer is ComplexPolygonRenderLayer => "Gdb_Polygon_Symbology",
            not null => throw new InvalidDataException("The GDB source geometry does not match its retained renderer."),
            _ => renderer switch
            {
                PointRenderLayer => "Point_Map_Layer_Symbology",
                PolylineRenderLayer => "Polyline_Line_Symbology",
                PolygonRenderLayer => "Polygon_Map_Layer_Shape_Symbology",
                _ => throw new NotSupportedException("The retained vector renderer has no map-layer style contract.")
            }
        };
        var allowed = gdbVectorType switch
        {
            "Point" or "PointCollection" => new HashSet<string>([primaryID], StringComparer.Ordinal),
            "Line" or "LineCollection" => new HashSet<string>([primaryID, "Gdb_Line_Vertex_Symbology"], StringComparer.Ordinal),
            "Polygon" or "PolygonCollection" => new HashSet<string>([primaryID], StringComparer.Ordinal),
            not null => throw new InvalidDataException("The GDB source geometry type is unsupported."),
            _ => renderer switch
            {
            PointRenderLayer => new HashSet<string>([primaryID], StringComparer.Ordinal),
            PolylineRenderLayer => new HashSet<string>([primaryID, "Polyline_Vertex_Symbology"], StringComparer.Ordinal),
            PolygonRenderLayer => new HashSet<string>([primaryID, "Polygon_Map_Layer_Vertex_Symbology"], StringComparer.Ordinal),
            _ => new HashSet<string>([primaryID], StringComparer.Ordinal)
            }
        };
        if (styles.Any(style => !allowed.Contains(style.DefinitionID)) ||
            styles.Select(style => style.DefinitionID).Distinct(StringComparer.Ordinal).Count() != styles.Length)
            throw new ArgumentException("The vector plot styles do not match the selected source renderer.");
        MapVectorPlotStyle primary = styles.SingleOrDefault(style => style.DefinitionID == primaryID)
            ?? throw new ArgumentException("The vector plot request is missing its primary source style.");
        vector.VectorSymbology = MakeSymbology(primary, sourceFeatureIndices);
        string lineVertexID = gdbVectorType is null ? "Polyline_Vertex_Symbology" : "Gdb_Line_Vertex_Symbology";
        if (renderer is PolylineRenderLayer line &&
            styles.SingleOrDefault(style => style.DefinitionID == lineVertexID) is { } lineVertex)
            line.VertexSymbology = MakeSymbology(lineVertex, sourceFeatureIndices);
        if (renderer is PolygonRenderLayer polygon &&
            styles.SingleOrDefault(style => style.DefinitionID == "Polygon_Map_Layer_Vertex_Symbology") is { } polygonVertex)
            polygon.VertexSymbology = MakeSymbology(polygonVertex, sourceFeatureIndices);
    }

    private static VectorSymbology MakeSymbology(MapVectorPlotStyle style, int[]? sourceFeatureIndices)
    {
        ArgumentNullException.ThrowIfNull(style.SourceOverrides);
        var values = ValidateStyle(style.Base);
        var baseStyle = new VectorSymbology(ParseARGB(values.PenColor), values.PenWidth,
            ParseARGB(values.FillColor))
        {
            PointSize = values.PointSize,
            HaloColor = ParseARGB(values.HaloColor),
            HaloThickness = checked((float)values.HaloThickness),
            TextEmphasis = values.TextEmphasis
        };
        if (style.SourceOverrides.Length == 0) return baseStyle;
        if (sourceFeatureIndices is null)
            throw new NotSupportedException("Conditional vector styles cannot be rendered because source feature ordinals were not proven.");
        Dictionary<int, MapVectorPlotStyleValues> bySourceIndex = style.SourceOverrides.ToDictionary(
            item => item.SourceFeatureIndex, item => ValidateStyle(item.Values));
        if (bySourceIndex.Keys.Any(index => index < 0))
            throw new ArgumentException("A conditional source style has an invalid source row.");
        foreach (MapVectorPlotStyleValues value in bySourceIndex.Values)
            if (value.HaloColor != values.HaloColor || value.HaloThickness != values.HaloThickness ||
                value.TextEmphasis != values.TextEmphasis)
                throw new NotSupportedException("The source conditional renderer does not support per-row halo or text overrides.");

        var conditional = new ConditionalVectorSymbology(baseStyle.PenColor, baseStyle.PenWidth, baseStyle.FillColor)
        {
            PointSize = baseStyle.PointSize,
            HaloColor = baseStyle.HaloColor,
            HaloThickness = baseStyle.HaloThickness,
            TextEmphasis = baseStyle.TextEmphasis
        };
        conditional.GetPenColorOverride = fid => Override(fid)?.PenColor is string pen ? ParseARGB(pen) : null;
        conditional.GetPenWidthOverride = fid => Override(fid)?.PenWidth;
        conditional.GetFillColorOverride = fid => Override(fid)?.FillColor is string fill ? ParseARGB(fill) : null;
        conditional.GetPointSizeOverride = fid => Override(fid)?.PointSize;
        return conditional;

        MapVectorPlotStyleValues? Override(int featureID)
        {
            if ((uint)featureID >= (uint)sourceFeatureIndices.Length) return null;
            return bySourceIndex.GetValueOrDefault(sourceFeatureIndices[featureID]);
        }
    }

    private static MapVectorPlotStyleValues ValidateStyle(MapVectorPlotStyleValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _ = ParseARGB(values.PenColor);
        _ = ParseARGB(values.FillColor);
        _ = ParseARGB(values.HaloColor);
        if (!double.IsFinite(values.PenWidth) || !double.IsFinite(values.HaloThickness) ||
            values.HaloThickness < -float.MaxValue || values.HaloThickness > float.MaxValue)
            throw new ArgumentException("The vector style contains a non-finite or unsupported measurement.");
        return values;
    }

    private static System.Drawing.Color ParseARGB(string value)
    {
        if (value is null || value.Length != 9 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            throw new ArgumentException("Vector style colors must use #AARRGGBB source format.");
        return System.Drawing.Color.FromArgb(unchecked((int)argb));
    }

    private static byte[] PremultipliedRgba(Span<int> argb)
    {
        byte[] bytes = new byte[checked(argb.Length * 4)];
        for (int index = 0; index < argb.Length; index++)
        {
            uint color = unchecked((uint)argb[index]);
            byte alpha = (byte)(color >> 24);
            bytes[index * 4] = (byte)((((color >> 16) & 0xff) * alpha + 127) / 255);
            bytes[index * 4 + 1] = (byte)((((color >> 8) & 0xff) * alpha + 127) / 255);
            bytes[index * 4 + 2] = (byte)(((color & 0xff) * alpha + 127) / 255);
            bytes[index * 4 + 3] = alpha;
        }
        return bytes;
    }

    private static void Verify(OperationContext context, MapTreeRequest map, CatalogTreeNodeLocator node)
    {
        MapSourceIdentity.VerifyPersistedNode(map, context.Capture);
        if (!SameLocator(context.Capture.Locator, node) ||
            !context.SourceRevision.Equals(SourceRevisionOf(context.Capture.Family), StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string SourceRevisionOf(MapSourceFamilyCapture family)
    {
        MapSourceFileRevision[] sources = family.Files.Where(file => file.Role == "source").ToArray();
        if (sources.Length != 1 || sources[0].Revision is not string revision || !IsSHA256(revision))
            throw new SourceFileChangedException();
        return revision;
    }

    private static bool SameLocator(CatalogTreeNodeLocator first, CatalogTreeNodeLocator second) =>
        first.RootName == second.RootName && first.NodeType == second.NodeType && first.NodeName == second.NodeName &&
        first.AssetID == second.AssetID &&
        first.MapType == second.MapType && first.Filename == second.Filename &&
        first.FilenameRelative == second.FilenameRelative &&
        (first.NodePath ?? []).SequenceEqual(second.NodePath ?? []) &&
        first.ParentGeometryAssetID == second.ParentGeometryAssetID &&
        (first.ParentGeometryNodePath ?? []).SequenceEqual(second.ParentGeometryNodePath ?? []) &&
        first.ParentGeometryNodeName == second.ParentGeometryNodeName &&
        first.AssociationAssetID == second.AssociationAssetID &&
        first.AssociationKind == second.AssociationKind &&
        first.ParentPlanAssetID == second.ParentPlanAssetID &&
        (first.ParentPlanNodePath ?? []).SequenceEqual(second.ParentPlanNodePath ?? []) &&
        first.ParentPlanNodeName == second.ParentPlanNodeName &&
        first.ParentResultNodeType == second.ParentResultNodeType &&
        first.ParentResultAssetID == second.ParentResultAssetID &&
        (first.ParentResultNodePath ?? []).SequenceEqual(second.ParentResultNodePath ?? []) &&
        first.ParentResultNodeName == second.ParentResultNodeName &&
        first.ParentResultRevision == second.ParentResultRevision &&
        first.ParentBoundaryConditionAssetID == second.ParentBoundaryConditionAssetID &&
        (first.ParentBoundaryConditionNodePath ?? []).SequenceEqual(second.ParentBoundaryConditionNodePath ?? []) &&
        first.ParentBoundaryConditionNodeName == second.ParentBoundaryConditionNodeName;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private static void ValidateCommon(MapTreeRequest? map, CatalogTreeNodeLocator? node,
        string? layerID, string? sourceRevision)
    {
        bool gdbNode = node is not null && node.NodeType == "GdbVectorLayer" && node.MapType == "GdbVectorLayer";
        bool fileNode = node is not null && node.NodeType == "MapLayer" &&
            (node.MapType is "PointsVector" or "PolylinesVector" or "PolygonsVector");
        if (map is null || node is null || string.IsNullOrWhiteSpace(layerID) ||
            string.IsNullOrWhiteSpace(sourceRevision) || !IsSHA256(sourceRevision))
            throw new ArgumentException("The vector plot request has an incomplete owner or source revision.");
        if ((!fileNode && !gdbNode) || node.NodePath is not { Length: > 0 } ||
            string.IsNullOrWhiteSpace(node.RootName) || string.IsNullOrWhiteSpace(node.NodeName) ||
            string.IsNullOrWhiteSpace(node.Filename) || string.IsNullOrWhiteSpace(node.FilenameRelative))
            throw new NotSupportedException("The selected map node is not a supported retained vector or GDB layer.");
        _ = MapSourceIdentity.CaptureMap(map);
    }

    private static void ValidateViewport(RasterMapViewExtent viewport)
    {
        if (viewport is null || !double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MinX >= viewport.MaxX || viewport.MinY >= viewport.MaxY ||
            viewport.PixelWidth is < 1 or > 4096 || viewport.PixelHeight is < 1 or > 4096 ||
            (long)viewport.PixelWidth * viewport.PixelHeight > MaximumPlotPixels)
            throw new ArgumentException("The vector plot viewport must be finite and within the retained renderer pixel limit.");
    }

    private static void ValidateRenderScale(double renderScale)
    {
        if (!double.IsFinite(renderScale) || renderScale <= 0)
            throw new ArgumentException("The vector source render scale must be finite and greater than zero.");
    }

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries)
                     .Concat(project.Terrains).Concat(project.BoundaryConditions).Concat(project.NValues)
                     .Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }
}

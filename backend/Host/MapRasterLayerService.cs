using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.Rendering.Layers;
using Geospatial.Transformers;
using Ras.Layers;

namespace Ras.Backend;

/// <summary>
/// Source-backed GDAL import preview and map raster loading. The selected
/// file remains the `.vis` Filename; pixels are rendered from its current GDAL
/// source context and are never persisted as a replacement raster.
/// </summary>
public static class MapRasterLayerService
{
    private const long MaximumVisualizationBytes = 32L * 1024 * 1024;
    private const long MaximumProjectXmlBytes = 8L * 1024 * 1024;
    private const long MaximumPreviewCells = 104_857_600;
    private const long MaximumRenderPixels = 2_097_152;
    private const int MaximumRenderSide = 1536;

    public static MapRasterLayerInspectResult Inspect(MapRasterLayerInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = MapRasterLayerSourceManifest.CanonicalPath(request.SourcePath);
        string revision = MapRasterLayerSourceManifest.CaptureRevision(path);
        using var raster = new GDALRaster(path);
        if (raster.PixelsWide <= 0 || raster.PixelsTall <= 0)
            throw new InvalidDataException("The selected GDAL raster has no cells.");
        string mapLayerType = MapRasterLayerRendering.ResolveMapType(raster);
        RasterImportContextSource properties = RasterImportContextPropertiesService.Inspect(
            new RasterImportContextPropertiesRequest([path])).Sources.Single();
        MapRasterLayerSourceManifest.RequireCurrent(path, revision);
        if (properties.Error is not null || properties.Info is null || properties.Stats is null)
            throw new InvalidDataException(properties.Error ?? "The raster properties could not be read.");
        Projection? projection = raster.GetProjection();
        long previewCells = raster.GetPyramidLevels(1)[^1].CellCountLong;
        return new(path, path, revision, raster.DriverLongName, raster.GetDataType().Name ?? "Unknown",
            raster.PixelsWide, raster.PixelsTall, raster.GetBandCount(), mapLayerType, [mapLayerType],
            projection?.GetPrettyWKT(),
            projection is not null && projection.TryParseUnitSystem(out var units)
                ? units.ToString() : null,
            raster.GetCellSizeX(), previewCells <= MaximumPreviewCells,
            properties.Info, properties.Stats);
    }

    public static MapRasterLayerPreviewResult Preview(MapRasterLayerPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectPath);
        if (request.CoreLayerContext is not null)
            CoreLayerImportService.ValidateContext(request.CoreLayerContext, Path.GetFullPath(request.ProjectPath));
        string path = MapRasterLayerSourceManifest.CanonicalPath(request.SourcePath);
        MapRasterLayerSourceManifest.RequireCurrent(path, request.SourceRevision);
        string expectedType;
        long previewCells;
        using (var raster = new GDALRaster(path))
        {
            if (raster.PixelsWide <= 0 || raster.PixelsTall <= 0)
                throw new InvalidDataException("The selected GDAL raster has no cells.");
            expectedType = MapRasterLayerRendering.ResolveMapType(raster);
            previewCells = raster.GetPyramidLevels(1)[^1].CellCountLong;
        }
        if (!MapRasterLayerRendering.SupportedTypes.Contains(request.MapLayerType, StringComparer.Ordinal) ||
            !string.Equals(expectedType, request.MapLayerType, StringComparison.Ordinal))
            throw new ArgumentException("The selected map layer type does not match the current GDAL raster.");
        if (previewCells > MaximumPreviewCells)
            throw new NotSupportedException("This raster is too large for an import dialog preview.");

        Projection? targetProjection = ReadProjectProjection(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA);
        using MapRasterLayerRendering.IDisposableRenderer renderer =
            MapRasterLayerRendering.Open(path, request.MapLayerType, targetProjection);
        RasterMapViewExtent viewport = request.Viewport is null
            ? PreviewViewport(renderer.Layer.ValidExtent)
            : ValidateViewport(request.Viewport);
        MapSourceRasterFrame frame = MapSourceRasterRendering.Render(renderer.Layer, viewport);
        MapRasterLayerSourceManifest.RequireCurrent(path, request.SourceRevision);
        return new(new RasterMapLayerPayload("preview:" + request.SourceRevision, null,
            Path.GetFileNameWithoutExtension(path), frame.Width, frame.Height, frame.Rgba,
            frame.MinX, frame.MinY, frame.MaxX, frame.MaxY));
    }

    public static MapRasterLayerReadResult Load(MapRasterLayerReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RasterMapSourceIdentity source = request.Source ?? throw new ArgumentException("The map raster source identity is missing.");
        if (source.SourceMapID is null || !Guid.TryParse(source.SourceMapID, out _) ||
            source.MapLayerType is not (MapRasterLayerRendering.SingleBandMapType or
                MapRasterLayerRendering.ImageMapType or MapRasterLayerRendering.DiscreteMapType))
            throw new ArgumentException("The map raster source identity is incomplete.");

        if (source.GdbSource is not null) return LoadGdb(request);

        if (source.MapLayerType == MapRasterLayerRendering.SingleBandMapType)
        {
            // Keep existing single-band symbology and plot-option behavior. The
            // manifest revision wraps the source reader's file revision so VRT
            // dependencies remain part of identity without changing its API.
            string expectedManifest = MapRasterLayerSourceManifest.CaptureRevision(source.SourcePath);
            if (source.SourceRevision is not null &&
                !source.SourceRevision.Equals(expectedManifest, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            RasterSymbologyInspectResult loaded = RasterSymbologyService.Load(
                new RasterSymbologyRequest(source with { SourceRevision = null }, request.ProjectXml));
            string manifestAfterRead = MapRasterLayerSourceManifest.CaptureRevision(source.SourcePath);
            if (!manifestAfterRead.Equals(expectedManifest, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            RasterImportContextSource? stats = loaded.RasterInfoStats is null
                ? null : loaded.RasterInfoStats with { Revision = expectedManifest };
            return new(loaded.PreviousVisualizationRevision, loaded.VisualizationRevision,
                expectedManifest, loaded.Raster, stats);
        }

        ReadMapLayerContext context = Open(source, request.ProjectXml);
        Projection? targetProjection = context.TargetProjection;
        using MapRasterLayerRendering.IDisposableRenderer renderer =
            MapRasterLayerRendering.Open(context.SourcePath, source.MapLayerType, targetProjection);
        RasterMapViewExtent? viewport = request.Viewport;
        MapSourceRasterFrame frame = viewport is null
            ? MapSourceRasterRendering.Render(renderer.Layer, FullExtentViewport(renderer.Layer.ValidExtent))
            : MapSourceRasterRendering.Render(renderer.Layer, ValidateViewport(viewport));
        RasterImportContextSource properties = RasterImportContextPropertiesService.Inspect(
            new RasterImportContextPropertiesRequest([context.SourcePath])).Sources.Single();
        if (properties.Error is not null || properties.Info is null || properties.Stats is null)
            throw new InvalidDataException(properties.Error ?? "The raster properties could not be read.");
        string after = MapRasterLayerSourceManifest.CaptureRevision(context.SourcePath);
        if (!after.Equals(context.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        properties = properties with { Revision = context.SourceRevision };
        return new(context.VisualizationRevision, context.VisualizationRevision, context.SourceRevision,
            new RasterMapLayerPayload(source.Id, source.SourceMapID, source.NodeName, frame.Width, frame.Height,
                frame.Rgba, frame.MinX, frame.MinY, frame.MaxX, frame.MaxY), properties);
    }

    private static MapRasterLayerReadResult LoadGdb(MapRasterLayerReadRequest request)
    {
        RasterMapSourceIdentity source = request.Source;
        MapTreeRequest map = GdbRasterSourceService.ValidateRasterIdentity(source, request.ProjectXml);
        if (source.MapLayerType == MapRasterLayerRendering.SingleBandMapType)
        {
            RasterSymbologyInspectResult loaded = RasterSymbologyService.Load(
                new RasterSymbologyRequest(source, request.ProjectXml));
            return new(loaded.PreviousVisualizationRevision, loaded.VisualizationRevision,
                loaded.SourceRevision, loaded.Raster, loaded.RasterInfoStats);
        }
        // GDB image/classification frames use the full source extent. Do not silently
        // accept a viewport until this path also binds its CRS and sampling capture.
        if (request.Viewport is not null)
            throw new NotSupportedException("GDB classification and image layers currently render their full source extent.");
        GdbRasterSymbologyIdentity owner = source.GdbSource!;
        return GdbRasterSourceService.WithOpenedDataset(
            new GdbRasterSourceLoadRequest(map, owner.Node, owner.Source), opened =>
            {
                Projection? targetProjection = ReadProjectProjection(source.ProjectPath, request.ProjectXml,
                    map.ProjectXmlSHA);
                using MapRasterLayerRendering.IDisposableRenderer renderer = MapRasterLayerRendering.Open(
                    opened.Subdataset.ConnectionString, source.MapLayerType, targetProjection);
                // Bound by source pixel dimensions rather than map-unit width: a 20×20
                // source with metre coordinates still returns its actual 20×20 image.
                double scale = Math.Min(1d, Math.Min(
                    (double)MaximumRenderSide / Math.Max(opened.Subdataset.Width, opened.Subdataset.Height),
                    Math.Sqrt((double)MaximumRenderPixels /
                        ((long)opened.Subdataset.Width * opened.Subdataset.Height))));
                var extent = renderer.Layer.ValidExtent;
                var viewport = new RasterMapViewExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY,
                    Math.Max(1, (int)Math.Round(opened.Subdataset.Width * scale)),
                    Math.Max(1, (int)Math.Round(opened.Subdataset.Height * scale)));
                MapSourceRasterFrame frame = MapSourceRasterRendering.Render(renderer.Layer, viewport);
                string revision = opened.Capture.Map.VisualizationRevision ?? throw new SourceFileChangedException();
                return new MapRasterLayerReadResult(revision, revision, opened.Capture.Source.Revision,
                    new RasterMapLayerPayload(source.Id, source.SourceMapID, source.NodeName,
                        frame.Width, frame.Height, frame.Rgba, frame.MinX, frame.MinY, frame.MaxX, frame.MaxY));
            });
    }

    private static ReadMapLayerContext Open(RasterMapSourceIdentity source, string? projectXml)
    {
        if (string.IsNullOrWhiteSpace(source.ProjectPath) || string.IsNullOrWhiteSpace(source.VisualizationPath) ||
            string.IsNullOrWhiteSpace(source.SourcePath) || source.NodePath is null ||
            source.NodePath.Length == 0 || source.NodePath.Any(index => index < 0))
            throw new ArgumentException("The raster map source identity is incomplete.");
        string projectPath = MapRasterLayerSourceManifest.CanonicalPath(source.ProjectPath);
        string visualizationPath = MapRasterLayerSourceManifest.CanonicalPath(source.VisualizationPath);
        string expectedVisualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!SamePath(visualizationPath, expectedVisualizationPath))
            throw new ArgumentException("The visualization file does not belong to this project.");
        byte[] bytes = File.ReadAllBytes(visualizationPath);
        if (bytes.LongLength > MaximumVisualizationBytes)
            throw new NotSupportedException("The visualization file is too large to read.");
        string visualizationRevision = Convert.ToHexString(SHA256.HashData(bytes));
        if (!visualizationRevision.Equals(source.VisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        XDocument visualization = ParseXml(bytes, MaximumVisualizationBytes);
        XElement[] maps = visualization.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
        if (visualization.Root?.Name != "Ras" || source.MapIndex < 0 || source.MapIndex >= maps.Length)
            throw new SourceFileChangedException();
        XElement map = maps[source.MapIndex];
        if (!string.Equals((string?)map.Attribute("Name"), source.MapName, StringComparison.Ordinal) ||
            !string.Equals((string?)map.Attribute("Type"), source.MapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        XElement[] nodes = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        XElement? sourceNode = null;
        foreach (int index in source.NodePath)
        {
            if ((uint)index >= (uint)nodes.Length) throw new SourceFileChangedException();
            sourceNode = nodes[index];
            nodes = sourceNode.Elements("Node").ToArray();
        }
        if (sourceNode is null || (string?)sourceNode.Attribute("Type") != "MapLayer" ||
            (string?)sourceNode.Attribute("MapType") != source.MapLayerType ||
            (string?)sourceNode.Attribute("Name") != source.NodeName ||
            !string.Equals((string?)sourceNode.Attribute("Filename"), source.Filename, StringComparison.Ordinal) ||
            !string.Equals((string?)sourceNode.Attribute("FilenameRelative"), source.FilenameRelative, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        string nodeID = $"vis-map-{source.MapIndex}-node-{string.Join('.', source.NodePath)}";
        if (!string.Equals(source.Id, nodeID, StringComparison.Ordinal))
            throw new ArgumentException("The raster map node identity is invalid.");
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string resolved = ResolveMapRasterPath(projectDirectory,
            (string?)sourceNode.Attribute("FilenameRelative"), (string?)sourceNode.Attribute("Filename"));
        if (!SamePath(resolved, source.SourcePath))
            throw new ArgumentException("The raster source path does not match the current visualization node.");
        string revision = MapRasterLayerSourceManifest.CaptureRevision(resolved);
        if (source.SourceRevision is not null &&
            !revision.Equals(source.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string actualType;
        using (var raster = new GDALRaster(resolved)) actualType = MapRasterLayerRendering.ResolveMapType(raster);
        if (!string.Equals(actualType, source.MapLayerType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        Projection? targetProjection = ReadProjectProjection(projectPath, projectXml, null);
        return new(projectPath, visualizationPath, visualizationRevision, resolved, revision, targetProjection);
    }

    private static Projection? ReadProjectProjection(string projectPath, string? projectXml, string? projectXmlSHA)
    {
        byte[] bytes;
        if (projectXml is null) bytes = File.ReadAllBytes(projectPath);
        else bytes = Encoding.UTF8.GetBytes(projectXml);
        if (bytes.LongLength > MaximumProjectXmlBytes)
            throw new NotSupportedException("The project XML is too large to read.");
        if (projectXmlSHA is not null &&
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(projectXmlSHA, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        XDocument project = ParseXml(bytes, MaximumProjectXmlBytes);
        if (project.Root?.Name != "Project") throw new InvalidDataException("The current project XML has an invalid Project root element.");
        XElement? element = project.Root.Element("Projection");
        if (element is null) return null;
        ProjectionDefinition definition = ProjectionDefinition.Deserialize(element);
        var result = definition.Evaluate(out Projection? projection, out _);
        if (!result.Result) throw new ArgumentException("The current project projection could not be evaluated.");
        return projection;
    }

    private static RasterMapViewExtent PreviewViewport(Geospatial.Vectors.Extent extent)
    {
        if (!extent.IsFiniteAndValid()) throw new NotSupportedException("The raster has no finite map extent.");
        double scale = Math.Min(512d / Math.Max(extent.Width, extent.Height), 1d);
        int width = Math.Clamp((int)Math.Round(extent.Width * scale), 1, 512);
        int height = Math.Clamp((int)Math.Round(extent.Height * scale), 1, 512);
        return new(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY, width, height);
    }

    private static RasterMapViewExtent FullExtentViewport(Geospatial.Vectors.Extent extent)
    {
        if (!extent.IsFiniteAndValid() || !double.IsFinite(extent.Width) || !double.IsFinite(extent.Height))
            throw new NotSupportedException("The raster has no finite map extent.");
        double area = extent.Width * extent.Height;
        double pixelScale = area > MaximumRenderPixels
            ? Math.Sqrt(MaximumRenderPixels / area) : 1d;
        double sideScale = MaximumRenderSide / Math.Max(extent.Width, extent.Height);
        double scale = Math.Min(Math.Min(pixelScale, sideScale), 1d);
        int width = Math.Clamp((int)Math.Round(extent.Width * scale), 1, MaximumRenderSide);
        int height = Math.Clamp((int)Math.Round(extent.Height * scale), 1, MaximumRenderSide);
        while ((long)width * height > MaximumRenderPixels)
        {
            if (width >= height && width > 1) width--;
            else if (height > 1) height--;
            else break;
        }
        return new(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY, width, height);
    }

    private static RasterMapViewExtent ValidateViewport(RasterMapViewExtent? extent)
    {
        if (extent is not RasterMapViewExtent value || !double.IsFinite(value.MinX) ||
            !double.IsFinite(value.MinY) || !double.IsFinite(value.MaxX) || !double.IsFinite(value.MaxY) ||
            value.MaxX <= value.MinX || value.MaxY <= value.MinY ||
            value.PixelWidth is < 1 or > 4096 || value.PixelHeight is < 1 or > 4096)
            throw new ArgumentException("The map raster viewport is invalid.");
        return value;
    }

    private static string ResolveMapRasterPath(string projectDirectory, string? relative, string? absolute)
    {
        foreach (string? value in new[] { relative, absolute })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            string normalized = value.Replace('\\', Path.DirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.IsPathFullyQualified(normalized)
                ? normalized : Path.Combine(projectDirectory, normalized));
            if (File.Exists(candidate)) return MapRasterLayerSourceManifest.CanonicalPath(candidate);
        }
        string missing = string.IsNullOrWhiteSpace(relative) ? absolute ?? "" : Path.GetFullPath(
            Path.IsPathFullyQualified(relative) ? relative : Path.Combine(projectDirectory, relative));
        throw new FileNotFoundException("The raster referenced by the visualization file was not found.", missing);
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(MapRasterLayerSourceManifest.CanonicalPath(first),
            MapRasterLayerSourceManifest.CanonicalPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static XDocument ParseXml(byte[] bytes, long maximumBytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes,
            IgnoreWhitespace = false
        };
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private sealed record ReadMapLayerContext(string ProjectPath, string VisualizationPath,
        string VisualizationRevision, string SourcePath, string SourceRevision, Projection? TargetProjection);
}

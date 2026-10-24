using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Transformers;
using Geospatial.Vectors;
using OSGeo.GDAL;

namespace Ras.Backend;

/// <summary>Reads raster subdatasets owned by an exact OpenFileGDB directory and saved map row.</summary>
public static class GdbRasterSourceService
{
    private const long MaximumVisualizationBytes = 32L * 1024 * 1024;
    private const int MaximumSubdatasets = 10_000;
    // GDALOpenEx flags from gdal.h. Opening with both vector and raster capability lets
    // OpenFileGDB expose raster subdatasets even when its root has no raster bands.
    private const uint GdalOfRaster = 0x02;
    private const uint GdalOfVector = 0x04;
    private const uint GdalOfShared = 0x20;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record ResolvedNode(
        MapSourceMapCapture Map,
        CatalogTreeNodeLocator ParentNode,
        CatalogTreeNodeLocator Node,
        GdbMapSourceIdentity Source,
        XDocument Visualization,
        string ConnectionString);

    /// <summary>Enumerates actual GDAL raster subdatasets and binds them to the shared GDB manifest.</summary>
    public static GdbRasterCatalogResult CatalogSubdatasets(
        string sourcePath,
        GdbMapSourceIdentity? expectedSource = null)
    {
        GdbMapSourceIdentity source = CaptureExpectedSource(sourcePath, expectedSource);
        GdbRasterSubdatasetDescriptor[] descriptors;
        KeyValuePair<string, string>[] subdatasets = ReadSubdatasets(source.SourcePath);
        if (subdatasets.Length > MaximumSubdatasets)
            throw new NotSupportedException("The file geodatabase contains more raster subdatasets than the native map supports.");
        descriptors = subdatasets.Select(ReadDescriptor).ToArray();

        GdbMapSourceService.VerifySource(source);
        if (expectedSource is not null && !SameSource(expectedSource, source))
            throw new SourceFileChangedException();
        return new(source, descriptors);
    }

    /// <summary>
    /// Opens the exact raster subdataset and invokes a synchronous, read-only callback while the
    /// GDAL dataset and raster reader are alive. It verifies map, node, and full GDB identity on exit.
    /// </summary>
    public static T WithOpenedRasterSource<T>(
        GdbRasterSourceLoadRequest request,
        Func<GdbRasterOpenedSource, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return WithOpenedDataset(request, opened =>
        {
            if (opened.Subdataset.MapType != MapRasterLayerRendering.SingleBandMapType)
                throw new NotSupportedException("Continuous raster symbology requires a single float or double band.");
            Type dataType = opened.Dataset.GetDataType();
            string connection = opened.Subdataset.ConnectionString;
            IDisposable? cleanup = null;
            try
            {
                IResample<float> raster;
                if (dataType.IsEquivalentTo(typeof(float)))
                {
                    raster = RasterReader.TryGetBestRasterizer<float>(connection,
                                 out _, out cleanup, "GDAL")
                             ?? throw new InvalidDataException("The selected GDB raster subdataset could not be read.");
                }
                else
                {
                    IResample<double> doubleRaster = RasterReader.TryGetBestRasterizer<double>(connection,
                        out _, out cleanup, "GDAL")
                        ?? throw new InvalidDataException("The selected GDB raster subdataset could not be read.");
                    raster = doubleRaster.ConvertType(ConvertTypeResamplers.DoubleToFloatConverter,
                        (float?)doubleRaster.NoDataValue);
                }
                double? noData = raster.NoDataValue is float value && float.IsFinite(value) ? value : null;
                return read(new GdbRasterOpenedSource(opened.Capture, opened.Subdataset, opened.Dataset,
                    raster, opened.BaseDefinition, noData, NonEmptyWkt(opened.Dataset.GetProjection()),
                    opened.VisualizationSnapshot));
            }
            finally { cleanup?.Dispose(); }
        });
    }

    /// <summary>
    /// Opens an exact saved child for any supported raster map type. The entire source,
    /// project, visualization and node capture are revalidated after the callback returns.
    /// </summary>
    public static T WithOpenedDataset<T>(GdbRasterSourceLoadRequest request,
        Func<GdbRasterOpenedDataset, T> read)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Node);
        ArgumentNullException.ThrowIfNull(read);
        ResolvedNode resolved = ResolvePersistedNode(request.Map, request.Node, request.ExpectedSource);
        GdbRasterSubdatasetDescriptor descriptor = FindDescriptor(resolved.Source.SourcePath,
            resolved.ConnectionString);
        if (!descriptor.CanRender)
            throw new NotSupportedException(descriptor.Diagnostic ?? "The selected GDB raster layout is unsupported.");
        if (!string.Equals(descriptor.MapType, resolved.Node.MapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        using var dataset = new GDALRaster(resolved.ConnectionString);
        ValidateRasterDataset(dataset, descriptor);
        var opened = new GdbRasterOpenedDataset(
            new(resolved.Map, resolved.ParentNode, resolved.Node, resolved.Source), descriptor, dataset,
            dataset.GetPyramidDefinition().BasePyramidLevel, new XDocument(resolved.Visualization));
        T result = read(opened);
        VerifyCapture(request.Map, opened.Capture);
        return result;
    }

    /// <summary>Requires the outer render identity to describe the exact saved GDB child.</summary>
    internal static MapTreeRequest ValidateRasterIdentity(RasterMapSourceIdentity source, string? projectXml)
    {
        GdbRasterSymbologyIdentity identity = source.GdbSource
            ?? throw new ArgumentException("The GDB source owner is missing.");
        ArgumentNullException.ThrowIfNull(identity.Map);
        ArgumentNullException.ThrowIfNull(identity.Node);
        ArgumentNullException.ThrowIfNull(identity.Source);
        MapTreeRequest map = identity.Map;
        CatalogTreeNodeLocator node = identity.Node;
        if (!string.Equals(map.ProjectXml, projectXml, StringComparison.Ordinal) ||
            !MapRasterLayerRendering.SupportedTypes.Contains(source.MapLayerType, StringComparer.Ordinal) ||
            source.NodePath is null || node.NodePath is null || !source.NodePath.SequenceEqual(node.NodePath) ||
            source.Id != $"vis-map-{source.MapIndex}-node-{string.Join('.', source.NodePath)}" ||
            source.NodeName != node.NodeName || source.MapIndex != map.MapIndex ||
            source.MapName != map.MapName || source.MapType != map.MapType ||
            source.VisualizationRevision != map.ExpectedVisualizationRevision ||
            !MapIDsMatch(source.SourceMapID, map.MapID) ||
            !SamePath(source.ProjectPath, map.ProjectPath) ||
            !SamePath(source.VisualizationPath, Path.ChangeExtension(map.ProjectPath, ".vis")) ||
            !SamePath(source.SourcePath, identity.Source.SourcePath) ||
            source.SourceRevision is null ||
            !source.SourceRevision.Equals(identity.Source.Revision, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.Filename, node.Filename, StringComparison.Ordinal) ||
            !string.Equals(source.FilenameRelative, node.FilenameRelative, StringComparison.Ordinal) ||
            node.NodeType != "MapLayer" || node.MapType != source.MapLayerType ||
            string.IsNullOrWhiteSpace(node.Filename) || string.IsNullOrWhiteSpace(node.AssetID) ||
            !SamePath(node.AssetID, identity.Source.SourcePath))
            throw new SourceFileChangedException();
        return map;
    }

    private static bool MapIDsMatch(string? sourceMapID, string? mapID) =>
        Guid.TryParse(sourceMapID, out Guid source) && Guid.TryParse(mapID, out Guid map)
            ? source == map : string.Equals(sourceMapID, mapID, StringComparison.Ordinal);

    /// <summary>Revalidates an opened capture without reading raster pixels.</summary>
    public static void VerifyCapture(MapTreeRequest map, GdbRasterNodeCapture capture)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(capture.Map);
        ArgumentNullException.ThrowIfNull(capture.ParentNode);
        ArgumentNullException.ThrowIfNull(capture.Node);
        ArgumentNullException.ThrowIfNull(capture.Source);
        ResolvedNode current = ResolvePersistedNode(map, capture.Node, capture.Source);
        if (!SameMap(capture.Map, current.Map) ||
            !SameLocator(capture.ParentNode, current.ParentNode) ||
            !SameLocator(capture.Node, current.Node) ||
            !SameSource(capture.Source, current.Source))
            throw new SourceFileChangedException();

        if (!ReadSubdatasets(current.Source.SourcePath).Any(item =>
                string.Equals(item.Key, current.ConnectionString, StringComparison.Ordinal)))
            throw new SourceFileChangedException();
        GdbMapSourceService.VerifySource(capture.Source);
        MapSourceIdentity.VerifyMap(map, capture.Map);
    }

    private static ResolvedNode ResolvePersistedNode(
        MapTreeRequest mapRequest,
        CatalogTreeNodeLocator locator,
        GdbMapSourceIdentity? expectedSource)
    {
        ArgumentNullException.ThrowIfNull(mapRequest);
        ArgumentNullException.ThrowIfNull(locator);
        if (locator.NodeType != "MapLayer" || locator.MapType is null ||
            !MapRasterLayerRendering.SupportedTypes.Contains(locator.MapType, StringComparer.Ordinal) ||
            locator.NodePath is not { Length: >= 2 } path || path.Any(index => index < 0) ||
            string.IsNullOrWhiteSpace(locator.RootName) || string.IsNullOrWhiteSpace(locator.NodeName) ||
            string.IsNullOrWhiteSpace(locator.Filename) || string.IsNullOrWhiteSpace(locator.AssetID) ||
            HasUnsupportedOwner(locator))
            throw new SourceFileChangedException();

        MapSourceMapCapture map = MapSourceIdentity.CaptureMap(mapRequest);
        XDocument visualization = ReadVisualization(map);
        XElement savedMap = ResolveMap(visualization, map);
        XElement serializedNode = ResolveNode(savedMap, path, locator.RootName);
        XElement? serializedParent = serializedNode.Parent;
        int[] parentPath = path[..^1];
        if (serializedParent?.Name != "Node" || (string?)serializedParent.Attribute("Type") != "GdbDataset")
            throw new SourceFileChangedException();

        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(mapRequest.ProjectPath))!;
        string? filename = (string?)serializedParent.Attribute("Filename");
        string? filenameRelative = (string?)serializedParent.Attribute("FilenameRelative");
        string sourcePath = ResolveGdbPath(projectDirectory, filenameRelative, filename);
        GdbMapSourceIdentity source = CaptureExpectedSource(sourcePath, expectedSource);
        string canonicalSource = SourceFilePaths.Canonical(source.SourcePath);

        string parentName = (string?)serializedParent.Attribute("Name") ?? "";
        var parentLocator = new CatalogTreeNodeLocator(
            locator.RootName,
            "GdbDataset",
            parentName,
            parentPath,
            canonicalSource,
            "GdbDataset",
            filename,
            filenameRelative);

        string? parentFilename = (string?)serializedParent.Attribute("Filename");
        string? resolvedParentFilename = string.IsNullOrWhiteSpace(parentFilename)
            ? null : ResolvePath(projectDirectory, parentFilename);
        if (!string.Equals((string?)serializedNode.Attribute("Type"), "MapLayer", StringComparison.Ordinal) ||
            !string.Equals((string?)serializedNode.Attribute("Name"), locator.NodeName, StringComparison.Ordinal) ||
            !string.Equals((string?)serializedNode.Attribute("MapType"), locator.MapType, StringComparison.Ordinal) ||
            !string.Equals((string?)serializedNode.Attribute("Filename"), locator.Filename, StringComparison.Ordinal) ||
            !string.Equals((string?)serializedNode.Attribute("FilenameRelative"), locator.FilenameRelative, StringComparison.Ordinal) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(locator.AssetID), canonicalSource) ||
            !string.Equals((string?)serializedNode.Attribute("FilenameRelative"), filenameRelative, StringComparison.Ordinal) ||
            resolvedParentFilename is null ||
            !PathComparer.Equals(SourceFilePaths.Canonical(resolvedParentFilename), canonicalSource))
            throw new SourceFileChangedException();

        MapSourceIdentity.VerifyMap(mapRequest, map);
        GdbMapSourceService.VerifySource(source);
        if (!ReadSubdatasets(canonicalSource).Any(item =>
                string.Equals(item.Key, locator.Filename, StringComparison.Ordinal)))
            throw new SourceFileChangedException();

        return new(map, parentLocator, locator, source, visualization, locator.Filename);
    }

    private static GdbMapSourceIdentity CaptureExpectedSource(string sourcePath, GdbMapSourceIdentity? expected)
    {
        GdbMapSourceIdentity current = GdbMapSourceService.CaptureSource(sourcePath);
        if (expected is not null && !SameSource(expected, current))
            throw new SourceFileChangedException();
        return current;
    }

    private static GdbRasterSubdatasetDescriptor FindDescriptor(string sourcePath, string connectionString)
    {
        KeyValuePair<string, string>[] matches = ReadSubdatasets(sourcePath)
            .Where(pair => string.Equals(pair.Key, connectionString, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new SourceFileChangedException();
        return ReadDescriptor(matches[0]);
    }

    private static KeyValuePair<string, string>[] ReadSubdatasets(string sourcePath)
    {
        // Raster-only GDALRaster.Open fails for a valid vector-only .gdb. OpenFileGDB
        // publishes raster entries in the SUBDATASETS metadata domain of the root
        // vector-capable dataset, so open both capability classes and treat an empty
        // metadata domain as a genuine empty raster catalog.
        using Dataset root = Gdal.OpenEx(sourcePath,
            GdalOfRaster | GdalOfVector | GdalOfShared, null, null, null)
            ?? throw new InvalidDataException("GDAL could not open the file geodatabase to inspect raster subdatasets.");
        string[] metadata = root.GetMetadata("SUBDATASETS") ?? [];
        if (metadata.Length == 0) return [];
        if (metadata.Length > MaximumSubdatasets * 2 || metadata.Length % 2 != 0)
            throw new InvalidDataException("GDAL returned an invalid or oversized GDB subdataset catalog.");

        var names = new Dictionary<int, string>();
        var descriptions = new Dictionary<int, string>();
        foreach (string item in metadata)
        {
            if (string.IsNullOrEmpty(item))
                throw new InvalidDataException("GDAL returned malformed GDB subdataset metadata.");
            int separator = item.IndexOf('=');
            if (separator <= 0 || separator == item.Length - 1)
                throw new InvalidDataException("GDAL returned malformed GDB subdataset metadata.");
            string key = item[..separator];
            string value = item[(separator + 1)..];
            const string prefix = "SUBDATASET_";
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("GDAL returned malformed GDB subdataset metadata.");
            int suffix = key.IndexOf('_', prefix.Length);
            if (suffix < 0 || !int.TryParse(key.AsSpan(prefix.Length, suffix - prefix.Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int ordinal) || ordinal <= 0)
                throw new InvalidDataException("GDAL returned malformed GDB subdataset metadata.");
            string kind = key[(suffix + 1)..];
            if (kind.Equals("NAME", StringComparison.Ordinal))
            {
                if (!names.TryAdd(ordinal, value))
                    throw new InvalidDataException("GDAL returned duplicate GDB subdataset names.");
            }
            else if (kind.Equals("DESC", StringComparison.Ordinal))
            {
                if (!descriptions.TryAdd(ordinal, value))
                    throw new InvalidDataException("GDAL returned duplicate GDB subdataset descriptions.");
            }
            else throw new InvalidDataException("GDAL returned an unknown GDB subdataset metadata field.");
        }

        if (names.Count == 0 || names.Keys.Any(ordinal => !descriptions.ContainsKey(ordinal)) ||
            descriptions.Keys.Any(ordinal => !names.ContainsKey(ordinal)) ||
            !names.Keys.Order().SequenceEqual(Enumerable.Range(1, names.Count)))
            throw new InvalidDataException("GDAL returned incomplete GDB subdataset metadata.");
        if (names.Count > MaximumSubdatasets)
            throw new NotSupportedException("The file geodatabase contains more raster subdatasets than the native map supports.");
        return names.OrderBy(pair => pair.Key)
            .Select(pair => new KeyValuePair<string, string>(pair.Value,
                descriptions[pair.Key]))
            .ToArray();
    }

    private static GdbRasterSubdatasetDescriptor ReadDescriptor(KeyValuePair<string, string> subdataset)
    {
        try
        {
            using var raster = new GDALRaster(subdataset.Key);
            Type? type = raster.GetDataType();
            int width = raster.PixelsWide;
            int height = raster.PixelsTall;
            int bandCount = raster.GetBandCount();
            string driver = raster.DriverShortName;
            string dataType = DescribeDataType(type);
            double? noData = raster.TryGetNoData<double>(1, out double value) && double.IsFinite(value)
                ? value : null;
            MapVectorSourceExtent? extent = ToFiniteExtent(raster.GetFullNativeExtents());
            string? projectionWKT = NonEmptyWkt(raster.GetProjection());
            string? mapType = null;
            string? diagnostic = null;
            try { mapType = MapRasterLayerRendering.ResolveMapType(raster); }
            catch (NotSupportedException error) { diagnostic = error.Message; }
            if (width <= 0 || height <= 0) diagnostic = "The GDB raster subdataset has invalid dimensions.";
            else if (extent is null) diagnostic = "The GDB raster subdataset has no finite map extent.";
            bool renderable = width > 0 && height > 0 && mapType is not null && extent is not null;
            return new(subdataset.Value, subdataset.Key, driver, width, height, bandCount,
                dataType, noData, extent, projectionWKT, renderable,
                renderable ? "available" : "unsupported", diagnostic, mapType);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and
                                           not AccessViolationException and not OperationCanceledException and
                                           not SourceFileChangedException)
        {
            return new(subdataset.Value, subdataset.Key, "", 0, 0, 0, "unknown", null,
                null, null, false, "failed", exception.Message);
        }
    }

    private static void ValidateRasterDataset(GDALRaster dataset, GdbRasterSubdatasetDescriptor descriptor)
    {
        if (dataset.PixelsWide != descriptor.Width || dataset.PixelsTall != descriptor.Height ||
            dataset.GetBandCount() != descriptor.BandCount ||
            !string.Equals(DescribeDataType(dataset.GetDataType()), descriptor.DataType, StringComparison.Ordinal) ||
            !string.Equals(dataset.DriverShortName, descriptor.DriverName, StringComparison.Ordinal) ||
            !string.Equals(MapRasterLayerRendering.ResolveMapType(dataset), descriptor.MapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static string DescribeDataType(Type? type) => type is null ? "unknown" :
        type.IsEquivalentTo(typeof(float)) ? "float32" :
        type.IsEquivalentTo(typeof(double)) ? "float64" : type.FullName ?? type.Name;

    private static string? NonEmptyWkt(Projection? projection)
    {
        string? wkt = projection?.GetWKT();
        return string.IsNullOrWhiteSpace(wkt) ? null : wkt;
    }

    private static MapVectorSourceExtent? ToFiniteExtent(Extent extent) =>
        double.IsFinite(extent.MinX) && double.IsFinite(extent.MinY) &&
        double.IsFinite(extent.MaxX) && double.IsFinite(extent.MaxY) &&
        extent.MaxX > extent.MinX && extent.MaxY > extent.MinY
            ? new(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY)
            : null;

    private static XDocument ReadVisualization(MapSourceMapCapture capture)
    {
        if (capture.VisualizationRevision is null || !File.Exists(capture.VisualizationPath))
            throw new SourceFileChangedException();
        var info = new FileInfo(capture.VisualizationPath);
        if (info.Length > MaximumVisualizationBytes)
            throw new NotSupportedException("The visualization file is too large to inspect the GDB raster source.");
        byte[] bytes = File.ReadAllBytes(capture.VisualizationPath);
        if (!string.Equals(Hash(bytes), capture.VisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumVisualizationBytes,
            IgnoreWhitespace = false
        };
        using var stream = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "Ras") throw new InvalidDataException("The visualization file has an invalid Ras root element.");
        return document;
    }

    private static XElement ResolveMap(XDocument visualization, MapSourceMapCapture capture)
    {
        XElement[] maps = visualization.Root?.Element("Maps")?.Elements("Map").ToArray() ?? [];
        if (capture.MapIndex is not int index || index < 0 || index >= maps.Length)
            throw new SourceFileChangedException();
        XElement map = maps[index];
        if (!string.Equals((string?)map.Attribute("Name"), capture.MapName, StringComparison.Ordinal) ||
            !string.Equals((string?)map.Attribute("Type"), capture.MapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        return map;
    }

    private static XElement ResolveNode(XElement map, int[] path, string rootName)
    {
        XElement[] roots = map.Element("Tree")?.Element("Nodes")?.Elements("Node").ToArray() ?? [];
        if (path.Length < 2 || path[0] < 0 || path[0] >= roots.Length)
            throw new SourceFileChangedException();
        XElement root = roots[path[0]];
        if (!string.Equals((string?)root.Attribute("Type"), "Root", StringComparison.Ordinal) ||
            !string.Equals((string?)root.Attribute("Name"), rootName, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        XElement current = root;
        for (int index = 1; index < path.Length; index++)
        {
            XElement[] children = current.Elements("Node").ToArray();
            if (path[index] < 0 || path[index] >= children.Length)
                throw new SourceFileChangedException();
            current = children[path[index]];
        }
        return current;
    }

    private static string ResolveGdbPath(string projectDirectory, string? relative, string? filename)
    {
        string? candidate = !string.IsNullOrWhiteSpace(relative)
            ? ResolvePath(projectDirectory, relative)
            : null;
        if (candidate is not null && Directory.Exists(candidate)) return SourceFilePaths.Canonical(candidate);
        candidate = !string.IsNullOrWhiteSpace(filename) ? ResolvePath(projectDirectory, filename) : null;
        if (candidate is null || !Directory.Exists(candidate)) throw new SourceFileChangedException();
        return SourceFilePaths.Canonical(candidate);
    }

    private static string? ResolvePath(string projectDirectory, string sourcePath)
    {
        string normalized = sourcePath.Replace('\\', Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(projectDirectory, normalized));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool HasUnsupportedOwner(CatalogTreeNodeLocator locator) =>
        locator.ParentGeometryAssetID is not null || locator.ParentGeometryNodePath is not null ||
        locator.ParentGeometryNodeName is not null || locator.AssociationAssetID is not null ||
        locator.AssociationKind is not null || locator.ParentPlanAssetID is not null ||
        locator.ParentPlanNodePath is not null || locator.ParentPlanNodeName is not null ||
        locator.ParentResultNodeType is not null || locator.ParentResultAssetID is not null ||
        locator.ParentResultNodePath is not null || locator.ParentResultNodeName is not null ||
        locator.ParentResultRevision is not null || locator.ParentBoundaryConditionAssetID is not null ||
        locator.ParentBoundaryConditionNodePath is not null || locator.ParentBoundaryConditionNodeName is not null;

    private static bool SameSource(GdbMapSourceIdentity first, GdbMapSourceIdentity second)
    {
        if (!IsWellFormedSource(first) || !IsWellFormedSource(second)) return false;
        return PathComparer.Equals(SourceFilePaths.Canonical(first.SourcePath), SourceFilePaths.Canonical(second.SourcePath)) &&
               string.Equals(first.Revision, second.Revision, StringComparison.OrdinalIgnoreCase) &&
               first.Files.Length == second.Files.Length &&
               first.Files.Zip(second.Files).All(pair =>
                   string.Equals(pair.First.RelativePath, pair.Second.RelativePath, StringComparison.Ordinal) &&
                   pair.First.Length == pair.Second.Length &&
                   string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWellFormedSource(GdbMapSourceIdentity? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.SourcePath) ||
            !Path.IsPathFullyQualified(source.SourcePath) || !IsSha256(source.Revision) ||
            source.Files is null || source.Files.Length == 0)
            return false;
        try
        {
            if (!PathComparer.Equals(SourceFilePaths.Canonical(source.SourcePath), source.SourcePath)) return false;
            string? previous = null;
            foreach (GdbMapSourceFileRevision? file in source.Files)
            {
                if (file is null || string.IsNullOrWhiteSpace(file.RelativePath) || file.Length < 0 ||
                    !IsSha256(file.Revision) || Path.IsPathRooted(file.RelativePath) ||
                    file.RelativePath.Split('/').Any(part => part is "" or "." or ".."))
                    return false;
                if (previous is not null && string.CompareOrdinal(previous, file.RelativePath) >= 0) return false;
                previous = file.RelativePath;
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSha256(string value) => value is { Length: 64 } &&
        value.All(Uri.IsHexDigit);

    private static bool SameMap(MapSourceMapCapture left, MapSourceMapCapture right) =>
        PathComparer.Equals(left.ProjectPath, right.ProjectPath) &&
        string.Equals(left.ProjectRevision, right.ProjectRevision, StringComparison.OrdinalIgnoreCase) &&
        SameRevision(left.ProjectXmlSHA, right.ProjectXmlSHA) &&
        PathComparer.Equals(left.VisualizationPath, right.VisualizationPath) &&
        SameRevision(left.VisualizationRevision, right.VisualizationRevision) &&
        string.Equals(left.MapID, right.MapID, StringComparison.Ordinal) &&
        left.MapIndex == right.MapIndex &&
        string.Equals(left.MapName, right.MapName, StringComparison.Ordinal) &&
        string.Equals(left.MapType, right.MapType, StringComparison.Ordinal) &&
        left.IsPersisted == right.IsPersisted;

    private static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        string.Equals(left.RootName, right.RootName, StringComparison.Ordinal) &&
        string.Equals(left.NodeType, right.NodeType, StringComparison.Ordinal) &&
        string.Equals(left.NodeName, right.NodeName, StringComparison.Ordinal) &&
        SamePath(left.AssetID, right.AssetID) &&
        string.Equals(left.MapType, right.MapType, StringComparison.Ordinal) &&
        string.Equals(left.Filename, right.Filename, StringComparison.Ordinal) &&
        string.Equals(left.FilenameRelative, right.FilenameRelative, StringComparison.Ordinal) &&
        SamePath(left.NodePath, right.NodePath);

    private static bool SamePath(string? left, string? right) =>
        left is null ? right is null : right is not null && PathComparer.Equals(left, right);

    private static bool SamePath(int[]? left, int[]? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static bool SameRevision(string? left, string? right) => left is null
        ? right is null
        : right is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.GDALAssist;
using Geospatial.GDALAssist.Vectors;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using OSGeo.OGR;
using Ras.Layers;
using OgrGeometry = OSGeo.OGR.Geometry;

namespace Ras.Backend;

public sealed record GdbMapRendererCapture(
    MapSourceMapCapture Map,
    CatalogTreeNodeLocator Locator,
    GdbMapSourceIdentity Source,
    GdbMapLayerDescriptor Layer,
    string SourcePath,
    string SourceRevision,
    string GdbLayerName,
    string GdbVectorType,
    IRenderLayer Renderer,
    int[]? SourceFeatureIndices);

/// <summary>Reads and renders exact, source-backed vector layers from an OpenFileGDB dataset.</summary>
public static class GdbMapSourceService
{
    private const int MaximumManifestFiles = 250_000;
    private const long MaximumSingleFileBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumTotalBytes = 64L * 1024 * 1024 * 1024;
    private const int MaximumFeatures = 5_000_000;
    private const int MaximumGeometryDepth = 64;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class BoundsAccumulator
    {
        private double _minX = double.PositiveInfinity;
        private double _minY = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity;
        private double _maxY = double.NegativeInfinity;

        public void Add(double x, double y)
        {
            _minX = Math.Min(_minX, x);
            _minY = Math.Min(_minY, y);
            _maxX = Math.Max(_maxX, x);
            _maxY = Math.Max(_maxY, y);
        }

        public MapVectorSourceExtent? ToExtent() => double.IsFinite(_minX) && double.IsFinite(_minY) &&
            double.IsFinite(_maxX) && double.IsFinite(_maxY)
                ? new(_minX, _minY, _maxX, _maxY) : null;
    }

    private sealed class ProjectScope(Project project) : IDisposable
    {
        public Project Project { get; } = project;
        public void Dispose()
        {
            foreach (CoreLayerBase layer in Project.Plans.Cast<CoreLayerBase>().Concat(Project.Geometries)
                         .Concat(Project.Terrains).Concat(Project.BoundaryConditions).Concat(Project.NValues)
                         .Concat(Project.ClassificationLayers))
                layer.Dispose();
            foreach (Result result in Project.Results) result.Dispose();
        }
    }

    public static GdbMapCatalogResponse Catalog(GdbMapCatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        GdbMapSourceIdentity source = CaptureSource(request.SourcePath);
        if (request.ExpectedSourceRevision is { Length: > 0 } expected &&
            !string.Equals(source.Revision, expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        GdbRasterCatalogResult rasters = GdbRasterSourceService.CatalogSubdatasets(source.SourcePath, source);
        // A valid raster-only OpenFileGDB can expose SUBDATASETS to GDAL while
        // OGR has no vector handle for its root. Confirm the raster catalog first
        // so only that explicit case is allowed to have an empty vector catalog.
        bool hasRasterDataset = rasters.Subdatasets.Any(raster =>
            (raster.Status is "available" or "unsupported") && raster.Width > 0 && raster.Height > 0 &&
            !string.IsNullOrWhiteSpace(raster.ConnectionString));
        GdbMapLayerDescriptor[] layers = ReadCatalog(source.SourcePath,
            allowMissingVectorHandle: hasRasterDataset);
        VerifySource(source);
        return new(source, layers, rasters.Subdatasets);
    }

    public static GdbMapSourceLoadResponse Load(GdbMapSourceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Node);
        if (request.Node.NodeType != "GdbVectorLayer" || request.Node.MapType != "GdbVectorLayer" ||
            string.IsNullOrWhiteSpace(request.ExpectedSourceRevision))
            throw new ArgumentException("The GDB map source request has an incomplete exact layer identity.");

        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedGdbNode(request.Map, request.Node);
        GdbMapSourceIdentity source = CaptureSource(location.SourcePath);
        EnsureRevision(source, request.ExpectedSourceRevision);
        GdbMapLayerDescriptor layer = FindLayer(source.SourcePath,
            location.GdbLayerName ?? throw new SourceFileChangedException());
        EnsureStoredVectorType(location.GdbVectorType, layer.VectorType);

        using ProjectScope projectScope = OpenProject(request.Map.ProjectPath, request.Map.ProjectXml,
            request.Map.CoreLayerContext);
        GdbMapSourceData data = ReadLayerData(source.SourcePath, layer.Name, projectScope.Project);
        MapSourceIdentity.VerifyPersistedGdbNode(request.Map, location);
        VerifySource(source);
        MapSourceMapCapture currentMap = MapSourceIdentity.CaptureMap(request.Map);
        if (!SameMapCapture(location.Map, currentMap)) throw new SourceFileChangedException();
        return new(location.Map, location.Locator, source, layer, data);
    }

    /// <summary>
    /// Revalidates one exact persisted GDB vector leaf without reading every
    /// feature geometry. Callers still bind the directory manifest, saved map
    /// and sublayer name/type; the .gdb package is never treated as one file.
    /// </summary>
    public static GdbMapLayerDescriptor VerifyVectorLayer(MapTreeRequest map,
        CatalogTreeNodeLocator node, string expectedSourceRevision)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(node);
        if (node.NodeType != "GdbVectorLayer" || node.MapType != "GdbVectorLayer" ||
            string.IsNullOrWhiteSpace(expectedSourceRevision))
            throw new ArgumentException("The GDB vector capture has an incomplete exact layer identity.");

        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedGdbNode(map, node);
        GdbMapSourceIdentity source = CaptureSource(location.SourcePath);
        EnsureRevision(source, expectedSourceRevision);
        GdbMapLayerDescriptor layer = FindLayer(source.SourcePath,
            location.GdbLayerName ?? throw new SourceFileChangedException());
        EnsureStoredVectorType(location.GdbVectorType, layer.VectorType);
        MapSourceIdentity.VerifyPersistedGdbNode(map, location);
        VerifySource(source);
        if (!SameMapCapture(location.Map, MapSourceIdentity.CaptureMap(map)))
            throw new SourceFileChangedException();
        return layer;
    }

    public static GdbMapRendererCapture CreateRendererCapture(MapTreeRequest map,
        CatalogTreeNodeLocator node, string expectedSourceRevision)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(node);
        if (node.NodeType != "GdbVectorLayer" || node.MapType != "GdbVectorLayer")
            throw new NotSupportedException("The selected source is not a persisted GDB vector layer.");
        MapSourceNodeLocation location = MapSourceIdentity.ResolvePersistedGdbNode(map, node);
        GdbMapSourceIdentity source = CaptureSource(location.SourcePath);
        EnsureRevision(source, expectedSourceRevision);
        GdbMapLayerDescriptor layer = FindLayer(source.SourcePath,
            location.GdbLayerName ?? throw new SourceFileChangedException());
        EnsureStoredVectorType(location.GdbVectorType, layer.VectorType);
        using ProjectScope projectScope = OpenProject(map.ProjectPath, map.ProjectXml,
            map.CoreLayerContext);
        GdbMapSourceData data = ReadLayerData(source.SourcePath, layer.Name, projectScope.Project);
        IRenderLayer renderer = CreateRenderer(layer.VectorType, data,
            out int[] sourceFeatureIndices);
        MapSourceIdentity.VerifyPersistedGdbNode(map, location);
        VerifySource(source);
        MapSourceMapCapture currentMap = MapSourceIdentity.CaptureMap(map);
        if (!SameMapCapture(location.Map, currentMap)) throw new SourceFileChangedException();
        return new(location.Map, location.Locator, source, layer, source.SourcePath, source.Revision,
            layer.Name, layer.VectorType, renderer, sourceFeatureIndices);
    }

    public static void VerifyRendererCapture(MapTreeRequest map, CatalogTreeNodeLocator node,
        GdbMapRendererCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        MapSourceIdentity.VerifyPersistedGdbNode(map, new(capture.Map, capture.Locator,
            capture.Source.SourcePath, capture.GdbVectorType, capture.GdbLayerName));
        if (!SameLocator(capture.Locator, node) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(node.AssetID ?? ""), capture.SourcePath))
            throw new SourceFileChangedException();
        VerifySource(capture.Source);
        GdbMapLayerDescriptor current = FindLayer(capture.SourcePath, capture.GdbLayerName);
        if (!SameLayer(current, capture.Layer) || !string.Equals(current.VectorType, capture.GdbVectorType,
                StringComparison.Ordinal))
            throw new SourceFileChangedException();
        MapSourceMapCapture mapCapture = MapSourceIdentity.CaptureMap(map);
        if (!SameMapCapture(capture.Map, mapCapture)) throw new SourceFileChangedException();
    }

    public static GdbMapSourceIdentity CaptureSource(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException("The selected file geodatabase must use an absolute path.", nameof(sourcePath));
        string canonical = SourceFilePaths.Canonical(Path.GetFullPath(sourcePath));
        if (!string.Equals(Path.GetExtension(canonical), ".gdb", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(canonical))
            throw new NotSupportedException("Choose a file geodatabase (.gdb) directory.");

        GdbMapSourceFileRevision[] files = CaptureManifest(canonical);
        string revision = ComputeManifestRevision(canonical, files);
        return new(canonical, revision, files);
    }

    public static void VerifySource(GdbMapSourceIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ValidateIdentityShape(expected);
        GdbMapSourceIdentity current = CaptureSource(expected.SourcePath);
        if (!SameSource(expected, current)) throw new SourceFileChangedException();
    }

    private static GdbMapSourceFileRevision[] CaptureManifest(string canonicalRoot)
    {
        var files = new List<GdbMapSourceFileRevision>();
        var pending = new Stack<string>();
        pending.Push(canonicalRoot);
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The GDB directory contains a symbolic link and cannot be captured as a stable source.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if (IsTransientLockFile(entry)) continue;
                string fullPath = SourceFilePaths.Canonical(Path.GetFullPath(entry));
                EnsureDescendant(canonicalRoot, fullPath);
                GdbMapSourceFileRevision file = CaptureManifestFile(canonicalRoot, fullPath);
                totalBytes = checked(totalBytes + file.Length);
                if (file.Length > MaximumSingleFileBytes || totalBytes > MaximumTotalBytes)
                    throw new InvalidDataException("The GDB source exceeds the supported manifest size.");
                files.Add(file);
                if (files.Count > MaximumManifestFiles)
                    throw new InvalidDataException("The GDB source contains more files than the supported manifest limit.");
            }
        }
        if (files.Count == 0) throw new InvalidDataException("The GDB source directory contains no stable data files.");
        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static GdbMapSourceFileRevision CaptureManifestFile(string root, string fullPath)
    {
        var info = new FileInfo(fullPath);
        info.Refresh();
        if (!info.Exists) throw new SourceFileChangedException();
        long length = info.Length;
        DateTime writeTime = info.LastWriteTimeUtc;
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        string revision = Convert.ToHexString(SHA256.HashData(stream));
        info.Refresh();
        if (!info.Exists || info.Length != length || info.LastWriteTimeUtc != writeTime)
            throw new SourceFileChangedException();
        string relative = Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return new(relative, length, revision);
    }

    private static bool IsTransientLockFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".lck", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith(".~lock.", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDescendant(string root, string path)
    {
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The GDB manifest contains a path outside its source directory.");
    }

    private static string ComputeManifestRevision(string canonicalRoot, GdbMapSourceFileRevision[] files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, canonicalRoot);
        foreach (GdbMapSourceFileRevision file in files)
        {
            Append(hash, file.RelativePath);
            Append(hash, file.Length.ToString(CultureInfo.InvariantCulture));
            Append(hash, file.Revision.ToUpperInvariant());
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static void ValidateIdentityShape(GdbMapSourceIdentity identity)
    {
        if (!Path.IsPathFullyQualified(identity.SourcePath) || string.IsNullOrWhiteSpace(identity.Revision) ||
            identity.Revision.Length != 64 || identity.Files is null || identity.Files.Length == 0 ||
            identity.Files.Length > MaximumManifestFiles)
            throw new SourceFileChangedException();
        string canonical = SourceFilePaths.Canonical(identity.SourcePath);
        if (!PathComparer.Equals(canonical, identity.SourcePath) ||
            !string.Equals(Path.GetExtension(canonical), ".gdb", StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string? previous = null;
        foreach (GdbMapSourceFileRevision file in identity.Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.RelativePath) || file.Length < 0 ||
                file.Revision is not { Length: 64 } revision || revision.Any(character => !Uri.IsHexDigit(character)) ||
                Path.IsPathRooted(file.RelativePath) || file.RelativePath.Split('/').Any(part => part is "" or "." or "..") ||
                (previous is not null && string.Compare(previous, file.RelativePath, StringComparison.Ordinal) >= 0))
                throw new SourceFileChangedException();
            previous = file.RelativePath;
        }
        if (!string.Equals(ComputeManifestRevision(identity.SourcePath, identity.Files), identity.Revision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static bool SameSource(GdbMapSourceIdentity first, GdbMapSourceIdentity second) =>
        PathComparer.Equals(first.SourcePath, second.SourcePath) &&
        string.Equals(first.Revision, second.Revision, StringComparison.OrdinalIgnoreCase) &&
        first.Files.Length == second.Files.Length && first.Files.Zip(second.Files).All(pair =>
            string.Equals(pair.First.RelativePath, pair.Second.RelativePath, StringComparison.Ordinal) &&
            pair.First.Length == pair.Second.Length &&
            string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.OrdinalIgnoreCase));

    private static GdbMapLayerDescriptor[] ReadCatalog(string sourcePath, bool allowMissingVectorHandle = false)
    {
        DataSource? openedDataset = Ogr.OpenShared(sourcePath, 0);
        if (openedDataset is null)
        {
            if (allowMissingVectorHandle) return [];
            throw new InvalidDataException("OGR could not open the file geodatabase as a vector dataset.");
        }

        using var dataset = openedDataset;
        using Driver? driver = dataset.GetDriver();
        if (driver is null)
            throw new InvalidDataException("OGR opened the GDB vector source without a dataset driver.");
        int count = dataset.GetLayerCount();
        if (count is < 0 or > 100_000) throw new InvalidDataException("The GDB dataset returned an invalid layer count.");
        var layers = new List<GdbMapLayerDescriptor>(count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            using Layer? layer = dataset.GetLayerByIndex(index);
            if (layer is null) throw new InvalidDataException("The GDB dataset returned a missing vector layer.");
            string name = layer.GetName();
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                throw new InvalidDataException("The GDB dataset returned an empty or duplicate vector layer name.");
            OSGeo.OGR.wkbGeometryType rawType = layer.GetGeomType();
            Geospatial.GDALAssist.VectorType vectorType = VectorTypeHelpers.Parse(rawType);
            if (vectorType is Geospatial.GDALAssist.VectorType.None or Geospatial.GDALAssist.VectorType.Unknown)
                continue;
            long featureCount = layer.GetFeatureCount(1);
            if (featureCount < 0) throw new InvalidDataException("The GDB layer returned an invalid feature count.");
            using OSGeo.OSR.SpatialReference? projection = layer.GetSpatialRef();
            string? projectionWkt = projection is null
                ? null
                : projection.ExportToWkt(out string projectionText, []) == 0
                    ? projectionText
                    : throw new InvalidDataException("The GDB layer projection could not be read.");
            layers.Add(new(name, rawType.ToString(), (int)rawType, vectorType.ToString(), featureCount,
                string.IsNullOrWhiteSpace(projectionWkt) ? null : projectionWkt));
        }
        return layers.ToArray();
    }

    private static GdbMapLayerDescriptor FindLayer(string sourcePath, string name)
    {
        GdbMapLayerDescriptor[] matches = ReadCatalog(sourcePath).Where(layer =>
            string.Equals(layer.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static void EnsureStoredVectorType(string? stored, string actual)
    {
        if (!Enum.TryParse(stored, ignoreCase: false, out Geospatial.GDALAssist.VectorType parsed) ||
            parsed is Geospatial.GDALAssist.VectorType.None or Geospatial.GDALAssist.VectorType.Unknown ||
            !string.Equals(parsed.ToString(), actual, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static GdbMapSourceData ReadLayerData(string sourcePath, string layerName, Project project)
    {
        using var dataset = new VectorDataset(sourcePath);
        VectorLayer[] matches = Enumerable.Range(0, dataset.LayerCount()).Select(dataset.GetLayer)
            .Where(layer => string.Equals(layer.GetName(), layerName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            foreach (VectorLayer match in matches) match.Dispose();
            throw new SourceFileChangedException();
        }
        using VectorLayer layer = matches[0];
        Geospatial.GDALAssist.VectorType vectorType = layer.GetGeometryType();
        if (vectorType is Geospatial.GDALAssist.VectorType.None or Geospatial.GDALAssist.VectorType.Unknown)
            throw new NotSupportedException("The selected GDB layer does not have a supported point, line, or polygon geometry type.");
        Projection? sourceProjection = layer.GetProjection();
        Projection? targetProjection = project.Projection;
        ReprojectionTransformer? transformer = null;
        string status;
        if (targetProjection is not null)
        {
            if (sourceProjection is null)
                throw new InvalidOperationException("The GDB vector layer has no source projection; it cannot be loaded into this projected map.");
            if (!sourceProjection.IsEqual(targetProjection))
                transformer = new ReprojectionTransformer(sourceProjection, targetProjection);
            status = "projected";
        }
        else if (IsValidLocalProject(project))
        {
            status = "localProjectSourceCoordinates";
        }
        else
        {
            throw new InvalidOperationException("The project has no valid target projection for this GDB vector layer.");
        }

        using (transformer)
        {
            string? sourceWkt = NonEmptyWkt(sourceProjection);
            string? targetWkt = NonEmptyWkt(targetProjection);
            if (status == "projected" && (sourceWkt is null || targetWkt is null))
                throw new InvalidDataException("The GDB source or project projection did not provide a usable WKT definition.");
            var sourceBounds = new BoundsAccumulator();
            var targetBounds = new BoundsAccumulator();
            FeatureDefn definition = layer.OgrLayer.GetLayerDefn();
            var features = new List<GdbMapSourceFeature>();
            layer.OgrLayer.ResetReading();
            int ordinal = 0;
            while (true)
            {
                using Feature feature = layer.OgrLayer.GetNextFeature();
                if (feature is null) break;
                if (ordinal >= MaximumFeatures)
                    throw new InvalidDataException("The GDB layer exceeds the supported map feature count.");
                using OgrGeometry? geometry = feature.GetGeometryRef();
                GdbMapSourceGeometry mapped = geometry is null
                    ? new("None", 0, [], [], [])
                    : ReadGeometry(geometry, transformer, sourceBounds, targetBounds, depth: 0);
                GdbMapSourceAttribute[] attributes = ReadAttributes(feature, definition);
                long fid = feature.GetFID();
                features.Add(new(ordinal, fid < 0 ? null : fid.ToString(CultureInfo.InvariantCulture), mapped, attributes));
                ordinal++;
            }
            layer.OgrLayer.ResetReading();
            return new(vectorType.ToString(), (int)layer.OgrLayer.GetGeomType(), features.Count,
                sourceBounds.ToExtent(), targetBounds.ToExtent(), status, sourceWkt, targetWkt,
                features.ToArray());
        }
    }

    private static GdbMapSourceGeometry ReadGeometry(OgrGeometry geometry, ReprojectionTransformer? transformer,
        BoundsAccumulator sourceBounds, BoundsAccumulator targetBounds, int depth)
    {
        if (depth > MaximumGeometryDepth) throw new InvalidDataException("The GDB feature geometry is nested too deeply.");
        OSGeo.OGR.wkbGeometryType rawType = geometry.GetGeometryType();
        Geospatial.GDALAssist.VectorType parsed = VectorTypeHelpers.Parse(rawType);
        string type = parsed.ToString();
        if (parsed is Geospatial.GDALAssist.VectorType.Point or Geospatial.GDALAssist.VectorType.Line)
        {
            var vertices = new GdbMapSourceVertex[geometry.GetPointCount()];
            var values = new double[4];
            bool hasZ = geometry.Is3D() != 0;
            bool hasM = geometry.IsMeasured() != 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                geometry.GetPointZM(index, values);
                vertices[index] = MakeVertex(values[0], values[1], hasZ ? values[2] : null,
                    hasM ? values[3] : null, transformer, sourceBounds, targetBounds);
            }
            return new(type, (int)rawType, vertices, [], []);
        }
        if (parsed is Geospatial.GDALAssist.VectorType.Polygon)
        {
            var rings = new GdbMapSourceRing[geometry.GetGeometryCount()];
            for (int index = 0; index < rings.Length; index++)
            {
                using OgrGeometry ring = geometry.GetGeometryRef(index);
                rings[index] = ReadRing(ring, index == 0 ? "exterior" : "interior", transformer,
                    sourceBounds, targetBounds);
            }
            return new(type, (int)rawType, [], rings, []);
        }

        int childCount = geometry.GetGeometryCount();
        var children = new GdbMapSourceGeometry[childCount];
        for (int index = 0; index < childCount; index++)
        {
            using OgrGeometry child = geometry.GetGeometryRef(index);
            children[index] = ReadGeometry(child, transformer, sourceBounds, targetBounds, depth + 1);
        }
        return new(type, (int)rawType, [], [], children);
    }

    private static GdbMapSourceRing ReadRing(OgrGeometry ring, string role, ReprojectionTransformer? transformer,
        BoundsAccumulator sourceBounds, BoundsAccumulator targetBounds)
    {
        OSGeo.OGR.wkbGeometryType rawType = ring.GetGeometryType();
        var values = new double[4];
        bool hasZ = ring.Is3D() != 0;
        bool hasM = ring.IsMeasured() != 0;
        var vertices = new GdbMapSourceVertex[ring.GetPointCount()];
        for (int index = 0; index < vertices.Length; index++)
        {
            ring.GetPointZM(index, values);
            vertices[index] = MakeVertex(values[0], values[1], hasZ ? values[2] : null,
                hasM ? values[3] : null, transformer, sourceBounds, targetBounds);
        }
        return new(role, vertices);
    }

    private static GdbMapSourceVertex MakeVertex(double x, double y, double? z, double? m,
        ReprojectionTransformer? transformer, BoundsAccumulator sourceBounds, BoundsAccumulator targetBounds)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || z is { } zValue && !double.IsFinite(zValue) ||
            m is { } mValue && !double.IsFinite(mValue))
            throw new InvalidDataException("The GDB source returned a non-finite coordinate.");
        sourceBounds.Add(x, y);
        double targetX = x;
        double targetY = y;
        if (transformer is not null)
        {
            Point[] input = [new Point(x, y)];
            Point[] output = [default];
            transformer.Transform(input.AsMemory(), output.AsMemory());
            targetX = output[0].X;
            targetY = output[0].Y;
            if (!double.IsFinite(targetX) || !double.IsFinite(targetY))
                throw new InvalidDataException("The GDB source projection produced a non-finite map coordinate.");
        }
        targetBounds.Add(targetX, targetY);
        return new(targetX, targetY, x, y, z, m);
    }

    internal static GdbMapSourceAttribute[] ReadAttributes(Feature feature, FeatureDefn definition)
    {
        int count = definition.GetFieldCount();
        if (count is < 0 or > 100_000)
            throw new InvalidDataException("The GDB layer returned an invalid field count.");
        var attributes = new GdbMapSourceAttribute[count];
        for (int index = 0; index < count; index++)
        {
            using FieldDefn field = definition.GetFieldDefn(index);
            string name = field.GetName();
            string ogrType = field.GetTypeName();
            string subtype = field.GetSubType().ToString();
            string type = subtype.Contains("Boolean", StringComparison.OrdinalIgnoreCase)
                ? "Boolean" : ogrType;
            bool isSet = feature.IsFieldSet(index);
            bool isNull = isSet && feature.IsFieldNull(index);
            if (!isSet || isNull)
            {
                attributes[index] = new(index, name, type, null, null, isSet, isNull);
                continue;
            }

            (string? value, bool? booleanValue, int? timeZoneFlag) = type.ToLowerInvariant() switch
            {
                "boolean" => (null, feature.GetFieldAsInteger(index) != 0, null),
                "integer" or "int" => (feature.GetFieldAsInteger(index).ToString(CultureInfo.InvariantCulture), null, null),
                "integer64" or "long" => (feature.GetFieldAsInteger64(index).ToString(CultureInfo.InvariantCulture), null, null),
                "real" or "float" or "double" => (feature.GetFieldAsDouble(index).ToString("R", CultureInfo.InvariantCulture), null, null),
                "string" or "widestring" => (feature.GetFieldAsString(index), null, null),
                "date" or "time" or "datetime" => ReadDateTimeAttribute(feature, index, type),
                "integerlist" => (SerializeIntegerList(feature, index), null, null),
                "integer64list" => (SerializeInteger64List(feature, index), null, null),
                "reallist" => (SerializeRealList(feature, index), null, null),
                "stringlist" or "widestringlist" => (JsonSerializer.Serialize(feature.GetFieldAsStringList(index) ?? []), null, null),
                "binary" => (SerializeBinary(feature, index), null, null),
                _ => throw new NotSupportedException($"The GDB field type '{ogrType}' is not supported for map attributes.")
            };
            if ((type is "String" or "WideString") && value is null ||
                type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) && booleanValue is null)
                throw new InvalidDataException("The GDB field reader returned no value for a set, non-null field.");
            attributes[index] = new(index, name, type, value, booleanValue, IsSet: true, IsNull: false,
                TimeZoneFlag: timeZoneFlag);
        }
        return attributes;
    }

    private static (string? Value, bool? BooleanValue, int? TimeZoneFlag) ReadDateTimeAttribute(
        Feature feature, int index, string type)
    {
        feature.GetFieldAsDateTime(index, out int year, out int month, out int day,
            out int hour, out int minute, out float second, out int timeZoneFlag);
        if (!float.IsFinite(second) || second < 0 || second >= 60)
            throw new InvalidDataException("The GDB field reader returned an invalid date or time value.");
        string seconds = second.ToString("00.#######", CultureInfo.InvariantCulture);
        string value = type.ToLowerInvariant() switch
        {
            "date" when year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day is >= 1 and <= 31 =>
                $"{year:D4}-{month:D2}-{day:D2}",
            "time" when hour is >= 0 and <= 23 && minute is >= 0 and <= 59 =>
                $"{hour:D2}:{minute:D2}:{seconds}",
            "datetime" when year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day is >= 1 and <= 31 &&
                             hour is >= 0 and <= 23 && minute is >= 0 and <= 59 =>
                $"{year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{seconds}",
            _ => throw new InvalidDataException("The GDB field reader returned an invalid date or time value.")
        };
        return (value, null, timeZoneFlag);
    }

    private static string SerializeIntegerList(Feature feature, int index)
    {
        int[] values = feature.GetFieldAsIntegerList(index, out int count) ?? [];
        return JsonSerializer.Serialize(CheckedList(values, count));
    }

    private static string SerializeInteger64List(Feature feature, int index)
    {
        string text = feature.GetFieldAsString(index);
        if (string.IsNullOrEmpty(text)) return "[]";
        string[] tokens = text.Split(',');
        var values = new long[tokens.Length];
        for (int item = 0; item < tokens.Length; item++)
        {
            if (!long.TryParse(tokens[item].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out values[item]))
                throw new InvalidDataException("The GDB field reader returned an invalid Integer64 list.");
        }
        return JsonSerializer.Serialize(values);
    }

    private static string SerializeRealList(Feature feature, int index)
    {
        double[] values = feature.GetFieldAsDoubleList(index, out int count) ?? [];
        double[] checkedValues = CheckedList(values, count);
        return JsonSerializer.Serialize(checkedValues, new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
    }

    private static string SerializeBinary(Feature feature, int index)
    {
        string hexadecimal = feature.GetFieldAsString(index);
        try { return Convert.ToBase64String(Convert.FromHexString(hexadecimal)); }
        catch (FormatException error)
        {
            throw new InvalidDataException("The GDB field reader returned invalid binary data.", error);
        }
    }

    private static T[] CheckedList<T>(T[] values, int count)
    {
        if (count < 0 || count > values.Length)
            throw new InvalidDataException("The GDB field reader returned an invalid list length.");
        return values.AsSpan(0, count).ToArray();
    }

    private static IRenderLayer CreateRenderer(string vectorType,
        GdbMapSourceData data, out int[] sourceFeatureIndices)
    {
        var indices = new List<int>();
        switch (Enum.Parse<Geospatial.GDALAssist.VectorType>(vectorType, ignoreCase: false))
        {
            case Geospatial.GDALAssist.VectorType.Point:
            case Geospatial.GDALAssist.VectorType.PointCollection:
            {
                var points = new PointCollection();
                foreach (GdbMapSourceFeature feature in data.Features)
                    foreach (GdbMapSourceVertex vertex in FlattenVertices(feature.Geometry))
                    {
                        points.Add(vertex.X, vertex.Y);
                        indices.Add(feature.SourceOrdinal);
                    }
                if (points.Count == 0) throw new InvalidDataException("The GDB point layer contains no drawable points.");
                sourceFeatureIndices = indices.ToArray();
                return new PointRenderLayer(points);
            }
            case Geospatial.GDALAssist.VectorType.Line:
            case Geospatial.GDALAssist.VectorType.LineCollection:
            {
                var lines = new PolylineCollection();
                foreach (GdbMapSourceFeature feature in data.Features)
                    foreach (GdbMapSourceGeometry line in FlattenGeometries(feature.Geometry)
                                 .Where(geometry => VectorTypeHelpers.Parse((OSGeo.OGR.wkbGeometryType)geometry.GeometryTypeCode)
                                     is Geospatial.GDALAssist.VectorType.Line))
                    {
                        if (line.Vertices.Length < 2) continue;
                        var polyline = new Polyline(line.Vertices.Length);
                        foreach (GdbMapSourceVertex vertex in line.Vertices) polyline.Add(vertex.X, vertex.Y);
                        lines.Add(polyline);
                        indices.Add(feature.SourceOrdinal);
                    }
                if (lines.Count == 0) throw new InvalidDataException("The GDB line layer contains no drawable paths.");
                sourceFeatureIndices = indices.ToArray();
                return new PolylineRenderLayer(lines);
            }
            case Geospatial.GDALAssist.VectorType.Polygon:
            case Geospatial.GDALAssist.VectorType.PolygonCollection:
            {
                var polygons = new ComplexPolygonCollection();
                foreach (GdbMapSourceFeature feature in data.Features)
                    foreach (GdbMapSourceGeometry polygonGeometry in FlattenGeometries(feature.Geometry)
                                 .Where(geometry => VectorTypeHelpers.Parse((OSGeo.OGR.wkbGeometryType)geometry.GeometryTypeCode)
                                     is Geospatial.GDALAssist.VectorType.Polygon))
                    {
                        Polygon[] rings = polygonGeometry.Rings.Select(ToPolygonRing).Where(ring => ring is not null).ToArray()!;
                        if (rings.Length == 0 || !(bool)ComplexPolygon.TryCreateComplexPolygon(rings, out ComplexPolygon? polygon))
                            continue;
                        polygons.Add(polygon);
                        indices.Add(feature.SourceOrdinal);
                    }
                if (polygons.Count == 0) throw new InvalidDataException("The GDB polygon layer contains no drawable polygons.");
                sourceFeatureIndices = indices.ToArray();
                return new ComplexPolygonRenderLayer(polygons);
            }
            default:
                throw new NotSupportedException("The selected GDB vector type has no retained map renderer.");
        }
    }

    private static Polygon? ToPolygonRing(GdbMapSourceRing ring)
    {
        int count = ring.Vertices.Length;
        if (count > 1 && ring.Vertices[0].X == ring.Vertices[^1].X && ring.Vertices[0].Y == ring.Vertices[^1].Y)
            count--;
        if (count < 3) return null;
        var polygon = new Polygon(count);
        for (int index = 0; index < count; index++) polygon.Add(ring.Vertices[index].X, ring.Vertices[index].Y);
        return polygon;
    }

    private static IEnumerable<GdbMapSourceGeometry> FlattenGeometries(GdbMapSourceGeometry geometry)
    {
        if (geometry.Vertices.Length > 0 || geometry.Rings.Length > 0) yield return geometry;
        foreach (GdbMapSourceGeometry child in geometry.Children)
            foreach (GdbMapSourceGeometry nested in FlattenGeometries(child)) yield return nested;
    }

    private static IEnumerable<GdbMapSourceVertex> FlattenVertices(GdbMapSourceGeometry geometry)
    {
        foreach (GdbMapSourceVertex vertex in geometry.Vertices) yield return vertex;
        foreach (GdbMapSourceRing ring in geometry.Rings)
            foreach (GdbMapSourceVertex vertex in ring.Vertices) yield return vertex;
        foreach (GdbMapSourceGeometry child in geometry.Children)
            foreach (GdbMapSourceVertex vertex in FlattenVertices(child)) yield return vertex;
    }

    private static ProjectScope OpenProject(string projectPath, string? projectXml,
        CoreLayerRequestContext? coreLayerContext) =>
        new(CoreLayerImportService.OpenProject(Path.GetFullPath(projectPath), projectXml, coreLayerContext));

    private static bool IsValidLocalProject(Project project) =>
        project.ProjectionDefinition.Type == ProjectionType.Local &&
        project.ProjectionDefinition.Evaluate(out Projection? evaluatedProjection, out _).Result &&
        evaluatedProjection is null;

    private static string? NonEmptyWkt(Projection? projection)
    {
        if (projection is null) return null;
        string wkt = projection.GetWKT();
        return string.IsNullOrWhiteSpace(wkt) ? null : wkt;
    }

    private static bool SameMapCapture(MapSourceMapCapture first, MapSourceMapCapture second) =>
        SourceFilePaths.SameSourceFile(first.ProjectPath, second.ProjectPath) &&
        string.Equals(first.ProjectRevision, second.ProjectRevision, StringComparison.OrdinalIgnoreCase) &&
        first.ProjectXmlSHA == second.ProjectXmlSHA &&
        SourceFilePaths.SameSourceFile(first.VisualizationPath, second.VisualizationPath) &&
        string.Equals(first.VisualizationRevision, second.VisualizationRevision, StringComparison.OrdinalIgnoreCase) &&
        first.MapID == second.MapID && first.MapIndex == second.MapIndex && first.MapName == second.MapName &&
        first.MapType == second.MapType && first.IsPersisted == second.IsPersisted;

    private static bool SameLocator(CatalogTreeNodeLocator first, CatalogTreeNodeLocator second) =>
        first.RootName == second.RootName && first.NodeType == second.NodeType && first.NodeName == second.NodeName &&
        OptionalPathEquals(first.AssetID, second.AssetID) && first.MapType == second.MapType &&
        first.Filename == second.Filename && first.FilenameRelative == second.FilenameRelative &&
        (first.NodePath ?? []).SequenceEqual(second.NodePath ?? []);

    private static bool SameLayer(GdbMapLayerDescriptor first, GdbMapLayerDescriptor second) =>
        first.Name == second.Name && first.GeometryType == second.GeometryType &&
        first.GeometryTypeCode == second.GeometryTypeCode && first.VectorType == second.VectorType &&
        first.FeatureCount == second.FeatureCount && first.ProjectionWKT == second.ProjectionWKT;

    private static bool OptionalPathEquals(string? first, string? second) => first is null
        ? second is null : second is not null && SourceFilePaths.SameSourceFile(first, second);

    private static void EnsureRevision(GdbMapSourceIdentity source, string expectedRevision)
    {
        if (expectedRevision.Length != 64 || !string.Equals(source.Revision, expectedRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }
}

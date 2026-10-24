using System.Xml.Linq;
using Geospatial.GDALAssist;

namespace Ras.Backend;

public sealed record MapRasterImportRequest(MapDocumentRequest Map, string SourcePath,
    string SourceRevision, string ExpectedProjectRevision, string ExpectedSourceCanonicalPath);

public static partial class MapTreeService
{
    /// <summary>
    /// Persists the retained single-band raster map context after consuming the
    /// actual TIFF metadata. The project connection remains the caller's normal
    /// project draft; this transaction publishes only the exact map tree.
    /// </summary>
    public static MapDocumentMutationReceipt ImportRaster(MapRasterImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        MapDocumentRequest owner = request.Map;
        using var context = Open(owner.ProjectPath, owner.ProjectXml, owner.ProjectXmlSHA,
            owner.ExpectedVisualizationRevision, allowMissing: owner.MapIndex is null);
        if (!context.ProjectRevision.Equals(request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string path = ResolveRasterImportSource(context, request);
        using (GDALRaster raster = GDALRaster.TryOpen(path)
            ?? throw new InvalidDataException("The selected raster could not be opened."))
        {
            if (raster.GetBandCount() != 1 || raster.PixelsWide <= 0 || raster.PixelsTall <= 0)
                throw new NotSupportedException("Choose a nonempty single-band raster for this map layer.");
            _ = raster.GetProjection();
        }
        _ = ResolveRasterImportSource(context, request);

        XElement map;
        int index;
        if (owner.MapIndex is int savedIndex)
        {
            map = ResolveMap(context.Visualization, savedIndex,
                owner.MapName ?? throw new ArgumentException("The saved map name is missing."),
                owner.MapType ?? throw new ArgumentException("The saved map type is missing."));
            index = savedIndex;
        }
        else
        {
            XElement maps = EnsureMaps(context.Visualization.Root!);
            if (owner.Kind != "Project" || maps.Elements("Map").Any()) throw new SourceFileChangedException();
            map = new XElement("Map", new XAttribute("Name", NextMapName([])), new XAttribute("Type", "Project"));
            maps.Add(map);
            maps.SetAttributeValue("Maps", (string?)map.Attribute("Name"));
            index = 0;
        }

        string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
        string rootName = (string?)map.Attribute("Type") switch
        {
            "Project" => "Map Layers",
            "Buildable" or "Geometry" or "Plan" => "Layers",
            _ => throw new NotSupportedException("Raster layers can be added only to Project, Buildable, Geometry and Plan maps.")
        };
        XElement root = EnsureRootNode(map, rootName);
        if (root.DescendantsAndSelf().Any(node => (string?)node.Attribute("Type") == "MapLayer" &&
            (string?)node.Attribute("MapType") == "SingleBandRasterMapLayer" &&
            PathEquals(ResolveMapLayerPath(projectDirectory, (string?)node.Attribute("FilenameRelative"),
                (string?)node.Attribute("Filename")) ?? "", path)))
            throw new ArgumentException("This raster layer is already present in the selected map.");
        XElement parent = root;
        if (rootName == "Map Layers")
        {
            parent = root.Elements("Node").FirstOrDefault(node =>
                (string?)node.Attribute("Type") == "Folder" && (string?)node.Attribute("Name") == "Rasters")!;
            if (parent is null)
            {
                parent = new XElement("Node", new XAttribute("Type", "Folder"), new XAttribute("Name", "Rasters"));
                root.Add(parent);
            }
        }
        parent.Add(new XElement("Node", new XAttribute("Type", "MapLayer"),
            new XAttribute("Name", Path.GetFileNameWithoutExtension(path)),
            new XAttribute("Filename", path), new XAttribute("FilenameRelative", Path.GetRelativePath(projectDirectory, path)),
            new XAttribute("MapType", "SingleBandRasterMapLayer"), new XAttribute("IsChecked", true)));
        _ = ResolveRasterImportSource(context, request);
        string revision = Commit(context);
        return new(index, (string)map.Attribute("Name")!, (string)map.Attribute("Type")!,
            context.PreviousVisualizationRevision, revision);
    }

    private static string ResolveRasterMapSource(OperationContext context, string? sourcePath, string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath) ||
            string.IsNullOrWhiteSpace(sourceRevision))
            throw new ArgumentException("The raster source path and revision are required.");
        string path = CanonicalPath(sourcePath);
        if (!new[] { ".tif", ".tiff" }.Contains(Path.GetExtension(sourcePath), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Choose a TIFF raster source file.");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected raster file was not found.", path);
        string projectDirectory = CanonicalPath(Path.GetDirectoryName(context.ProjectAliasPath)!);
        if (!IsWithinDirectory(path, projectDirectory) && !IsExternalConnectionFile(context, path))
            throw new ArgumentException("The raster source is outside the current project connections.");
        if (!RevisionFile(path).Equals(sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return path;
    }

    private static string ResolveRasterImportSource(OperationContext context, MapRasterImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Path.IsPathFullyQualified(request.SourcePath) ||
            string.IsNullOrWhiteSpace(request.SourceRevision))
            throw new ArgumentException("The selected raster path and revision are required.");
        string path = Path.GetFullPath(request.SourcePath);
        string canonical = CanonicalPath(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!new[] { ".tif", ".tiff" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Choose a TIFF raster source file.");
        if (string.IsNullOrWhiteSpace(request.ExpectedSourceCanonicalPath) ||
            !canonical.Equals(request.ExpectedSourceCanonicalPath, comparison)) throw new SourceFileChangedException();
        if (!File.Exists(path)) throw new FileNotFoundException("The selected raster file was not found.", path);
        string projectDirectory = CanonicalPath(Path.GetDirectoryName(context.ProjectAliasPath)!);
        if (!IsWithinDirectory(canonical, projectDirectory) && !IsExternalConnectionFile(context, canonical))
            throw new ArgumentException("The raster source is outside the current project connections.");
        if (!RevisionFile(path).Equals(request.SourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return path;
    }
}

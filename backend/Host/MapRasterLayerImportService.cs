using System.Xml.Linq;
using Geospatial.GDALAssist;

namespace Ras.Backend;

/// <summary>Persists the exact source GDAL renderer context in the selected `.vis` map.</summary>
public static partial class MapTreeService
{
    public static MapDocumentMutationReceipt ImportRasterLayer(MapRasterLayerImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        if (!MapRasterLayerRendering.SupportedTypes.Contains(request.MapLayerType, StringComparer.Ordinal))
            throw new NotSupportedException("The selected GDAL raster map type is not supported.");

        MapDocumentRequest owner = request.Map;
        using OperationContext context = Open(owner.ProjectPath, owner.ProjectXml, owner.ProjectXmlSHA,
            owner.ExpectedVisualizationRevision, allowMissing: owner.MapIndex is null);
        if (!context.ProjectRevision.Equals(request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        string path = ResolveRasterLayerImportSource(context, request);
        string type;
        using (var raster = GDALRaster.TryOpen(path)
            ?? throw new InvalidDataException("The selected file is not a readable GDAL raster."))
        {
            if (raster.PixelsWide <= 0 || raster.PixelsTall <= 0)
                throw new InvalidDataException("The selected GDAL raster has no cells.");
            type = MapRasterLayerRendering.ResolveMapType(raster);
        }
        if (!string.Equals(type, request.MapLayerType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        MapRasterLayerSourceManifest.RequireCurrent(path, request.SourceRevision);

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

        string rootName = (string?)map.Attribute("Type") switch
        {
            "Project" => "Map Layers",
            "Buildable" or "Geometry" or "Plan" => "Layers",
            _ => throw new NotSupportedException("GDAL raster layers can only be added to Project, Buildable, Geometry and Plan maps.")
        };
        XElement root = EnsureRootNode(map, rootName);
        string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
        if (root.DescendantsAndSelf().Any(node => (string?)node.Attribute("Type") == "MapLayer" &&
            MapRasterLayerRendering.SupportedTypes.Contains((string?)node.Attribute("MapType") ?? "", StringComparer.Ordinal) &&
            PathEquals(ResolveMapLayerPath(projectDirectory, (string?)node.Attribute("FilenameRelative"),
                (string?)node.Attribute("Filename")) ?? "", path)))
            throw new ArgumentException("This GDAL raster source is already present in the selected map.");

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
            new XAttribute("Filename", path),
            new XAttribute("FilenameRelative", Path.GetRelativePath(projectDirectory, path)),
            new XAttribute("MapType", request.MapLayerType),
            new XAttribute("IsChecked", true)));
        MapRasterLayerSourceManifest.RequireCurrent(path, request.SourceRevision);
        string revision = Commit(context);
        return new(index, (string)map.Attribute("Name")!, (string)map.Attribute("Type")!,
            context.PreviousVisualizationRevision, revision);
    }

    private static string ResolveRasterLayerImportSource(OperationContext context,
        MapRasterLayerImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !Path.IsPathFullyQualified(request.SourcePath) ||
            string.IsNullOrWhiteSpace(request.SourceRevision))
            throw new ArgumentException("The selected raster path and source revision are required.");
        string path = Path.GetFullPath(request.SourcePath);
        string canonical = MapRasterLayerSourceManifest.CanonicalPath(path);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.IsNullOrWhiteSpace(request.ExpectedSourceCanonicalPath) ||
            !canonical.Equals(MapRasterLayerSourceManifest.CanonicalPath(request.ExpectedSourceCanonicalPath), comparison))
            throw new SourceFileChangedException();
        if (!File.Exists(path)) throw new FileNotFoundException("The selected raster source was not found.", path);
        string projectDirectory = MapRasterLayerSourceManifest.CanonicalPath(
            Path.GetDirectoryName(context.ProjectAliasPath)!);
        if (!IsWithinDirectory(canonical, projectDirectory) && !IsExternalConnectionFile(context, canonical))
            throw new ArgumentException("The raster source is outside the current project connections.");
        MapRasterLayerSourceManifest.RequireCurrent(path, request.SourceRevision);
        return path;
    }
}

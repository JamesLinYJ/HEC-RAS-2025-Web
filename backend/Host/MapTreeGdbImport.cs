using System.Xml.Linq;

namespace Ras.Backend;

public static partial class MapTreeService
{
    /// <summary>Imports source-backed vector layers and raster subdatasets into the selected .vis map.</summary>
    public static GdbMapImportResponse ImportGdbVector(GdbMapImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.Source);
        MapDocumentRequest owner = request.Map;
        using var context = Open(owner.ProjectPath, owner.ProjectXml, owner.ProjectXmlSHA,
            owner.ExpectedVisualizationRevision, allowMissing: owner.MapIndex is null);
        if (!string.Equals(context.ProjectRevision, request.ExpectedProjectRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        GdbMapSourceService.VerifySource(request.Source);
        GdbMapCatalogResponse catalog = GdbMapSourceService.Catalog(
            new(request.Source.SourcePath, request.Source.Revision));
        if (!SameGdbSource(request.Source, catalog.Source)) throw new SourceFileChangedException();
        GdbRasterSubdatasetDescriptor[] rasterSubdatasets = (catalog.RasterSubdatasets ?? [])
            .Where(raster => raster.CanRender && raster.Status == "available" &&
                raster.MapType is { } mapType &&
                MapRasterLayerRendering.SupportedTypes.Contains(mapType, StringComparer.Ordinal)).ToArray();
        if (catalog.Layers.Length == 0 && rasterSubdatasets.Length == 0)
            throw new NotSupportedException("The file geodatabase has no retained map-ready vector or raster subdatasets.");

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
            if (owner.Kind != "Project" || maps.Elements("Map").Any())
                throw new SourceFileChangedException();
            map = new XElement("Map", new XAttribute("Name", NextMapName([])),
                new XAttribute("Type", "Project"));
            maps.Add(map);
            maps.SetAttributeValue("Maps", (string?)map.Attribute("Name"));
            index = 0;
        }

        AddGdbVectorLayers(context, map, request.Source, catalog.Layers, rasterSubdatasets);
        GdbMapSourceService.VerifySource(request.Source);
        string revision = Commit(context);
        var receipt = new MapDocumentMutationReceipt(index, (string)map.Attribute("Name")!,
            (string)map.Attribute("Type")!, context.PreviousVisualizationRevision, revision);
        return new(receipt, catalog.Source, catalog.Layers, rasterSubdatasets);
    }

    private static void AddGdbVectorLayers(OperationContext context, XElement map,
        GdbMapSourceIdentity source, GdbMapLayerDescriptor[] layers,
        GdbRasterSubdatasetDescriptor[] rasterSubdatasets)
    {
        string mapType = (string?)map.Attribute("Type") ?? "";
        if (mapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("GDB vector layers can be added only to Project, Buildable, Geometry and Plan maps.");
        string projectDirectory = Path.GetDirectoryName(context.ProjectAliasPath)!;
        string rootName = mapType == "Project" ? "Map Layers" : "Layers";
        XElement root = EnsureRootNode(map, rootName);
        XElement vectors = root.Elements("Node").FirstOrDefault(node =>
            (string?)node.Attribute("Type") == "Folder" && (string?)node.Attribute("Name") == "Vectors")!;
        if (vectors is null)
        {
            vectors = new XElement("Node", new XAttribute("Type", "Folder"), new XAttribute("Name", "Vectors"));
            root.AddFirst(vectors);
        }

        bool duplicate = root.DescendantsAndSelf().Where(node =>
                (string?)node.Attribute("Type") is "GdbDataset" or "GdbVectorLayer")
            .Any(node =>
            {
                string? relative = (string?)node.Attribute("FilenameRelative");
                string? filename = (string?)node.Attribute("Filename");
                string? resolved = ResolveGdbMapLayerPath(projectDirectory, relative, filename);
                return resolved is not null && PathEquals(resolved, source.SourcePath);
            });
        if (duplicate)
            throw new ArgumentException("This file geodatabase is already present in the selected map.");

        string relative = Path.GetRelativePath(projectDirectory, source.SourcePath);
        string datasetName = Path.GetFileName(source.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dataset = new XElement("Node", new XAttribute("Type", "GdbDataset"),
            new XAttribute("Name", datasetName), new XAttribute("Filename", source.SourcePath),
            new XAttribute("FilenameRelative", relative), new XAttribute("IsChecked", true));
        foreach (GdbMapLayerDescriptor layer in layers)
        {
            if (!Enum.TryParse(layer.VectorType, ignoreCase: false, out Geospatial.GDALAssist.VectorType parsed) ||
                parsed is Geospatial.GDALAssist.VectorType.None or Geospatial.GDALAssist.VectorType.Unknown)
                throw new InvalidDataException("The GDB catalog returned a non-renderable vector layer.");
            dataset.Add(new XElement("Node", new XAttribute("Type", "GdbVectorLayer"),
                new XAttribute("Name", layer.Name), new XAttribute("MapType", "GdbVectorLayer"),
                new XAttribute("Filename", source.SourcePath), new XAttribute("FilenameRelative", relative),
                new XAttribute("GdbLayerName", layer.Name), new XAttribute("GdbVectorType", layer.VectorType),
                new XAttribute("IsChecked", true)));
        }
        foreach (GdbRasterSubdatasetDescriptor raster in rasterSubdatasets)
        {
            if (string.IsNullOrWhiteSpace(raster.Name) || string.IsNullOrWhiteSpace(raster.ConnectionString) ||
                raster.ConnectionString.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
                raster.MapType is not { } rasterMapType ||
                !MapRasterLayerRendering.SupportedTypes.Contains(rasterMapType, StringComparer.Ordinal))
                throw new InvalidDataException("GDB 栅格目录缺少完整的子数据集身份或受支持的 MapType。");
            dataset.Add(new XElement("Node", new XAttribute("Type", "MapLayer"),
                new XAttribute("Name", raster.Name),
                new XAttribute("MapType", rasterMapType),
                new XAttribute("Filename", raster.ConnectionString),
                new XAttribute("FilenameRelative", relative),
                new XAttribute("IsChecked", raster.CanRender)));
        }
        vectors.Add(dataset);
    }

    /// <summary>
    /// Confirms every child of a GDB dataset still belongs to its exact source directory before the
    /// parent can be removed. Display labels are intentionally independent from source layer names.
    /// </summary>
    private static void VerifyGdbDatasetChildren(XElement dataset, GdbMapSourceIdentity source,
        GdbMapCatalogResponse catalog)
    {
        string sourcePath = SourceFilePaths.Canonical(source.SourcePath);
        string? parentRelative = (string?)dataset.Attribute("FilenameRelative");
        if (string.IsNullOrWhiteSpace(parentRelative) ||
            !PathEquals((string?)dataset.Attribute("Filename") ?? "", sourcePath))
            throw new SourceFileChangedException();

        foreach (XElement child in dataset.Elements("Node"))
        {
            if (!string.Equals((string?)child.Attribute("FilenameRelative"), parentRelative,
                    StringComparison.Ordinal))
                throw new SourceFileChangedException();

            string? type = (string?)child.Attribute("Type");
            if (type == "GdbVectorLayer")
            {
                if ((string?)child.Attribute("MapType") != "GdbVectorLayer" ||
                    !PathEquals((string?)child.Attribute("Filename") ?? "", sourcePath))
                    throw new SourceFileChangedException();
                string? layerName = (string?)child.Attribute("GdbLayerName");
                string? vectorType = (string?)child.Attribute("GdbVectorType");
                GdbMapLayerDescriptor[] matches = catalog.Layers.Where(layer =>
                    string.Equals(layer.Name, layerName, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1 ||
                    !string.Equals(matches[0].VectorType, vectorType, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                continue;
            }

            if (type == "MapLayer" &&
                (string?)child.Attribute("MapType") is { } mapType &&
                MapRasterLayerRendering.SupportedTypes.Contains(mapType, StringComparer.Ordinal))
            {
                string? connectionString = (string?)child.Attribute("Filename");
                GdbRasterSubdatasetDescriptor[] matches = (catalog.RasterSubdatasets ?? []).Where(item =>
                    string.Equals(item.ConnectionString, connectionString, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1 ||
                    !string.Equals(matches[0].MapType, mapType, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                continue;
            }

            throw new SourceFileChangedException();
        }
    }

    /// <summary>Builds one exact raster child backed by a GDAL subdataset string under its GDB parent.</summary>
    private static CatalogTreeNode BuildGdbRasterMapNode(XElement element, string id, string rootName,
        int[] path, string projectDirectory, MapTreeGdbCatalogs gdbRasterCatalogs)
    {
        string name = (string?)element.Attribute("Name") ?? "";
        string mapType = (string?)element.Attribute("MapType") ?? "";
        string? connectionString = (string?)element.Attribute("Filename");
        string? relative = (string?)element.Attribute("FilenameRelative");
        XElement? parent = element.Parent;
        string? parentFilename = (string?)parent?.Attribute("Filename");
        string? parentRelative = (string?)parent?.Attribute("FilenameRelative");
        string? resolved = ResolveGdbMapLayerPath(projectDirectory, parentRelative, parentFilename);
        string? assetID = resolved is null ? null : CanonicalPath(resolved);
        GdbRasterSubdatasetDescriptor? descriptor = null;
        string? sourceRevision = null;
        string status = "unavailable";
        string? diagnostic = null;
        bool supportedMapType = MapRasterLayerRendering.SupportedTypes.Contains(mapType, StringComparer.Ordinal);
        if (parent is null || (string?)parent.Attribute("Type") != "GdbDataset" ||
            string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(connectionString) ||
            !string.Equals(relative, parentRelative, StringComparison.Ordinal) || assetID is null)
        {
            diagnostic = "The GDB raster child is missing its exact parent directory or connection identity.";
        }
        else if (!supportedMapType)
        {
            status = "sourceChanged";
            diagnostic = "已保存的 GDB 栅格图层 MapType 缺失或不受支持。";
        }
        else
        {
            try
            {
                GdbRasterCatalogResult catalog = gdbRasterCatalogs.Get(assetID);
                sourceRevision = catalog.Source.Revision;
                GdbRasterSubdatasetDescriptor[] matches = catalog.Subdatasets.Where(item =>
                    string.Equals(item.ConnectionString, connectionString, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1)
                {
                    status = "sourceChanged";
                    diagnostic = "已保存的 GDB 栅格图层不再对应唯一的 GDAL 子数据集。";
                }
                else if (matches[0].MapType is null && matches[0].Status == "failed")
                {
                    descriptor = matches[0];
                    status = descriptor.Status;
                    diagnostic = descriptor.Diagnostic;
                }
                else if (!string.Equals(matches[0].MapType, mapType, StringComparison.Ordinal))
                {
                    status = "sourceChanged";
                    diagnostic = "当前 GDB 栅格图层类型与已保存的 MapType 不一致。";
                }
                else
                {
                    descriptor = matches[0];
                    status = descriptor.Status;
                    diagnostic = descriptor.Diagnostic;
                }
            }
            catch (SourceFileChangedException)
            {
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and
                                          not AccessViolationException and not OperationCanceledException)
            {
                status = "readFailed";
                diagnostic = error.Message;
            }
        }

        var properties = element.Attributes().Where(attribute => attribute.Name != "Type" && attribute.Name != "Name")
            .Select(attribute => new CatalogTreeProperty("Source/" + attribute.Name.LocalName,
                attribute.Name.LocalName, attribute.Value)).ToList();
        if (descriptor is not null)
        {
            properties.Add(new("Source/GdbRasterDriver", "GDB raster driver", descriptor.DriverName));
            properties.Add(new("Source/GdbRasterSize", "GDB raster dimensions",
                $"{descriptor.Width} × {descriptor.Height}"));
            properties.Add(new("Source/GdbRasterBandCount", "GDB raster bands",
                descriptor.BandCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            properties.Add(new("Source/GdbRasterDataType", "GDB raster data type", descriptor.DataType));
        }

        bool usable = descriptor?.CanRender == true && status == "available";
        string sourceClass = mapType switch
        {
            MapRasterLayerRendering.SingleBandMapType => "RasterMapLayerVisualContext",
            MapRasterLayerRendering.ImageMapType => "ImageMapLayerVisualContext",
            MapRasterLayerRendering.DiscreteMapType => "DiscreteRasterMapLayerVisualContext",
            _ => "UnsupportedMapLayerNode"
        };
        return new CatalogTreeNode(id, sourceClass, name,
            AssetID: assetID, Properties: properties.ToArray(),
            Checked: usable ? (ReadBool(element, "IsChecked") ? "checked" : "unchecked") : null,
            IsPinned: ReadBool(element, "IsPinned"), CanMove: false,
            Locator: new CatalogTreeNodeLocator(rootName, "MapLayer", name, path, assetID,
                MapType: mapType, Filename: connectionString,
                FilenameRelative: relative),
            DiagnosticStatus: status,
            DiagnosticSourceIdentity: assetID,
            DiagnosticSourceRevision: sourceRevision,
            DiagnosticReadError: diagnostic);
    }

    private static void VerifyGdbRasterTreeNode(MapTreeRequest mapRequest,
        CatalogTreeNodeLocator locator, XElement node, XElement parent,
        string? expectedSourcePath, string? expectedSourceRevision)
    {
        if (locator.NodeType != "MapLayer" ||
            locator.MapType is not { } mapType ||
            !MapRasterLayerRendering.SupportedTypes.Contains(mapType, StringComparer.Ordinal) ||
            locator.NodePath is not { Length: >= 2 } path || path.Any(index => index < 0) ||
            string.IsNullOrWhiteSpace(locator.NodeName) || string.IsNullOrWhiteSpace(locator.Filename) ||
            string.IsNullOrWhiteSpace(locator.FilenameRelative) || string.IsNullOrWhiteSpace(locator.AssetID) ||
            !ReferenceEquals(node.Parent, parent) || (string?)parent.Attribute("Type") != "GdbDataset" ||
            !string.Equals((string?)node.Attribute("Type"), "MapLayer", StringComparison.Ordinal) ||
            !string.Equals((string?)node.Attribute("Name"), locator.NodeName, StringComparison.Ordinal) ||
            !string.Equals((string?)node.Attribute("MapType"), mapType, StringComparison.Ordinal) ||
            !string.Equals((string?)node.Attribute("Filename"), locator.Filename, StringComparison.Ordinal) ||
            !string.Equals((string?)node.Attribute("FilenameRelative"), locator.FilenameRelative, StringComparison.Ordinal) ||
            !string.Equals((string?)parent.Attribute("FilenameRelative"), locator.FilenameRelative, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(mapRequest.ProjectPath))!;
        string gdbPath = ResolveGdbMapLayerPath(projectDirectory,
            (string?)parent.Attribute("FilenameRelative"), (string?)parent.Attribute("Filename"))
            ?? throw new SourceFileChangedException();
        string canonicalGdb = SourceFilePaths.Canonical(gdbPath);
        if (!PathEquals(canonicalGdb, locator.AssetID) ||
            !PathEquals(canonicalGdb, expectedSourcePath ?? "") ||
            !string.Equals(Path.GetExtension(canonicalGdb), ".gdb", StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        GdbRasterCatalogResult catalog = GdbRasterSourceService.CatalogSubdatasets(canonicalGdb);
        if (expectedSourceRevision is null ||
            !string.Equals(catalog.Source.Revision, expectedSourceRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        GdbRasterSubdatasetDescriptor[] matches = catalog.Subdatasets.Where(item =>
            string.Equals(item.ConnectionString, locator.Filename, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || !string.Equals(matches[0].MapType, mapType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        MapSourceMapCapture capture = MapSourceIdentity.CaptureMap(mapRequest);
        MapSourceIdentity.VerifyMap(mapRequest, capture);
    }

    private static bool SameGdbSource(GdbMapSourceIdentity first, GdbMapSourceIdentity second) =>
        PathEquals(first.SourcePath, second.SourcePath) &&
        string.Equals(first.Revision, second.Revision, StringComparison.OrdinalIgnoreCase) &&
        first.Files.Length == second.Files.Length && first.Files.Zip(second.Files).All(pair =>
            pair.First.RelativePath == pair.Second.RelativePath && pair.First.Length == pair.Second.Length &&
            string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.OrdinalIgnoreCase));
}

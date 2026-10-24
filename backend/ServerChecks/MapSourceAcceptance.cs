using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OSGeo.GDAL;
using OSGeo.OGR;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record MapSourceAcceptanceReport(
    string State,
    string WorkspaceId,
    string ProjectPath,
    string MapName,
    string VisualizationRevision,
    int GeoJsonPointFeatureCount,
    int GeoJsonPolygonFeatureCount,
    int ShapefileFeatureCount,
    int RasterWidth,
    int RasterHeight,
    double RasterMinX,
    double RasterMinY,
    double RasterMaxX,
    double RasterMaxY,
    bool GeoJsonSourceShaVerified,
    bool ShapefileSourceShaVerified,
    bool GeoTiffSourceShaVerified,
    bool VisualizationShaVerified,
    bool ReadOnlyOperationsPreservedVisBytes,
    bool StaleProjectXmlShaRejected,
    bool StaleVisualizationRevisionRejected,
    bool StaleVectorSourceShaRejected);

/// <summary>Exercises real map-source imports and reads through the loopback HTTP worker.</summary>
internal static class MapSourceAcceptance
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AcceptanceTimeout = TimeSpan.FromMinutes(3);
    private const string MapName = "地图来源验收";
    private const string GeoJsonFileName = "Map Source Features.geojson";
    private const string ShapefileFileName = "Map Source Lines.shp";
    private const string GeoTiffFileName = "Map Source Pixels.tif";
    private const string GeoJsonFeatureCollection = """
        {
          "type": "FeatureCollection",
          "features": [
            {"type":"Feature","id":"point-甲","properties":{"name":"测点甲","reading":12.5,"active":true},"geometry":{"type":"Point","coordinates":[0.5,0.5]}},
            {"type":"Feature","id":77,"properties":{"name":"带洞面","area":16,"active":false},"geometry":{"type":"Polygon","coordinates":[[[0,0],[4,0],[4,4],[0,4],[0,0]],[[1,1],[2,1],[2,2],[1,2],[1,1]]]}},
            {"type":"Feature","id":"multi-area","properties":{"name":"多面区","area":2.75},"geometry":{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,1],[0,0]],[[0.2,0.2],[0.4,0.2],[0.4,0.4],[0.2,0.4],[0.2,0.2]]],[[[3,3],[4,3],[4,4],[3,4],[3,3]]]]}},
            {"type":"Feature","id":"second-point","properties":{"name":"测点乙","reading":7},"geometry":{"type":"MultiPoint","coordinates":[[3.5,0.5],[3.75,0.75]]}}
          ]
        }
        """;

    public static async Task<MapSourceAcceptanceReport> RunAsync(Uri baseAddress, FixtureManifest fixture)
    {
        if (!baseAddress.IsLoopback || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Map source acceptance only connects to a loopback HTTP(S) URL.", nameof(baseAddress));
        ArgumentNullException.ThrowIfNull(fixture);
        string fixtureRoot = Path.GetFullPath(fixture.RootPath);
        if (!Directory.Exists(fixtureRoot) || !File.Exists(fixture.ProjectPath))
            throw new DirectoryNotFoundException("The real 4×4 map source fixture must already exist.");

        SourceFixture files = CreateSourceFixture(fixtureRoot);
        using var timeout = new CancellationTokenSource(AcceptanceTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The map source server did not report ready.");

        JsonElement upload = await UploadFixtureAsync(client, fixtureRoot, cancellation);
        string workspaceId = StringProperty(upload, "workspaceId");
        string uploadedRoot = Path.GetFullPath(StringProperty(upload, "rootPath"));
        JsonElement projectEntry = Property(upload, "projects").EnumerateArray().SingleOrDefault();
        if (projectEntry.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The uploaded map source workspace must contain exactly one .ras project.");
        string projectPath = Path.GetFullPath(StringProperty(projectEntry, "path"));
        string projectXml = string.Empty;

        string geoJsonPath = UploadedPath(uploadedRoot, fixtureRoot, files.GeoJsonPath);
        string shapefilePath = UploadedPath(uploadedRoot, fixtureRoot, files.ShapefilePath);
        string rasterPath = UploadedPath(uploadedRoot, fixtureRoot, files.GeoTiffPath);
        if (!File.Exists(geoJsonPath) || !File.Exists(shapefilePath) || !File.Exists(rasterPath))
            throw new FileNotFoundException("The workspace upload did not preserve all real vector and GeoTIFF source files.");

        OperationReceipt opened = await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation);
        JsonElement openedResult = opened.Result;
        string projectRevision = StringProperty(openedResult, "revision");
        projectXml = StringProperty(openedResult, "projectXml");
        string projectXmlSha = Sha256(Encoding.UTF8.GetBytes(projectXml));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (File.Exists(visualizationPath))
            throw new InvalidDataException("The map source fixture unexpectedly has a saved .vis before map creation.");

        string mapID = Guid.NewGuid().ToString("D");
        object createMapRequest = new
        {
            projectPath,
            projectXml,
            projectXmlSHA = projectXmlSha,
            expectedVisualizationRevision = (string?)null,
            kind = "Project",
            name = MapName,
        };
        JsonElement created = (await SubmitOperationAsync(client, "map.document.create", createMapRequest, cancellation)).Result;
        int mapIndex = IntProperty(created, "mapIndex");
        string savedMapName = StringProperty(created, "mapName");
        string mapType = StringProperty(created, "mapType");
        string visualizationRevision = StringProperty(created, "visualizationRevision");
        if (mapIndex != 0 || savedMapName != MapName || mapType != "Project")
            throw new InvalidDataException("Map creation returned a different saved map identity.");
        AssertDiskRevision(visualizationPath, visualizationRevision, "The created .vis bytes do not match the mutation receipt.");

        object MapDocumentRequest(string? expectedVisualizationRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA = projectXmlSha,
            expectedVisualizationRevision,
            kind = mapType,
            mapID,
            mapIndex,
            mapName = savedMapName,
            mapType,
        };

        object MapTreeRequest(string? expectedVisualizationRevision, string? xmlSha = null) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA = xmlSha ?? projectXmlSha,
            mapID,
            mapIndex,
            mapName = savedMapName,
            mapType,
            expectedVisualizationRevision,
            projectSessionID = mapID,
        };

        void VerifyMutation(JsonElement receipt, string? previousRevision)
        {
            if (OptionalString(receipt, "previousVisualizationRevision") != previousRevision)
                throw new InvalidDataException("A map source import used an unexpected previous .vis revision.");
            visualizationRevision = StringProperty(receipt, "visualizationRevision");
            AssertDiskRevision(visualizationPath, visualizationRevision,
                "The .vis bytes do not match a map source import receipt.");
        }

        string geoJsonSha = FileSha256(geoJsonPath);
        JsonElement geoJsonImport = (await SubmitOperationAsync(client, "map.vector.import", new
        {
            map = MapDocumentRequest(visualizationRevision),
            sourcePath = geoJsonPath,
            sourceRevision = geoJsonSha,
            expectedProjectRevision = projectRevision,
            expectedSourceCanonicalPath = Path.GetFullPath(geoJsonPath),
        }, cancellation)).Result;
        VerifyMutation(geoJsonImport, visualizationRevision);

        JsonElement shapefileFamily = (await SubmitOperationAsync(client, "map.vector.captureShapefile",
            new { sourcePath = shapefilePath }, cancellation)).Result;
        string shapefileSha = FileSha256(shapefilePath);
        JsonElement shapefileImport = (await SubmitOperationAsync(client, "map.vector.importShapefile", new
        {
            map = MapDocumentRequest(visualizationRevision),
            sourcePath = shapefilePath,
            expectedProjectRevision = projectRevision,
            expectedFamily = shapefileFamily,
        }, cancellation)).Result;
        VerifyMutation(shapefileImport, visualizationRevision);

        JsonElement rasterInspection = (await SubmitOperationAsync(client, "map.raster.layer.inspect",
            new { sourcePath = rasterPath }, cancellation)).Result;
        string rasterCanonicalPath = StringProperty(rasterInspection, "sourceCanonicalPath");
        string rasterSha = FileSha256(rasterPath);
        string rasterManifestRevision = StringProperty(rasterInspection, "sourceRevision");
        if (!rasterManifestRevision.Equals(rasterSha, StringComparison.OrdinalIgnoreCase) ||
            StringProperty(rasterInspection, "mapLayerType") != "SingleBandRasterMapLayer")
            throw new InvalidDataException("The GDAL inspection did not capture the standalone GeoTIFF source SHA and expected single-band map type.");
        JsonElement rasterImport = (await SubmitOperationAsync(client, "map.raster.layer.import", new
        {
            map = MapDocumentRequest(visualizationRevision),
            sourcePath = rasterCanonicalPath,
            sourceRevision = rasterManifestRevision,
            mapLayerType = StringProperty(rasterInspection, "mapLayerType"),
            expectedProjectRevision = projectRevision,
            expectedSourceCanonicalPath = rasterCanonicalPath,
        }, cancellation)).Result;
        VerifyMutation(rasterImport, visualizationRevision);

        JsonElement catalog = (await SubmitOperationAsync(client, "map.document.list", new
        {
            projectPath,
            projectXml,
            projectXmlSHA = projectXmlSha,
            expectedProjectRevision = projectRevision,
        }, cancellation)).Result;
        JsonElement[] maps = Property(catalog, "maps").EnumerateArray().ToArray();
        if (maps.Length != 1 || IntProperty(maps[0], "mapIndex") != mapIndex ||
            StringProperty(maps[0], "mapName") != savedMapName ||
            StringProperty(maps[0], "mapType") != mapType ||
            StringProperty(catalog, "visualizationRevision") != visualizationRevision ||
            IntProperty(catalog, "activeMapIndex") != mapIndex)
            throw new InvalidDataException("Saved map discovery did not recover the imported source map and latest .vis revision.");

        JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapTreeRequest(visualizationRevision), cancellation)).Result;
        if (StringProperty(tree, "visualizationRevision") != visualizationRevision)
            throw new InvalidDataException("The persisted map tree returned a different visualization revision.");
        JsonElement[] treeNodes = CollectTreeNodes(Property(tree, "roots"));
        MapLayerNode pointNode = FindMapLayer(treeNodes, "PointsVector", geoJsonPath);
        MapLayerNode polygonNode = FindMapLayer(treeNodes, "PolygonsVector", geoJsonPath);
        MapLayerNode lineNode = FindMapLayer(treeNodes, "PolylinesVector", shapefilePath);
        MapLayerNode rasterNode = FindMapLayer(treeNodes, "SingleBandRasterMapLayer", rasterCanonicalPath);

        byte[] visBytesBeforeReads = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        string diskVisSha = Sha256(visBytesBeforeReads);
        if (!diskVisSha.Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The persisted .vis SHA does not match the current map tree revision.");

        JsonElement pointResult = await LoadVectorAsync(client, MapTreeRequest(visualizationRevision), pointNode, cancellation);
        AssertSourceCapture(pointResult, projectRevision, projectXmlSha, visualizationRevision, mapID,
            mapIndex, savedMapName, mapType, pointNode.Locator, "geojson", geoJsonPath);
        JsonElement pointData = Property(pointResult, "data");
        AssertShapeData(pointData, "Point", 0, 2, 0.5, 0.5, 3.75, 0.75);
        JsonElement[] pointFeatures = Property(pointData, "features").EnumerateArray().ToArray();
        if (pointFeatures.Length != 2 || StringProperty(pointFeatures[0], "sourceID") != "point-甲" ||
            IntProperty(pointFeatures[0], "sourceOrdinal") != 0 ||
            StringProperty(pointFeatures[1], "sourceID") != "second-point" ||
            IntProperty(pointFeatures[1], "sourceOrdinal") != 3 ||
            AttributeString(pointFeatures[0], "name") != "测点甲" ||
            AttributeString(pointFeatures[0], "reading") != "12.5" ||
            AttributeBoolean(pointFeatures[0], "active") != true)
            throw new InvalidDataException("GeoJSON point IDs or original attributes were not returned from the saved source.");
        AssertPartCoordinates(pointFeatures[0], 0, [(0.5, 0.5)]);
        AssertPartCoordinates(pointFeatures[1], 0, [(3.5, 0.5)]);
        AssertPartCoordinates(pointFeatures[1], 1, [(3.75, 0.75)]);

        JsonElement polygonResult = await LoadVectorAsync(client, MapTreeRequest(visualizationRevision), polygonNode, cancellation);
        AssertSourceCapture(polygonResult, projectRevision, projectXmlSha, visualizationRevision, mapID,
            mapIndex, savedMapName, mapType, polygonNode.Locator, "geojson", geoJsonPath);
        JsonElement polygonData = Property(polygonResult, "data");
        AssertShapeData(polygonData, "Polygon", 0, 2, 0, 0, 4, 4);
        JsonElement[] polygonFeatures = Property(polygonData, "features").EnumerateArray().ToArray();
        if (polygonFeatures.Length != 2 || StringProperty(polygonFeatures[0], "sourceID") != "77" ||
            IntProperty(polygonFeatures[0], "sourceOrdinal") != 1 ||
            StringProperty(polygonFeatures[1], "sourceID") != "multi-area" ||
            IntProperty(polygonFeatures[1], "sourceOrdinal") != 2 ||
            AttributeString(polygonFeatures[0], "name") != "带洞面" ||
            AttributeString(polygonFeatures[1], "name") != "多面区")
            throw new InvalidDataException("GeoJSON polygon IDs, source ordinals, or attributes were not preserved.");
        AssertPolygonParts(polygonFeatures[0], [(0, false), (0, true)]);
        AssertPolygonParts(polygonFeatures[1], [(0, false), (0, true), (1, false)]);
        AssertPartCoordinates(polygonFeatures[0], 0, [(0, 0), (4, 0), (4, 4), (0, 4), (0, 0)]);
        AssertPartCoordinates(polygonFeatures[0], 1, [(1, 1), (2, 1), (2, 2), (1, 2), (1, 1)]);
        AssertPartCoordinates(polygonFeatures[1], 0, [(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)]);
        AssertPartCoordinates(polygonFeatures[1], 1, [(0.2, 0.2), (0.4, 0.2), (0.4, 0.4), (0.2, 0.4), (0.2, 0.2)]);
        AssertPartCoordinates(polygonFeatures[1], 2, [(3, 3), (4, 3), (4, 4), (3, 4), (3, 3)]);

        JsonElement lineResult = await LoadVectorAsync(client, MapTreeRequest(visualizationRevision), lineNode, cancellation);
        AssertSourceCapture(lineResult, projectRevision, projectXmlSha, visualizationRevision, mapID,
            mapIndex, savedMapName, mapType, lineNode.Locator, "shapefile", shapefilePath);
        JsonElement lineData = Property(lineResult, "data");
        AssertShapeData(lineData, "Polyline", 3, 1, 0.25, 0.75, 3.75, 3.25);
        JsonElement shapefileFeature = Property(lineData, "features").EnumerateArray().Single();
        JsonValueKind sourceIDKind = PropertyOrNull(shapefileFeature, "sourceID").ValueKind;
        if (sourceIDKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ||
            AttributeString(shapefileFeature, "label").TrimEnd() != "SHP-LINE")
            throw new InvalidDataException($"The real Shapefile record or DBF attribute was not read from the persisted layer: {shapefileFeature.GetRawText()}");
        JsonElement[] lineVertices = Property(shapefileFeature, "parts").EnumerateArray().Single()
            .GetProperty("vertices").EnumerateArray().ToArray();
        if (lineVertices.Length != 2) throw new InvalidDataException("The Shapefile polyline vertex count changed.");
        AssertCoordinates(lineVertices[0], 0.25, 0.75);
        AssertCoordinates(lineVertices[1], 3.75, 3.25);

        JsonElement rasterResult = (await SubmitOperationAsync(client, "map.raster.layer.load", new
        {
            source = new
            {
                id = RasterNodeID(mapIndex, ReadNodePath(rasterNode.Locator)),
                projectPath,
                visualizationPath,
                visualizationRevision,
                mapIndex,
                mapName = savedMapName,
                mapType,
                nodePath = ReadNodePath(rasterNode.Locator),
                nodeName = StringProperty(rasterNode.Value, "name"),
                mapLayerType = "SingleBandRasterMapLayer",
                filename = OptionalString(rasterNode.Locator, "filename"),
                filenameRelative = OptionalString(rasterNode.Locator, "filenameRelative"),
                sourcePath = rasterCanonicalPath,
                sourceMapID = mapID,
                sourceRevision = rasterManifestRevision,
            },
            projectXml,
        }, cancellation)).Result;
        JsonElement rasterPayload = Property(rasterResult, "raster");
        int rasterWidth = IntProperty(rasterPayload, "width");
        int rasterHeight = IntProperty(rasterPayload, "height");
        byte[] rgba = Convert.FromBase64String(StringProperty(rasterPayload, "rgba"));
        double rasterMinX = NumberProperty(rasterPayload, "minX");
        double rasterMinY = NumberProperty(rasterPayload, "minY");
        double rasterMaxX = NumberProperty(rasterPayload, "maxX");
        double rasterMaxY = NumberProperty(rasterPayload, "maxY");
        if (StringProperty(rasterResult, "visualizationRevision") != visualizationRevision ||
            StringProperty(rasterResult, "sourceRevision") != rasterSha ||
            StringProperty(rasterPayload, "id") != RasterNodeID(mapIndex, ReadNodePath(rasterNode.Locator)) ||
            rasterWidth <= 0 || rasterHeight <= 0 || rgba.Length != checked(rasterWidth * rasterHeight * 4) ||
            rgba.Where((_value, index) => index % 4 == 3).Any(alpha => alpha != 255) ||
            DistinctPixelCount(rgba) < 2 ||
            Math.Abs(rasterMinX) > 1e-8 || Math.Abs(rasterMinY) > 1e-8 ||
            Math.Abs(rasterMaxX - 4) > 1e-8 || Math.Abs(rasterMaxY - 4) > 1e-8)
            throw new InvalidDataException("The persisted GeoTIFF read did not return its source revision, 4×4 extent and varying opaque pixels.");

        JsonElement pointFamily = Property(Property(pointResult, "source"), "family");
        await ExpectSourceChangedAsync(client, "map.vector.source.load", new
        {
            map = MapTreeRequest(visualizationRevision, new string('0', 64)),
            node = polygonNode.Locator,
            expectedFamily = (object?)null,
        }, cancellation, "a stale project XML SHA");
        await ExpectSourceChangedAsync(client, "map.vector.source.load", new
        {
            map = MapTreeRequest(new string('0', 64)),
            node = polygonNode.Locator,
            expectedFamily = (object?)null,
        }, cancellation, "a stale visualization revision");

        byte[] originalGeoJsonBytes = await File.ReadAllBytesAsync(geoJsonPath, cancellation);
        try
        {
            await File.WriteAllBytesAsync(geoJsonPath, [.. originalGeoJsonBytes, (byte)' '], cancellation);
            await ExpectSourceChangedAsync(client, "map.vector.source.load", new
            {
                map = MapTreeRequest(visualizationRevision),
                node = polygonNode.Locator,
                expectedFamily = pointFamily,
            }, cancellation, "a stale GeoJSON source SHA");
        }
        finally
        {
            File.WriteAllBytes(geoJsonPath, originalGeoJsonBytes);
        }

        byte[] visBytesAfterReads = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        bool readOnlyPreservedVis = visBytesBeforeReads.SequenceEqual(visBytesAfterReads);
        if (!readOnlyPreservedVis)
            throw new InvalidDataException("Tree loading, source loading or stale-source rejection modified the persisted .vis bytes.");

        bool geoJsonShaVerified = FileSha256(geoJsonPath).Equals(geoJsonSha, StringComparison.OrdinalIgnoreCase);
        bool shapefileShaVerified = VerifyFamilyFileShas(Property(lineResult, "source"), shapefilePath);
        bool geoTiffShaVerified = rasterSha.Equals(FileSha256(rasterPath), StringComparison.OrdinalIgnoreCase) &&
            rasterManifestRevision.Equals(rasterSha, StringComparison.OrdinalIgnoreCase);
        bool visualizationShaVerified = Sha256(visBytesAfterReads).Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!geoJsonShaVerified || !shapefileShaVerified || !geoTiffShaVerified || !visualizationShaVerified)
            throw new InvalidDataException("One or more imported source SHA values do not match the retained source bytes.");

        _ = await SubmitOperationAsync(client, "backend.info", new { }, cancellation);
        return new("completed", workspaceId, projectPath, savedMapName, visualizationRevision,
            pointFeatures.Length, polygonFeatures.Length, IntProperty(lineData, "count"),
            rasterWidth, rasterHeight, rasterMinX, rasterMinY, rasterMaxX, rasterMaxY,
            geoJsonShaVerified, shapefileShaVerified, geoTiffShaVerified, visualizationShaVerified,
            readOnlyPreservedVis, true, true, true);
    }

    private static SourceFixture CreateSourceFixture(string root)
    {
        RasRuntimeBootstrap.Configure();
        string sourceRoot = Path.Combine(root, "map-source-fixtures");
        if (File.Exists(sourceRoot) || Directory.Exists(sourceRoot))
            throw new IOException("Map source fixture output must use a new directory: " + sourceRoot);
        Directory.CreateDirectory(sourceRoot);
        string geoJsonPath = Path.Combine(sourceRoot, GeoJsonFileName);
        string shapefilePath = Path.Combine(sourceRoot, ShapefileFileName);
        string rasterPath = Path.Combine(sourceRoot, GeoTiffFileName);
        foreach (string path in new[] { geoJsonPath, shapefilePath, rasterPath })
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException("Map source fixture files must not overwrite existing files: " + path);

        File.WriteAllText(geoJsonPath, GeoJsonFeatureCollection, new UTF8Encoding(false));
        WriteShapefile(shapefilePath);
        WriteGeoTiff(rasterPath);
        return new(geoJsonPath, shapefilePath, rasterPath);
    }

    private static void WriteShapefile(string path)
    {
        using OSGeo.OGR.Driver driver = Ogr.GetDriverByName("ESRI Shapefile")
            ?? throw new InvalidOperationException("The GDAL OGR ESRI Shapefile writer is unavailable.");
        using DataSource dataSource = driver.CreateDataSource(path, null)
            ?? throw new InvalidOperationException("The GDAL OGR Shapefile datasource could not be created.");
        using Layer layer = dataSource.CreateLayer("Map Source Lines", null, wkbGeometryType.wkbLineString, null)
            ?? throw new InvalidOperationException("The GDAL OGR Shapefile layer could not be created.");
        using var field = new FieldDefn("label", FieldType.OFTString);
        if (layer.CreateField(field, 1) != 0)
            throw new InvalidDataException("The GDAL OGR Shapefile DBF field could not be created.");
        using FeatureDefn featureDefinition = layer.GetLayerDefn();
        using var feature = new Feature(featureDefinition);
        feature.SetField("label", "SHP-LINE");
        using var geometry = new Geometry(wkbGeometryType.wkbLineString);
        geometry.AddPoint(0.25, 0.75, 0);
        geometry.AddPoint(3.75, 3.25, 0);
        if (feature.SetGeometry(geometry) != 0 || layer.CreateFeature(feature) != 0)
            throw new InvalidDataException("The GDAL OGR Shapefile geometry record could not be written.");
    }

    private static void WriteGeoTiff(string path)
    {
        const int width = 4;
        const int height = 4;
        using OSGeo.GDAL.Driver driver = Gdal.GetDriverByName("GTiff")
            ?? throw new InvalidOperationException("The GDAL GeoTIFF writer is unavailable.");
        using Dataset dataset = driver.Create(path, width, height, 1, DataType.GDT_Float32, null)
            ?? throw new InvalidOperationException("The GDAL GeoTIFF dataset could not be created.");
        dataset.SetGeoTransform([0, 1, 0, 4, 0, -1]);
        using Band band = dataset.GetRasterBand(1);
        float[] pixels = Enumerable.Range(1, width * height).Select(value => (float)value).ToArray();
        _ = band.WriteRaster(0, 0, width, height, pixels, width, height, 0, 0);
        dataset.FlushCache();
        band.FlushCache();
    }

    private static async Task<JsonElement> LoadVectorAsync(HttpClient client, object map,
        MapLayerNode node, CancellationToken cancellation) =>
        (await SubmitOperationAsync(client, "map.vector.source.load", new
        {
            map,
            node = node.Locator,
            expectedFamily = (object?)null,
        }, cancellation)).Result;

    private static void AssertSourceCapture(JsonElement result, string projectRevision, string projectXmlSha,
        string visualizationRevision, string mapID, int mapIndex, string mapName, string mapType,
        JsonElement requestedLocator, string expectedFamily, string expectedSourcePath)
    {
        JsonElement source = Property(result, "source");
        JsonElement map = Property(source, "map");
        JsonElement family = Property(source, "family");
        if (!StringProperty(map, "projectRevision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(map, "projectXmlSHA").Equals(projectXmlSha, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(map, "visualizationRevision").Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
            StringProperty(map, "mapID") != mapID || IntProperty(map, "mapIndex") != mapIndex ||
            StringProperty(map, "mapName") != mapName || StringProperty(map, "mapType") != mapType ||
            !BooleanProperty(map, "isPersisted") || StringProperty(family, "family") != expectedFamily ||
            !PathEquals(StringProperty(family, "sourcePath"), expectedSourcePath))
            throw new InvalidDataException("A source load returned a different project, saved map, .vis or source family identity.");
        AssertSameLocator(Property(source, "locator"), requestedLocator);
        JsonElement[] files = Property(family, "files").EnumerateArray().ToArray();
        JsonElement sourceFile = files.SingleOrDefault(file => StringProperty(file, "role") == "source" && BooleanProperty(file, "required"));
        if (sourceFile.ValueKind != JsonValueKind.Object || !BooleanProperty(sourceFile, "exists") ||
            !StringProperty(sourceFile, "revision").Equals(FileSha256(expectedSourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The map source family did not return the current primary file SHA.");
    }

    private static void AssertSameLocator(JsonElement actual, JsonElement requested)
    {
        if (StringProperty(actual, "rootName") != StringProperty(requested, "rootName") ||
            StringProperty(actual, "nodeType") != StringProperty(requested, "nodeType") ||
            StringProperty(actual, "nodeName") != StringProperty(requested, "nodeName") ||
            StringProperty(actual, "mapType") != StringProperty(requested, "mapType") ||
            OptionalString(actual, "filename") != OptionalString(requested, "filename") ||
            OptionalString(actual, "filenameRelative") != OptionalString(requested, "filenameRelative") ||
            !IntArray(Property(actual, "nodePath")).SequenceEqual(IntArray(Property(requested, "nodePath"))))
            throw new InvalidDataException("The source reader returned a different saved layer locator.");
    }

    private static void AssertShapeData(JsonElement data, string shapeType, int shapeTypeCode,
        int count, double minX, double minY, double maxX, double maxY)
    {
        if (StringProperty(data, "shapeType") != shapeType || IntProperty(data, "shapeTypeCode") != shapeTypeCode ||
            IntProperty(data, "count") != count)
            throw new InvalidDataException($"The source reader did not return the expected {shapeType} feature count and shape code.");
        AssertExtent(Property(data, "sourceExtent"), minX, minY, maxX, maxY, "source extent");
        AssertExtent(Property(data, "extent"), minX, minY, maxX, maxY, "project extent");
        if (StringProperty(data, "projectionStatus") != "localProjectSourceCoordinates")
            throw new InvalidDataException("The Local map source reader unexpectedly changed its source coordinates.");
    }

    private static void AssertExtent(JsonElement extent, double minX, double minY, double maxX, double maxY, string label)
    {
        if (Math.Abs(NumberProperty(extent, "minX") - minX) > 1e-8 ||
            Math.Abs(NumberProperty(extent, "minY") - minY) > 1e-8 ||
            Math.Abs(NumberProperty(extent, "maxX") - maxX) > 1e-8 ||
            Math.Abs(NumberProperty(extent, "maxY") - maxY) > 1e-8)
            throw new InvalidDataException($"The actual {label} does not match the written source coordinates.");
    }

    private static void AssertCoordinates(JsonElement vertex, double x, double y)
    {
        if (Math.Abs(NumberProperty(vertex, "sourceX") - x) > 1e-8 ||
            Math.Abs(NumberProperty(vertex, "sourceY") - y) > 1e-8 ||
            Math.Abs(NumberProperty(vertex, "x") - x) > 1e-8 ||
            Math.Abs(NumberProperty(vertex, "y") - y) > 1e-8)
            throw new InvalidDataException("The returned vertex does not preserve its real source and project coordinates.");
    }

    private static void AssertPolygonParts(JsonElement feature, (int PolygonIndex, bool IsHole)[] expected)
    {
        JsonElement[] parts = Property(feature, "parts").EnumerateArray().ToArray();
        if (parts.Length != expected.Length)
            throw new InvalidDataException("The persisted polygon source returned a different ring count.");
        for (int index = 0; index < parts.Length; index++)
        {
            if (IntProperty(parts[index], "partOrdinal") != index ||
                IntProperty(parts[index], "polygonIndex") != expected[index].PolygonIndex ||
                BooleanProperty(parts[index], "isHole") != expected[index].IsHole)
                throw new InvalidDataException("The GeoJSON hole or MultiPolygon ring grouping was lost.");
            JsonElement[] vertices = Property(parts[index], "vertices").EnumerateArray().ToArray();
            if (vertices.Length != 5 || !SameCoordinate(vertices[0], vertices[^1]))
                throw new InvalidDataException("A persisted GeoJSON polygon ring lost its closed source coordinates.");
        }
    }

    private static void AssertPartCoordinates(JsonElement feature, int partIndex, (double X, double Y)[] expected)
    {
        JsonElement[] parts = Property(feature, "parts").EnumerateArray().ToArray();
        if ((uint)partIndex >= (uint)parts.Length)
            throw new InvalidDataException("The persisted source omitted an expected geometry part.");
        JsonElement[] vertices = Property(parts[partIndex], "vertices").EnumerateArray().ToArray();
        if (vertices.Length != expected.Length)
            throw new InvalidDataException("The persisted geometry part returned a different vertex count.");
        for (int index = 0; index < expected.Length; index++)
            AssertCoordinates(vertices[index], expected[index].X, expected[index].Y);
    }

    private static string AttributeString(JsonElement feature, string name)
    {
        JsonElement[] attributes = Property(feature, "attributes").EnumerateArray().ToArray();
        JsonElement attribute = attributes.SingleOrDefault(value => StringProperty(value, "columnName") == name);
        return attribute.ValueKind == JsonValueKind.Object ? OptionalString(attribute, "value") ?? "" : "";
    }

    private static bool? AttributeBoolean(JsonElement feature, string name)
    {
        JsonElement[] attributes = Property(feature, "attributes").EnumerateArray().ToArray();
        JsonElement attribute = attributes.SingleOrDefault(value => StringProperty(value, "columnName") == name);
        JsonElement value = PropertyOrNull(attribute, "booleanValue");
        return value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null;
    }

    private static async Task ExpectSourceChangedAsync(HttpClient client, string method, object parameters,
        CancellationToken cancellation, string staleIdentity)
    {
        using HttpResponseMessage accepted = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        if (accepted.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidDataException($"The stale {staleIdentity} check was not accepted as a worker operation.");
        using JsonDocument submission = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(cancellation));
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = snapshot.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanProperty(root, "hasMore")) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                if (state != "failed" || StringProperty(Property(root, "error"), "code") != "sourceChanged")
                    throw new InvalidDataException($"The stale {staleIdentity} was not rejected as sourceChanged: {root}");
                return;
            }
            await Task.Delay(50, cancellation);
        }
    }

    private static async Task<OperationReceipt> SubmitOperationAsync(HttpClient client, string method,
        object parameters, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations ({method}) returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument submitted = JsonDocument.Parse(body);
        string id = StringProperty(submitted.RootElement, "id");
        return await WaitForOperationAsync(client, id, cancellation);
    }

    private static async Task<OperationReceipt> WaitForOperationAsync(HttpClient client, string id,
        CancellationToken cancellation)
    {
        long cursor = 0;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = snapshot.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanProperty(root, "hasMore")) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
            {
                if (state is not ("completed" or "succeeded"))
                    throw new InvalidOperationException($"Server operation {id} ended in {state}: {PropertyOrNull(root, "error")}");
                JsonElement result = Property(root, "result");
                if (result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    throw new InvalidDataException($"Completed server operation {id} omitted its result.");
                return new(id, state, result.Clone());
            }
            await Task.Delay(50, cancellation);
        }
    }

    private static async Task<JsonElement> UploadFixtureAsync(HttpClient client, string root,
        CancellationToken cancellation)
    {
        byte[] archive = ZipDirectory(root);
        using var multipart = new MultipartFormDataContent();
        using var stream = new MemoryStream(archive, writable: false);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        multipart.Add(content, "files", "map-source-acceptance.zip");
        using HttpResponseMessage response = await client.PostAsync("api/workspaces", multipart, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new HttpRequestException($"POST api/workspaces returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static byte[] ZipDirectory(string root)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                using Stream destination = entry.Open();
                using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                source.CopyTo(destination);
            }
        }
        return buffer.ToArray();
    }

    private static async Task<JsonDocument> GetWhenReadyAsync(HttpClient client, string path,
        CancellationToken cancellation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try { return await GetJsonAsync(client, path, cancellation); }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250, cancellation);
            }
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path,
        CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {path} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        return JsonDocument.Parse(body);
    }

    private static MapLayerNode FindMapLayer(IEnumerable<JsonElement> tree, string mapType, string sourcePath)
    {
        JsonElement[] matches = tree.Where(node => PropertyOrNull(node, "locator").ValueKind == JsonValueKind.Object)
            .Where(node => StringProperty(Property(node, "locator"), "nodeType") == "MapLayer" &&
                StringProperty(Property(node, "locator"), "mapType") == mapType &&
                PathEquals(OptionalString(node, "assetID") ?? "", sourcePath))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The saved map tree must contain one checked {mapType} node for {sourcePath}; found {matches.Length}.");
        JsonElement selected = matches[0];
        if (StringProperty(selected, "checked") != "checked")
            throw new InvalidDataException($"The saved {mapType} node is not persisted as checked.");
        return new(selected, Property(selected, "locator"));
    }

    private static JsonElement[] CollectTreeNodes(JsonElement roots)
    {
        if (roots.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The saved map tree omitted its roots array.");
        var all = new List<JsonElement>();
        void Visit(JsonElement node)
        {
            all.Add(node);
            JsonElement children = PropertyOrNull(node, "children");
            if (children.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in children.EnumerateArray()) Visit(child);
        }
        foreach (JsonElement root in roots.EnumerateArray()) Visit(root);
        return all.ToArray();
    }

    private static bool VerifyFamilyFileShas(JsonElement source, string sourcePath)
    {
        JsonElement family = Property(source, "family");
        if (StringProperty(family, "family") != "shapefile") return false;
        JsonElement[] files = Property(family, "files").EnumerateArray().ToArray();
        foreach (JsonElement file in files)
        {
            string role = StringProperty(file, "role");
            bool required = BooleanProperty(file, "required");
            bool exists = BooleanProperty(file, "exists");
            string? revision = OptionalString(file, "revision");
            string filePath = StringProperty(file, "path");
            if (required && (!exists || revision is null)) return false;
            if (exists && (revision is null || !revision.Equals(FileSha256(filePath), StringComparison.OrdinalIgnoreCase)))
                return false;
            if (role == "source" && !PathEquals(filePath, sourcePath)) return false;
        }
        return files.Any(file => StringProperty(file, "role") == "shx" && BooleanProperty(file, "required") && BooleanProperty(file, "exists")) &&
            files.Any(file => StringProperty(file, "role") == "dbf" && BooleanProperty(file, "required") && BooleanProperty(file, "exists"));
    }

    private static int[] ReadNodePath(JsonElement locator) => IntArray(Property(locator, "nodePath"));
    private static string RasterNodeID(int mapIndex, int[] nodePath) => $"vis-map-{mapIndex}-node-{string.Join(".", nodePath)}";

    private static int DistinctPixelCount(byte[] rgba) => rgba.Chunk(4)
        .Select(pixel => (pixel[0], pixel[1], pixel[2], pixel[3])).Distinct().Count();

    private static bool SameCoordinate(JsonElement left, JsonElement right) =>
        Math.Abs(NumberProperty(left, "sourceX") - NumberProperty(right, "sourceX")) <= 1e-8 &&
        Math.Abs(NumberProperty(left, "sourceY") - NumberProperty(right, "sourceY")) <= 1e-8;

    private static string UploadedPath(string uploadedRoot, string sourceRoot, string sourcePath)
    {
        string relative = Path.GetRelativePath(sourceRoot, sourcePath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidDataException("A map source fixture file escaped its upload root.");
        return Path.GetFullPath(Path.Combine(uploadedRoot, relative));
    }

    private static void AssertDiskRevision(string path, string revision, string message)
    {
        if (!FileSha256(path).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    private static string FileSha256(string path) => Sha256(File.ReadAllBytes(path));
    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static int[] IntArray(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(item => item.GetInt32()).ToArray()
        : throw new InvalidDataException("A saved map node path is missing.");

    private static bool IsTerminal(string state) => state.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("succeeded", StringComparison.OrdinalIgnoreCase) || state.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("cancelled", StringComparison.OrdinalIgnoreCase) || state.Equals("canceled", StringComparison.OrdinalIgnoreCase);

    private static string StringProperty(JsonElement value, string name) =>
        Property(value, name).GetString() ?? throw new InvalidDataException($"JSON property '{name}' is null.");
    private static string? OptionalString(JsonElement value, string name)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
    private static bool BooleanProperty(JsonElement value, string name) => Property(value, name).GetBoolean();
    private static int IntProperty(JsonElement value, string name) => Property(value, name).GetInt32();
    private static long LongPropertyOrDefault(JsonElement value, string name, long fallback) =>
        PropertyOrNull(value, name) is { ValueKind: JsonValueKind.Number } property && property.TryGetInt64(out long number)
            ? number : fallback;
    private static double NumberProperty(JsonElement value, string name) => Property(value, name).GetDouble();
    private static JsonElement Property(JsonElement value, string name)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind == JsonValueKind.Undefined
            ? throw new InvalidDataException($"JSON property '{name}' is missing.") : property;
    }
    private static JsonElement PropertyOrNull(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property) ? property : default;
    private sealed record SourceFixture(string GeoJsonPath, string ShapefilePath, string GeoTiffPath);
    private sealed record MapLayerNode(JsonElement Value, JsonElement Locator);
    private sealed record OperationReceipt(string Id, string State, JsonElement Result);
}

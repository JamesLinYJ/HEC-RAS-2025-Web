using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OSGeo.OGR;
using Ras.Backend;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record GdbSourceAcceptanceReport(
    string State,
    string ProjectPath,
    string UploadID,
    string SourcePath,
    string SourceRevision,
    int ManifestFileCount,
    string[] CatalogLayers,
    int PointFeatureCount,
    int PolygonFeatureCount,
    bool ChineseFieldsVerified,
    bool InteriorRingVerified,
    bool VisualizationRevisionVerified,
    bool ReadOperationsPreservedVisualization,
    bool StaleSourceRejected,
    bool StaleProjectRejected,
    int InvalidPathCasesRejected);

/// <summary>Exercises real OpenFileGDB upload, catalog, saved-map import and source reads over loopback HTTP.</summary>
internal static class GdbSourceAcceptance
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AcceptanceTimeout = TimeSpan.FromMinutes(5);
    private const string MapName = "GDB来源验收";
    private const string GdbDirectoryName = "vector-fixture.gdb";
    private const string PointLayerName = "SurveyPoints";
    private const string PolygonLayerName = "ParcelWithHole";
    private const string PointFieldName = "测站名称";
    private const string PolygonFieldName = "地类说明";

    /// <summary>
    /// Writes a small, real OpenFileGDB vector dataset at a new absolute path.
    /// The directory contains a Chinese-field point layer and a polygon layer with an interior ring.
    /// </summary>
    public static string CreateVectorSource(string requestedGdbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedGdbPath);
        if (!Path.IsPathFullyQualified(requestedGdbPath))
            throw new ArgumentException("The OpenFileGDB fixture path must be absolute.", nameof(requestedGdbPath));
        string path = Path.GetFullPath(requestedGdbPath);
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("The OpenFileGDB fixture path must not already exist: " + path);
        string parent = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The OpenFileGDB fixture path has no parent directory.", nameof(requestedGdbPath));
        Directory.CreateDirectory(parent);

        RasRuntimeBootstrap.Configure();
        using Driver driver = Ogr.GetDriverByName("OpenFileGDB")
            ?? throw new InvalidOperationException("The GDAL OpenFileGDB writer is unavailable.");
        using (DataSource dataSource = driver.CreateDataSource(path, null)
            ?? throw new InvalidOperationException("The GDAL OpenFileGDB datasource could not be created."))
        {
            WritePointLayer(dataSource);
            WritePolygonLayer(dataSource);
        }

        GdbMapSourceIdentity source = GdbMapSourceService.CaptureSource(path);
        if (source.Files.Length == 0)
            throw new InvalidDataException("The OpenFileGDB writer created no stable data files.");
        return source.SourcePath;
    }

    public static async Task<GdbSourceAcceptanceReport> RunAsync(Uri baseAddress, string fixtureDirectory)
    {
        if (!baseAddress.IsLoopback ||
            (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("GDB source acceptance only connects to a loopback HTTP(S) URL.", nameof(baseAddress));
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureDirectory);
        string fixtureRoot = Path.GetFullPath(fixtureDirectory);
        if (File.Exists(fixtureRoot) || Directory.Exists(fixtureRoot))
            throw new IOException("The GDB acceptance fixture output must be a new directory: " + fixtureRoot);

        FixtureManifest projectFixture = PlanComputeFixture.CreateMap(fixtureRoot);
        string localGdbPath = CreateVectorSource(Path.Combine(fixtureRoot, "gdb-source-fixtures", GdbDirectoryName));
        GdbMapSourceIdentity localSource = GdbMapSourceService.CaptureSource(localGdbPath);

        using var timeout = new CancellationTokenSource(AcceptanceTimeout);
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("The GDB source server did not report ready.");

        OperationReceipt opened = await SubmitOperationAsync(client, "project.open",
            new { projectPath = projectFixture.ProjectPath }, cancellation);
        string projectPath = StringProperty(opened.Result, "projectPath");
        string projectRevision = StringProperty(opened.Result, "revision");
        string projectXml = StringProperty(opened.Result, "projectXml");
        string projectXmlSha = Sha256(Encoding.UTF8.GetBytes(projectXml));
        string projectSessionID = Guid.NewGuid().ToString("D");
        string sourcesRoot = Path.Combine(Path.GetDirectoryName(projectPath)!, "Map Sources");
        if (Directory.Exists(sourcesRoot))
            throw new InvalidDataException("The GDB acceptance project unexpectedly has a pre-existing Map Sources directory.");

        int invalidPathCases = 0;
        foreach (string[] invalidPaths in InvalidPathCases())
        {
            await ExpectInvalidUploadAsync(client, projectPath, projectRevision, projectSessionID,
                invalidPaths, cancellation);
            invalidPathCases++;
            if (Directory.Exists(sourcesRoot))
                throw new InvalidDataException("A rejected GDB upload created a Map Sources staging directory.");
        }

        JsonElement upload = await UploadValidGdbAsync(client, localGdbPath, projectPath,
            projectRevision, projectSessionID, cancellation);
        string uploadID = StringProperty(upload, "uploadID");
        if (uploadID.Length != 32 || uploadID.Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("The GDB upload returned an invalid upload ID.");
        if (StringProperty(upload, "projectPath") != projectPath ||
            !StringProperty(upload, "projectRevision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            StringProperty(upload, "projectSessionID") != projectSessionID)
            throw new InvalidDataException("The GDB upload receipt is not bound to the selected project session.");

        JsonElement[] uploadedSources = Property(upload, "sources").EnumerateArray().ToArray();
        if (uploadedSources.Length != 1)
            throw new InvalidDataException("A GDB directory upload must return exactly one source identity.");
        JsonElement uploadedSource = uploadedSources[0];
        string expectedGdbPath = Path.Combine(sourcesRoot, uploadID, GdbDirectoryName);
        string sourcePath = StringProperty(uploadedSource, "sourcePath");
        string sourceCanonicalPath = StringProperty(uploadedSource, "sourceCanonicalPath");
        string sourceRevision = StringProperty(uploadedSource, "sourceRevision");
        if (!PathEquals(sourcePath, expectedGdbPath) || !PathEquals(sourceCanonicalPath, expectedGdbPath) ||
            StringProperty(uploadedSource, "kind") != "gdb" ||
            StringProperty(uploadedSource, "fileName") != GdbDirectoryName ||
            !Directory.Exists(expectedGdbPath))
            throw new InvalidDataException("The GDB upload did not publish one source under the project Map Sources directory.");

        GdbMapSourceIdentity uploadedCapture = GdbMapSourceService.CaptureSource(expectedGdbPath);
        if (!sourceRevision.Equals(uploadedCapture.Revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The upload receipt manifest revision does not match the published GDB directory.");
        AssertManifestPreserved(localSource, uploadedCapture);
        if (!sourceRevision.Equals(StringProperty(uploadedSource, "sourceRevision"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The uploaded source revision changed while validating its manifest.");

        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (File.Exists(visualizationPath))
            throw new InvalidDataException("The GDB acceptance project unexpectedly has a saved .vis before map creation.");
        JsonElement created = (await SubmitOperationAsync(client, "map.document.create", new
        {
            projectPath,
            projectXml,
            projectXmlSHA = projectXmlSha,
            expectedVisualizationRevision = (string?)null,
            kind = "Project",
            name = MapName,
        }, cancellation)).Result;
        int mapIndex = IntProperty(created, "mapIndex");
        string mapName = StringProperty(created, "mapName");
        string mapType = StringProperty(created, "mapType");
        string visualizationRevision = StringProperty(created, "visualizationRevision");
        if (mapIndex != 0 || mapName != MapName || mapType != "Project")
            throw new InvalidDataException("Map creation returned a different saved map identity.");
        AssertDiskRevision(visualizationPath, visualizationRevision,
            "The created .vis bytes do not match the map creation receipt.");

        object MapDocumentRequest(string? expectedVisualizationRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA = projectXmlSha,
            expectedVisualizationRevision,
            kind = mapType,
            mapID = projectSessionID,
            mapIndex,
            mapName,
            mapType,
        };

        object MapTreeRequest(string? expectedVisualizationRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA = projectXmlSha,
            mapID = projectSessionID,
            mapIndex,
            mapName,
            mapType,
            expectedVisualizationRevision,
            projectSessionID,
        };

        JsonElement catalog = (await SubmitOperationAsync(client, "map.gdb.catalog", new
        {
            sourcePath,
            expectedSourceRevision = sourceRevision,
        }, cancellation)).Result;
        AssertManifest(Property(catalog, "source"), uploadedCapture);
        JsonElement[] catalogLayers = Property(catalog, "layers").EnumerateArray().ToArray();
        AssertCatalogLayer(catalogLayers, PointLayerName, "Point");
        AssertCatalogLayer(catalogLayers, PolygonLayerName, "PolygonCollection");
        if (catalogLayers.Length != 2)
            throw new InvalidDataException("The OpenFileGDB catalog did not return exactly the two fixture vector layers.");

        JsonElement imported = (await SubmitOperationAsync(client, "map.gdb.import", new
        {
            map = MapDocumentRequest(visualizationRevision),
            expectedProjectRevision = projectRevision,
            source = Property(catalog, "source"),
        }, cancellation)).Result;
        JsonElement importReceipt = Property(imported, "receipt");
        if (OptionalString(importReceipt, "previousVisualizationRevision") != visualizationRevision)
            throw new InvalidDataException("GDB import used an unexpected previous .vis revision.");
        visualizationRevision = StringProperty(importReceipt, "visualizationRevision");
        AssertDiskRevision(visualizationPath, visualizationRevision,
            "The imported GDB .vis bytes do not match the mutation receipt.");
        JsonElement[] importedLayers = Property(imported, "importedLayers").EnumerateArray().ToArray();
        if (importedLayers.Length != catalogLayers.Length)
            throw new InvalidDataException("The GDB import did not persist all catalogued vector layers.");

        JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapTreeRequest(visualizationRevision), cancellation)).Result;
        if (!StringProperty(tree, "visualizationRevision").Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The persisted GDB map tree returned a different .vis revision.");
        JsonElement[] nodes = CollectTreeNodes(Property(tree, "roots"));
        JsonElement pointNode = FindGdbVectorNode(nodes, sourcePath, PointLayerName);
        JsonElement polygonNode = FindGdbVectorNode(nodes, sourcePath, PolygonLayerName);

        byte[] visualizationBytesBeforeReads = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        AssertFileRevision(visualizationBytesBeforeReads, visualizationRevision,
            "The persisted .vis SHA does not match the current map tree revision.");
        JsonElement pointRead = (await SubmitOperationAsync(client, "map.gdb.source.load", new
        {
            map = MapTreeRequest(visualizationRevision),
            node = Property(pointNode, "locator"),
            expectedSourceRevision = sourceRevision,
        }, cancellation)).Result;
        AssertLoadedSource(pointRead, uploadedCapture, PointLayerName, PointFieldName, "甲站", "Point");

        JsonElement polygonRead = (await SubmitOperationAsync(client, "map.gdb.source.load", new
        {
            map = MapTreeRequest(visualizationRevision),
            node = Property(polygonNode, "locator"),
            expectedSourceRevision = sourceRevision,
        }, cancellation)).Result;
        bool chineseFieldsVerified = AssertLoadedSource(
            pointRead, uploadedCapture, PointLayerName, PointFieldName, "甲站", "Point") &&
            AssertLoadedSource(polygonRead, uploadedCapture, PolygonLayerName, PolygonFieldName, "保留内环", "PolygonCollection");
        bool interiorRingVerified = AssertInteriorRing(polygonRead);

        string changedFile = Path.Combine(expectedGdbPath, uploadedCapture.Files[0].RelativePath
            .Replace('/', Path.DirectorySeparatorChar));
        byte[] originalChangedFile = await File.ReadAllBytesAsync(changedFile, cancellation);
        try
        {
            await File.WriteAllBytesAsync(changedFile, [.. originalChangedFile, (byte)0x20], cancellation);
            await ExpectSourceChangedAsync(client, "map.gdb.source.load", new
            {
                map = MapTreeRequest(visualizationRevision),
                node = Property(pointNode, "locator"),
                expectedSourceRevision = sourceRevision,
            }, cancellation, "a stale GDB source manifest");
        }
        finally
        {
            await File.WriteAllBytesAsync(changedFile, originalChangedFile, cancellation);
        }
        bool sourceRestored = GdbMapSourceService.CaptureSource(expectedGdbPath).Revision
            .Equals(sourceRevision, StringComparison.OrdinalIgnoreCase);
        if (!sourceRestored)
            throw new InvalidDataException("The stale-source fixture did not restore the uploaded GDB bytes.");

        byte[] originalProjectBytes = await File.ReadAllBytesAsync(projectPath, cancellation);
        bool staleProjectRejected;
        try
        {
            await File.WriteAllBytesAsync(projectPath, [.. originalProjectBytes, (byte)'\n'], cancellation);
            using HttpResponseMessage staleResponse = await UploadGdbAsync(client, localGdbPath, projectPath,
                projectRevision, projectSessionID, cancellation);
            staleProjectRejected = await AssertHttpErrorAsync(staleResponse,
                HttpStatusCode.Conflict, "sourceChanged", "a stale project revision", cancellation);
        }
        finally
        {
            await File.WriteAllBytesAsync(projectPath, originalProjectBytes, cancellation);
        }

        byte[] visualizationBytesAfterReads = await File.ReadAllBytesAsync(visualizationPath, cancellation);
        bool readOperationsPreservedVisualization = visualizationBytesBeforeReads.SequenceEqual(visualizationBytesAfterReads);
        bool visualizationRevisionVerified = Sha256(visualizationBytesAfterReads)
            .Equals(visualizationRevision, StringComparison.OrdinalIgnoreCase);
        if (!readOperationsPreservedVisualization || !visualizationRevisionVerified)
            throw new InvalidDataException("GDB catalog, source reads or stale-source checks changed the saved .vis bytes.");
        if (!staleProjectRejected)
            throw new InvalidDataException("The GDB upload endpoint accepted a stale project revision.");

        string[] layerNames = catalogLayers.Select(layer => StringProperty(layer, "name"))
            .Order(StringComparer.Ordinal).ToArray();
        int pointFeatureCount = IntProperty(Property(pointRead, "data"), "count");
        int polygonFeatureCount = IntProperty(Property(polygonRead, "data"), "count");
        if (pointFeatureCount != 1 || polygonFeatureCount != 1)
            throw new InvalidDataException("GDB source loading did not return one real point and one real polygon feature.");

        return new("completed", projectPath, uploadID, sourcePath, sourceRevision,
            uploadedCapture.Files.Length, layerNames, pointFeatureCount, polygonFeatureCount,
            chineseFieldsVerified, interiorRingVerified, visualizationRevisionVerified,
            readOperationsPreservedVisualization, true, staleProjectRejected, invalidPathCases);
    }

    private static void WritePointLayer(DataSource dataSource)
    {
        using Layer layer = dataSource.CreateLayer(PointLayerName, null, wkbGeometryType.wkbPoint, null)
            ?? throw new InvalidOperationException("The OpenFileGDB point layer could not be created.");
        using var field = new FieldDefn(PointFieldName, FieldType.OFTString);
        if (layer.CreateField(field, 1) != 0)
            throw new InvalidDataException("The OpenFileGDB Chinese point field could not be created.");
        using FeatureDefn definition = layer.GetLayerDefn();
        using var feature = new Feature(definition);
        feature.SetField(PointFieldName, "甲站");
        using var geometry = new Geometry(wkbGeometryType.wkbPoint);
        geometry.AddPoint(1.5, 2.5, 0);
        if (feature.SetGeometry(geometry) != 0 || layer.CreateFeature(feature) != 0)
            throw new InvalidDataException("The OpenFileGDB point feature could not be written.");
    }

    private static void WritePolygonLayer(DataSource dataSource)
    {
        using Layer layer = dataSource.CreateLayer(PolygonLayerName, null, wkbGeometryType.wkbPolygon, null)
            ?? throw new InvalidOperationException("The OpenFileGDB polygon layer could not be created.");
        using var field = new FieldDefn(PolygonFieldName, FieldType.OFTString);
        if (layer.CreateField(field, 1) != 0)
            throw new InvalidDataException("The OpenFileGDB Chinese polygon field could not be created.");
        using FeatureDefn definition = layer.GetLayerDefn();
        using var feature = new Feature(definition);
        feature.SetField(PolygonFieldName, "保留内环");
        using var polygon = new Geometry(wkbGeometryType.wkbPolygon);
        using var exterior = new Geometry(wkbGeometryType.wkbLinearRing);
        exterior.AddPoint(0, 0, 0);
        exterior.AddPoint(10, 0, 0);
        exterior.AddPoint(10, 10, 0);
        exterior.AddPoint(0, 10, 0);
        exterior.AddPoint(0, 0, 0);
        using var interior = new Geometry(wkbGeometryType.wkbLinearRing);
        interior.AddPoint(2, 2, 0);
        interior.AddPoint(4, 2, 0);
        interior.AddPoint(4, 4, 0);
        interior.AddPoint(2, 4, 0);
        interior.AddPoint(2, 2, 0);
        if (polygon.AddGeometry(exterior) != 0 || polygon.AddGeometry(interior) != 0 ||
            feature.SetGeometry(polygon) != 0 || layer.CreateFeature(feature) != 0)
            throw new InvalidDataException("The OpenFileGDB polygon-with-hole feature could not be written.");
    }

    private static IEnumerable<string[]> InvalidPathCases()
    {
        yield return [$"{GdbDirectoryName}/../escape.gdbtable"];
        yield return ["/absolute.gdb/a00000001.gdbtable"];
        yield return [$"{GdbDirectoryName}/a00000001.gdbtable", $"other.gdb/a00000002.gdbtable"];
        yield return [$"{GdbDirectoryName}/Table.gdbtable", $"{GdbDirectoryName}/table.gdbtable"];
        yield return [$"{GdbDirectoryName}/child", $"{GdbDirectoryName}/child/file.gdbtable"];
        yield return [GdbDirectoryName + "/" + string.Join('/', Enumerable.Repeat("nested", 64)) + "/file.gdbtable"];
    }

    private static async Task<JsonElement> UploadValidGdbAsync(HttpClient client, string gdbPath,
        string projectPath, string projectRevision, string projectSessionID, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await UploadGdbAsync(client, gdbPath, projectPath,
            projectRevision, projectSessionID, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new HttpRequestException($"POST api/gdb-source-uploads returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> UploadGdbAsync(HttpClient client, string gdbPath,
        string projectPath, string projectRevision, string projectSessionID, CancellationToken cancellation)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(projectPath), "projectPath");
        multipart.Add(new StringContent(projectRevision), "projectRevision");
        multipart.Add(new StringContent(projectSessionID), "projectSessionID");
        string gdbRootName = Path.GetFileName(gdbPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (string path in Directory.EnumerateFiles(gdbPath, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(gdbPath, path).Replace(Path.DirectorySeparatorChar, '/');
            string webkitRelativePath = gdbRootName + "/" + relative;
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellation));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(content, "files", webkitRelativePath);
        }
        return await client.PostAsync("api/gdb-source-uploads", multipart, cancellation);
    }

    private static async Task ExpectInvalidUploadAsync(HttpClient client, string projectPath,
        string projectRevision, string projectSessionID, string[] paths, CancellationToken cancellation)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(projectPath), "projectPath");
        multipart.Add(new StringContent(projectRevision), "projectRevision");
        multipart.Add(new StringContent(projectSessionID), "projectSessionID");
        foreach (string path in paths)
        {
            var content = new ByteArrayContent([0x41]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(content, "files", path);
        }
        using HttpResponseMessage response = await client.PostAsync("api/gdb-source-uploads", multipart, cancellation);
        await AssertHttpErrorAsync(response, HttpStatusCode.BadRequest, "invalidInput",
            "an invalid GDB relative path", cancellation);
    }

    private static async Task<bool> AssertHttpErrorAsync(HttpResponseMessage response,
        HttpStatusCode expectedStatus, string expectedCode, string label, CancellationToken cancellation)
    {
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != expectedStatus)
            throw new InvalidDataException($"The server did not reject {label} with HTTP {(int)expectedStatus}: {(int)response.StatusCode} {body}");
        using JsonDocument document = JsonDocument.Parse(body);
        string actualCode = StringProperty(Property(document.RootElement, "error"), "code");
        if (actualCode != expectedCode)
            throw new InvalidDataException($"The server rejected {label} with {actualCode}, expected {expectedCode}: {body}");
        return true;
    }

    private static void AssertManifestPreserved(GdbMapSourceIdentity original, GdbMapSourceIdentity uploaded)
    {
        if (original.Files.Length != uploaded.Files.Length)
            throw new InvalidDataException("The GDB upload changed the number of stable manifest files.");
        for (int index = 0; index < original.Files.Length; index++)
        {
            GdbMapSourceFileRevision expected = original.Files[index];
            GdbMapSourceFileRevision actual = uploaded.Files[index];
            if (expected.RelativePath != actual.RelativePath || expected.Length != actual.Length ||
                !expected.Revision.Equals(actual.Revision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The GDB upload did not preserve an internal path and byte-for-byte manifest entry.");
        }
    }

    private static void AssertManifest(JsonElement actual, GdbMapSourceIdentity expected)
    {
        if (!PathEquals(StringProperty(actual, "sourcePath"), expected.SourcePath) ||
            !StringProperty(actual, "revision").Equals(expected.Revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A GDB operation returned a different source path or manifest revision.");
        JsonElement[] files = Property(actual, "files").EnumerateArray().ToArray();
        if (files.Length != expected.Files.Length)
            throw new InvalidDataException("A GDB operation returned a different manifest file count.");
        for (int index = 0; index < files.Length; index++)
        {
            if (StringProperty(files[index], "relativePath") != expected.Files[index].RelativePath ||
                LongProperty(files[index], "length") != expected.Files[index].Length ||
                !StringProperty(files[index], "revision").Equals(expected.Files[index].Revision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A GDB operation returned a different internal path, length or file SHA.");
        }
    }

    private static void AssertCatalogLayer(JsonElement[] layers, string name, string vectorType)
    {
        JsonElement[] matches = layers.Where(layer => StringProperty(layer, "name") == name).ToArray();
        if (matches.Length != 1 || StringProperty(matches[0], "vectorType") != vectorType ||
            LongProperty(matches[0], "featureCount") != 1)
            throw new InvalidDataException($"The OpenFileGDB catalog did not return the expected {vectorType} layer '{name}'.");
    }

    private static JsonElement FindGdbVectorNode(JsonElement[] nodes, string sourcePath, string layerName)
    {
        JsonElement[] matches = nodes.Where(node =>
        {
            JsonElement locator = PropertyOrNull(node, "locator");
            return locator.ValueKind == JsonValueKind.Object &&
                StringProperty(locator, "nodeType") == "GdbVectorLayer" &&
                StringProperty(locator, "mapType") == "GdbVectorLayer" &&
                StringProperty(locator, "nodeName") == layerName &&
                PathEquals(OptionalString(locator, "assetID") ?? "", sourcePath);
        }).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"The saved map tree must contain one '{layerName}' GDB vector leaf; found {matches.Length}.");
        if (StringProperty(matches[0], "checked") != "checked")
            throw new InvalidDataException($"The saved GDB vector leaf '{layerName}' is not checked.");
        return matches[0];
    }

    private static bool AssertLoadedSource(JsonElement result, GdbMapSourceIdentity expectedSource,
        string layerName, string fieldName, string fieldValue, string geometryType)
    {
        AssertManifest(Property(result, "source"), expectedSource);
        JsonElement layer = Property(result, "layer");
        if (StringProperty(layer, "name") != layerName ||
            StringProperty(layer, "vectorType") != geometryType ||
            LongProperty(layer, "featureCount") != 1)
            throw new InvalidDataException($"The persisted source read returned a different '{layerName}' layer identity.");
        JsonElement data = Property(result, "data");
        if (StringProperty(data, "geometryType") != geometryType || IntProperty(data, "count") != 1)
            throw new InvalidDataException($"The persisted source read did not return one {geometryType} feature.");
        JsonElement[] features = Property(data, "features").EnumerateArray().ToArray();
        if (features.Length != 1)
            throw new InvalidDataException("The GDB source read returned an unexpected feature count.");
        JsonElement feature = features[0];
        JsonElement[] attributes = Property(feature, "attributes").EnumerateArray().ToArray();
        JsonElement[] matchingAttributes = attributes.Where(attribute =>
            StringProperty(attribute, "columnName") == fieldName &&
            OptionalString(attribute, "value") == fieldValue).ToArray();
        if (matchingAttributes.Length != 1)
            throw new InvalidDataException($"The source read did not preserve Chinese field '{fieldName}' and its value.");

        JsonElement geometry = Property(feature, "geometry");
        if (StringProperty(geometry, "geometryType") != geometryType)
            throw new InvalidDataException("The GDB source read returned a different feature geometry type.");
        JsonElement[] vertices = Property(geometry, "vertices").EnumerateArray().ToArray();
        if (geometryType == "Point")
        {
            if (vertices.Length != 1 ||
                Math.Abs(NumberProperty(vertices[0], "sourceX") - 1.5) > 1e-8 ||
                Math.Abs(NumberProperty(vertices[0], "sourceY") - 2.5) > 1e-8)
                throw new InvalidDataException("The GDB source read did not preserve the point's actual coordinates.");
        }
        else if (vertices.Length != 0)
        {
            throw new InvalidDataException("The polygon feature unexpectedly returned flat vertices instead of rings.");
        }
        return true;
    }

    private static bool AssertInteriorRing(JsonElement result)
    {
        JsonElement collection = Property(Property(Property(result, "data"), "features").EnumerateArray().Single(), "geometry");
        if (StringProperty(collection, "geometryType") != "PolygonCollection")
            throw new InvalidDataException("The OpenFileGDB multipolygon was not retained as a polygon collection.");
        var polygons = new List<JsonElement>();
        void Visit(JsonElement geometry)
        {
            if (StringProperty(geometry, "geometryType") == "Polygon")
            {
                polygons.Add(geometry);
                return;
            }
            JsonElement children = PropertyOrNull(geometry, "children");
            if (children.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in children.EnumerateArray()) Visit(child);
        }
        Visit(collection);
        if (polygons.Count != 1)
            throw new InvalidDataException("The GDB polygon collection must contain exactly one polygon child.");
        JsonElement[] rings = Property(polygons[0], "rings").EnumerateArray().ToArray();
        if (rings.Length != 2 || StringProperty(rings[0], "role") != "exterior" ||
            StringProperty(rings[1], "role") != "interior")
            throw new InvalidDataException("The OpenFileGDB polygon load did not preserve exterior and interior ring roles.");
        AssertClosedRing(rings[0], 0, 0, 10, 10);
        AssertClosedRing(rings[1], 2, 2, 4, 4);
        return true;
    }

    private static void AssertClosedRing(JsonElement ring, double minX, double minY, double maxX, double maxY)
    {
        JsonElement[] vertices = Property(ring, "vertices").EnumerateArray().ToArray();
        if (vertices.Length != 5 ||
            Math.Abs(NumberProperty(vertices[0], "sourceX") - NumberProperty(vertices[^1], "sourceX")) > 1e-8 ||
            Math.Abs(NumberProperty(vertices[0], "sourceY") - NumberProperty(vertices[^1], "sourceY")) > 1e-8 ||
            Math.Abs(vertices.Min(vertex => NumberProperty(vertex, "sourceX")) - minX) > 1e-8 ||
            Math.Abs(vertices.Min(vertex => NumberProperty(vertex, "sourceY")) - minY) > 1e-8 ||
            Math.Abs(vertices.Max(vertex => NumberProperty(vertex, "sourceX")) - maxX) > 1e-8 ||
            Math.Abs(vertices.Max(vertex => NumberProperty(vertex, "sourceY")) - maxY) > 1e-8)
            throw new InvalidDataException("A GDB polygon ring did not preserve its closed source coordinates.");
    }

    private static async Task ExpectSourceChangedAsync(HttpClient client, string method, object parameters,
        CancellationToken cancellation, string staleIdentity)
    {
        using HttpResponseMessage accepted = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        if (accepted.StatusCode != HttpStatusCode.Accepted)
        {
            string body = await accepted.Content.ReadAsStringAsync(cancellation);
            throw new InvalidDataException($"The stale {staleIdentity} check was not accepted as a worker operation: {body}");
        }
        using JsonDocument submission = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(cancellation));
        string id = StringProperty(submission.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument snapshot = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = snapshot.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (BooleanPropertyOrDefault(root, "hasMore")) continue;
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
            if (BooleanPropertyOrDefault(root, "hasMore")) continue;
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

    private static JsonElement[] CollectTreeNodes(JsonElement roots)
    {
        if (roots.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The saved GDB map tree omitted its roots array.");
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

    private static void AssertDiskRevision(string path, string revision, string message)
    {
        if (!FileSha256(path).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    private static void AssertFileRevision(byte[] bytes, string revision, string message)
    {
        if (!Sha256(bytes).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(message);
    }

    private static string FileSha256(string path) => Sha256(File.ReadAllBytes(path));
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static Uri EnsureTrailingSlash(Uri value) => value.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
        ? value : new Uri(value.AbsoluteUri + "/", UriKind.Absolute);
    private static bool IsTerminal(string state) =>
        state.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("succeeded", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("canceled", StringComparison.OrdinalIgnoreCase);

    private static string StringProperty(JsonElement value, string name) =>
        Property(value, name).GetString() ??
        throw new InvalidDataException($"JSON property '{name}' is null.");
    private static string? OptionalString(JsonElement value, string name)
    {
        JsonElement property = PropertyOrNull(value, name);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
    private static bool BooleanProperty(JsonElement value, string name) => Property(value, name).GetBoolean();
    private static bool BooleanPropertyOrDefault(JsonElement value, string name) =>
        PropertyOrNull(value, name) is { ValueKind: JsonValueKind.True };
    private static int IntProperty(JsonElement value, string name) => Property(value, name).GetInt32();
    private static long LongProperty(JsonElement value, string name) => Property(value, name).GetInt64();
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

    private sealed record OperationReceipt(string Id, string State, JsonElement Result);
}

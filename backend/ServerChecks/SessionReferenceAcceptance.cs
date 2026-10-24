using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Ras.Backend.ServerChecks;

internal sealed record SessionReferenceAcceptanceReport(
    string State, string ManifestPath, string TargetProjectPath, string ProjectXml, string ProjectRevision,
    string ProjectXmlRevision, Guid ProjectSessionID, int MapIndex, string MapName, string MapType,
    string VisualizationRevision, SessionReferenceAcceptanceSource[] ExternalSources,
    string[] ReferenceKinds, bool Composed, bool MapAndCatalogReadBack,
    bool CrossSessionRejected, bool DuplicateSourceRejected, bool StaleSourceRejected,
    bool TerrainSaveRebasedAndReloaded, bool GeometryMapSessionHdfReadBack,
    bool ProjectSaveRebased, bool OldProjectContextRejected, bool OldContextRejected,
    bool OriginalSourcesUnchanged);

internal sealed record SessionReferenceAcceptanceSource(string Kind, string Path, string SourceRevision);

internal static partial class ServerAcceptance
{
    private sealed record SessionReferenceOutcome(string State, JsonElement Result, JsonElement Error);

    public static async Task<SessionReferenceAcceptanceReport> RunSessionReferenceAcceptanceAsync(
        Uri baseAddress, string resultHoverManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback || baseAddress.Scheme is not ("http" or "https"))
            throw new ArgumentException("Session-reference acceptance requires a loopback HTTP(S) server.", nameof(baseAddress));

        ResultHoverManifest source = JsonSerializer.Deserialize<ResultHoverManifest>(
            await File.ReadAllTextAsync(Path.GetFullPath(resultHoverManifestPath)), JsonOptions)
            ?? throw new InvalidDataException("The Result-hover manifest is empty.");
        if (source.Schema != "hecras-result-hover-e2e/v1" || source.Sources.Length == 0 ||
            !File.Exists(source.ProjectPath) || !File.Exists(source.PlanPath) || !File.Exists(source.Sources[0].ResultPath))
            throw new InvalidDataException("This acceptance requires a completed fixture with real Plan and Result HDF files.");

        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory) || Directory.Exists(outputDirectory))
            throw new IOException("The acceptance output directory must be new; existing data is never overwritten.");
        Directory.CreateDirectory(outputDirectory);
        string externalRoot = Path.Combine(outputDirectory, "external-reference-sources");
        Directory.CreateDirectory(externalRoot);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (!StringProperty(health.RootElement, "status").Equals("ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The C# server did not report ready.");

        // Read source identities from the existing completed fixture without changing it.
        JsonElement sourceProject = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = Path.GetFullPath(source.ProjectPath) }, cancellation)).Result;
        string sourceXml = StringProperty(sourceProject, "projectXml");
        JsonElement sourceCatalog = (await SubmitOperationAsync(client, "catalog.tree.load", new
        {
            projectPath = Path.GetFullPath(source.ProjectPath), projectXml = sourceXml, mode = "Project"
        }, cancellation)).Result;
        JsonElement[] sourceNodes = SessionReferenceFlatten(Property(sourceCatalog, "roots"));
        string projectPlan = SessionReferenceAsset(sourceNodes, "ObservablePlan", source.PlanPath);
        if (!PathEquals(projectPlan, source.PlanPath))
            throw new InvalidDataException("The source catalog does not resolve the completed fixture Plan.");
        string projectGeometry = SessionReferenceAsset(sourceNodes, "ObservableGeometry");
        string projectBoundary = SessionReferenceAsset(sourceNodes, "ObservableBoundaryCondition");
        string projectTerrain = SessionReferenceAsset(sourceNodes, "ObservableTerrain");
        string projectResult = Path.GetFullPath(source.Sources[0].ResultPath);
        string[] originalPaths = [projectPlan, projectResult, projectGeometry, projectBoundary, projectTerrain,
            Path.GetFullPath(source.ProjectPath)];
        Dictionary<string, string> originalRevisions = originalPaths.Distinct(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToDictionary(path => path, FileSha256,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        string CopyExternal(string kind, string path)
        {
            string target = Path.GetFullPath(Path.Combine(externalRoot, kind + "-" + Path.GetFileName(path)));
            if (File.Exists(target)) throw new IOException("A copied external source would overwrite an existing file.");
            File.Copy(path, target, overwrite: false);
            return target;
        }
        string externalPlan = CopyExternal("plan", projectPlan);
        string externalResult = CopyExternal("result", projectResult);
        string externalGeometry = CopyExternal("geometry", projectGeometry);
        string externalBoundary = CopyExternal("boundary", projectBoundary);
        string externalTerrain = CopyExternal("terrain", projectTerrain);
        // Keep the external Terrain self-contained: HDF inputs resolve relative
        // paths before their retained absolute fallback paths.
        foreach (string dependency in Directory.EnumerateFiles(Path.GetDirectoryName(projectTerrain)!))
        {
            string name = Path.GetFileName(dependency);
            if (!(name.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".aux.xml", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".ovr", StringComparison.OrdinalIgnoreCase))) continue;
            originalRevisions[dependency] = FileSha256(dependency);
            File.Copy(dependency, Path.Combine(externalRoot, name), overwrite: false);
        }
        string Revision(string path) => FileSha256(path);

        // A new Host-created empty project avoids same-name ambiguity with the
        // project-owned source HDFs while preserving each external HDF byte-for-byte.
        string projectTitle = "SessionReference-" + Guid.NewGuid().ToString("N")[..8];
        JsonElement createdProject = await SessionReferenceRequireSuccessAsync(client, "project.create", new
        {
            projectTitle,
            description = "Session-reference HTTP acceptance fixture",
            createDirectory = outputDirectory,
            isUSC = false,
            isLocalProj = true,
            isEPSG = false,
            isWktProj = false,
            isPRJFile = false,
            projectionText = "",
            projectionDefinition = (object?)null
        }, cancellation);
        string projectPath = StringProperty(createdProject, "projectPath");
        // The retained serializer normally emits an indented .ras XML file.
        // Compact this disposable acceptance project before its initial open
        // so project.save has a deterministic serialization-only SHA change;
        // no user data or original fixture is touched.
        string compactProjectXml = XElement.Parse(await File.ReadAllTextAsync(projectPath, cancellation))
            .ToString(SaveOptions.DisableFormatting);
        await File.WriteAllTextAsync(projectPath, compactProjectXml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellation);
        JsonElement openedProject = (await SubmitOperationAsync(client, "project.open", new { projectPath }, cancellation)).Result;
        string projectXml = StringProperty(openedProject, "projectXml");
        string projectRevision = StringProperty(openedProject, "revision");
        string projectXmlRevision = Sha256(Encoding.UTF8.GetBytes(projectXml));
        if (!PathEquals(StringProperty(openedProject, "projectPath"), projectPath) ||
            !FileSha256(projectPath).Equals(projectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Guid sessionID = Guid.NewGuid();
        JsonElement? context = null;
        async Task<JsonElement> InspectHdf(string path, string expectedKind)
        {
            JsonElement inspection = await SessionReferenceRequireSuccessAsync(client, "workspace.import.sessionHdf.inspect", new
            {
                projectPath, projectSessionID = sessionID.ToString("D"), projectRevision, projectXml,
                projectXmlRevision, sourcePath = path, sourceRevision = Revision(path), coreLayerContext = context
            }, cancellation);
            JsonElement capture = Property(inspection, "reference");
            if (StringProperty(capture, "kind") != expectedKind ||
                !PathEquals(StringProperty(capture, "sourcePath"), path) ||
                !StringProperty(capture, "sourceRevision").Equals(Revision(path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The {expectedKind} inspect returned a different source capture.");
            return capture.Clone();
        }
        async Task<JsonElement> InspectAndAddCore(string path, string expectedKind)
        {
            object request = new
            {
                projectPath, projectSessionID = sessionID.ToString("D"), projectRevision, projectXml,
                projectXmlRevision, sourcePath = path, sourceRevision = Revision(path), coreLayerContext = context
            };
            JsonElement inspection = await SessionReferenceRequireSuccessAsync(client, "workspace.import.coreLayer.inspect", request, cancellation);
            if (StringProperty(inspection, "kind") != expectedKind ||
                !Property(inspection, "availableActions").EnumerateArray().Any(item => item.GetString() == "addReference"))
                throw new InvalidDataException($"The {expectedKind} inspect did not expose Add Reference.");
            JsonElement receipt = await SessionReferenceRequireSuccessAsync(client, "workspace.import.coreLayer.perform", new
            {
                projectPath, projectSessionID = sessionID.ToString("D"), projectRevision, projectXml,
                projectXmlRevision, sourcePath = path, sourceRevision = Revision(path),
                action = "addReference", coreLayerContext = context
            }, cancellation);
            JsonElement capture = Property(receipt, "reference");
            if (StringProperty(capture, "kind") != expectedKind ||
                !PathEquals(StringProperty(capture, "sourcePath"), path) ||
                !StringProperty(capture, "sourceRevision").Equals(Revision(path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The {expectedKind} Add Reference receipt changed source identity.");
            return capture.Clone();
        }

        JsonElement planCapture = await InspectHdf(externalPlan, "plan");
        JsonElement resultCapture = await InspectHdf(externalResult, "result");
        JsonElement terrainCapture = await InspectHdf(externalTerrain, "terrain");
        JsonElement geometryCapture = await InspectAndAddCore(externalGeometry, "geometry");
        JsonElement boundaryCapture = await InspectAndAddCore(externalBoundary, "boundaryCondition");
        JsonElement compose = await SessionReferenceRequireSuccessAsync(client, "workspace.references.compose", new
        {
            projectPath, projectSessionID = sessionID.ToString("D"), projectRevision, projectXml, projectXmlRevision,
            coreLayerContext = (JsonElement?)null,
            addCoreReferences = new[] { geometryCapture, boundaryCapture },
            addSessionHdfReferences = new[] { planCapture, resultCapture, terrainCapture }
        }, cancellation);
        context = Property(compose, "nextContext").Clone();
        JsonElement activeContext = context.Value;
        VerifySessionReferenceContext(activeContext, sessionID, projectPath, projectRevision,
            [externalGeometry, externalBoundary], [externalPlan, externalResult, externalTerrain]);

        bool crossSessionRejected = await SessionReferenceExpectRejectedAsync(client, "workspace.references.compose", new
        {
            projectPath, projectSessionID = Guid.NewGuid().ToString("D"), projectRevision, projectXml, projectXmlRevision,
            coreLayerContext = activeContext, addCoreReferences = Array.Empty<JsonElement>(),
            addSessionHdfReferences = Array.Empty<JsonElement>()
        }, cancellation);

        string aliasPath = Path.Combine(externalRoot, "duplicate-plan-alias.hdf");
        File.CreateSymbolicLink(aliasPath, externalPlan);
        JsonElement aliasCapture = await InspectHdf(aliasPath, "plan");
        JsonElement existingPlan = Property(activeContext, "sessionHdfReferences").EnumerateArray()
            .Single(item => StringProperty(item, "kind") == "plan");
        bool duplicateRejected = StringProperty(aliasCapture, "canonicalSourcePath")
                .Equals(StringProperty(existingPlan, "canonicalSourcePath"),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            await SessionReferenceExpectRejectedAsync(client, "workspace.references.compose", new
            {
                projectPath, projectSessionID = sessionID.ToString("D"), projectRevision, projectXml, projectXmlRevision,
                coreLayerContext = activeContext, addSessionHdfReferences = new[] { aliasCapture },
                removeReferenceIDs = Array.Empty<string>()
            }, cancellation);

        string stalePath = Path.Combine(externalRoot, "stale-terrain-capture.hdf");
        File.Copy(externalTerrain, stalePath, overwrite: false);
        JsonElement staleCapture = await InspectHdf(stalePath, "terrain");
        await using (var stream = new FileStream(stalePath, FileMode.Append, FileAccess.Write, FileShare.None))
            await stream.WriteAsync(new byte[] { 0xA5 }, cancellation);
        bool staleRejected = await SessionReferenceExpectRejectedAsync(client, "workspace.references.compose", new
        {
            projectPath, projectSessionID = sessionID.ToString("D"), projectRevision, projectXml, projectXmlRevision,
            coreLayerContext = activeContext, addSessionHdfReferences = new[] { staleCapture },
            removeReferenceIDs = Array.Empty<string>()
        }, cancellation);
        if (!crossSessionRejected || !duplicateRejected || !staleRejected)
            throw new InvalidDataException("Compose accepted a cross-session context, duplicate canonical source, or stale source hash.");

        JsonElement mapList = await SessionReferenceRequireSuccessAsync(client, "map.document.list", new
        {
            projectPath, projectXml, projectXmlSHA = projectXmlRevision, expectedProjectRevision = projectRevision
        }, cancellation);
        string mapName = "会话引用验收地图 " + sessionID.ToString("N")[..8];
        JsonElement created = await SessionReferenceRequireSuccessAsync(client, "map.document.create", new
        {
            projectPath, projectXml, projectXmlSHA = projectXmlRevision,
            expectedProjectRevision = projectRevision,
            expectedVisualizationRevision = OptionalString(mapList, "visualizationRevision"),
            coreLayerContext = activeContext, kind = "Plan", name = mapName,
            initialPlanPath = externalPlan, initialPlanRevision = Revision(externalPlan)
        }, cancellation);
        int mapIndex = IntProperty(created, "mapIndex");
        string savedMapName = StringProperty(created, "mapName");
        string mapType = StringProperty(created, "mapType");
        string visualizationRevision = StringProperty(created, "visualizationRevision");
        if (mapType != "Plan" || savedMapName != mapName ||
            OptionalString(created, "previousVisualizationRevision") != OptionalString(mapList, "visualizationRevision"))
            throw new InvalidDataException("The Plan map create receipt did not match its saved visualization owner.");
        string mapID = "map-" + mapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        object MapRequest(string expectedRevision) => new
        {
            projectPath, projectXml, projectXmlSHA = projectXmlRevision, mapID, mapIndex,
            mapName = savedMapName, mapType, expectedVisualizationRevision = expectedRevision,
            projectSessionID = sessionID.ToString("D"), coreLayerContext = context
        };
        JsonElement initialTree = await SessionReferenceRequireSuccessAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation);
        VerifySessionMapRevision(initialTree, visualizationRevision);
        async Task AddToMap(string action, string path)
        {
            JsonElement receipt = await SessionReferenceRequireSuccessAsync(client, "map.tree.perform", new
            {
                map = MapRequest(visualizationRevision), action, sourcePath = path, sourceRevision = Revision(path)
            }, cancellation);
            VerifySessionMapRevision(receipt, visualizationRevision);
            visualizationRevision = StringProperty(receipt, "visualizationRevision");
        }
        await AddToMap("addGeometry", externalGeometry);
        await AddToMap("addBoundaryCondition", externalBoundary);
        await AddToMap("addSessionHdf", externalTerrain);
        JsonElement freshTree = await SessionReferenceRequireSuccessAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation);
        VerifySessionMapRevision(freshTree, visualizationRevision);
        JsonElement freshCatalog = await SessionReferenceRequireSuccessAsync(client, "catalog.tree.load", new
        {
            projectPath, projectXml, mode = "Project",
            projectSessionID = sessionID.ToString("D"), coreLayerContext = activeContext
        }, cancellation);
        string[] allExternal = [externalPlan, externalResult, externalGeometry, externalBoundary, externalTerrain];
        bool mapAndCatalogReadBack = new[] { externalPlan, externalGeometry, externalBoundary, externalTerrain }
            .All(path => SessionReferenceHasAsset(Property(freshTree, "roots"), path)) &&
            allExternal.All(path => SessionReferenceHasAsset(Property(freshCatalog, "roots"), path));
        if (!mapAndCatalogReadBack)
            throw new InvalidDataException("Fresh map tree/catalog readback omitted one of the captured sources.");

        string oldTerrainRevision = Revision(externalTerrain);
        JsonElement loadedTerrain = await SessionReferenceRequireSuccessAsync(client, "model.load", new
        {
            assetPath = externalTerrain, kind = "Terrain", projectPath, projectXml, coreLayerContext = activeContext
        }, cancellation);
        if (!StringProperty(loadedTerrain, "revision").Equals(oldTerrainRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        const string fieldKey = "TerrainCatalogSummary/Context_Description";
        const string description = "External Terrain session reference acceptance";
        JsonElement savedTerrain = await SessionReferenceRequireSuccessAsync(client, "model.save", new
        {
            assetPath = externalTerrain, kind = "Terrain", projectPath, revision = oldTerrainRevision,
            fields = new Dictionary<string, string> { [fieldKey] = description },
            images = new Dictionary<string, object[]>(), projectXml,
            sourceState = OptionalString(loadedTerrain, "sourceState"), coreLayerContext = activeContext
        }, cancellation);
        string newTerrainRevision = Revision(externalTerrain);
        JsonElement rebasedContext = Property(savedTerrain, "coreLayerContext").Clone();
        JsonElement rebasedTerrain = Property(rebasedContext, "sessionHdfReferences").EnumerateArray()
            .Single(item => StringProperty(item, "kind") == "terrain" &&
                PathEquals(StringProperty(item, "sourcePath"), externalTerrain));
        if (newTerrainRevision.Equals(oldTerrainRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(savedTerrain, "revision").Equals(newTerrainRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(rebasedTerrain, "sourceRevision").Equals(newTerrainRevision, StringComparison.OrdinalIgnoreCase) ||
            StringProperty(rebasedContext, "identityToken").Equals(StringProperty(activeContext, "identityToken"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Terrain save did not return a source-revision-rebased context.");
        JsonElement reloadedTerrain = await SessionReferenceRequireSuccessAsync(client, "model.load", new
        {
            assetPath = externalTerrain, kind = "Terrain", projectPath, projectXml, coreLayerContext = rebasedContext
        }, cancellation);
        if (!StringProperty(reloadedTerrain, "revision").Equals(newTerrainRevision, StringComparison.OrdinalIgnoreCase) ||
            !Property(reloadedTerrain, "fields").EnumerateObject().Any(pair =>
                pair.Name == fieldKey && pair.Value.GetString() == description))
            throw new InvalidDataException("A fresh Terrain load with the rebased context did not read back the saved field.");
        bool oldContextRejected = await SessionReferenceExpectRejectedAsync(client, "model.load", new
        {
            assetPath = externalTerrain, kind = "Terrain", projectPath, projectXml, coreLayerContext = activeContext
        }, cancellation);
        if (!oldContextRejected)
            throw new InvalidDataException("model.load accepted the pre-save Terrain context after the HDF revision changed.");

        context = rebasedContext.Clone();
        // Geometry maps serialize standalone Plan and Terrain membership under
        // their exact Layers root. Exercise both session-only HDF captures
        // against the same verified parent Geometry and current .vis revision.
        string geometryMapName = "会话引用几何地图 " + sessionID.ToString("N")[..8];
        JsonElement geometryMapReceipt = await SessionReferenceRequireSuccessAsync(client, "map.document.create", new
        {
            projectPath, projectXml, projectXmlSHA = projectXmlRevision,
            expectedProjectRevision = projectRevision,
            expectedVisualizationRevision = visualizationRevision,
            coreLayerContext = rebasedContext, kind = "Geometry", name = geometryMapName,
            initialGeometryPath = externalGeometry, initialGeometryRevision = Revision(externalGeometry)
        }, cancellation);
        VerifySessionMapRevision(geometryMapReceipt, visualizationRevision);
        int geometryMapIndex = IntProperty(geometryMapReceipt, "mapIndex");
        geometryMapName = StringProperty(geometryMapReceipt, "mapName");
        if (StringProperty(geometryMapReceipt, "mapType") != "Geometry")
            throw new InvalidDataException("The Geometry map create receipt returned a different map type.");
        visualizationRevision = StringProperty(geometryMapReceipt, "visualizationRevision");
        string geometryMapID = "map-" + geometryMapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        object GeometryMapRequest(string expectedRevision) => new
        {
            projectPath, projectXml, projectXmlSHA = projectXmlRevision,
            mapID = geometryMapID, mapIndex = geometryMapIndex, mapName = geometryMapName,
            mapType = "Geometry", expectedVisualizationRevision = expectedRevision,
            projectSessionID = sessionID.ToString("D"), coreLayerContext = context
        };
        JsonElement geometryMapTree = await SessionReferenceRequireSuccessAsync(client, "map.tree.load",
            GeometryMapRequest(visualizationRevision), cancellation);
        VerifySessionMapRevision(geometryMapTree, visualizationRevision);
        async Task AddSessionHdfToGeometryMap(string sourcePath)
        {
            string previous = visualizationRevision;
            JsonElement receipt = await SessionReferenceRequireSuccessAsync(client, "map.tree.perform", new
            {
                map = GeometryMapRequest(previous), action = "addSessionHdf", sourcePath,
                sourceRevision = Revision(sourcePath)
            }, cancellation);
            VerifySessionMapRevision(receipt, previous);
            visualizationRevision = StringProperty(receipt, "visualizationRevision");
        }
        await AddSessionHdfToGeometryMap(externalPlan);
        await AddSessionHdfToGeometryMap(externalTerrain);
        JsonElement savedGeometryTree = await SessionReferenceRequireSuccessAsync(client, "map.tree.load",
            GeometryMapRequest(visualizationRevision), cancellation);
        VerifySessionMapRevision(savedGeometryTree, visualizationRevision);
        bool geometryMapSessionHdfReadBack =
            SessionReferenceHasUniqueAssetUnderRoot(Property(savedGeometryTree, "roots"), "Layers", externalPlan, "Plan") &&
            SessionReferenceHasUniqueAssetUnderRoot(Property(savedGeometryTree, "roots"), "Layers", externalTerrain, "Terrain");
        if (!geometryMapSessionHdfReadBack)
            throw new InvalidDataException("The Geometry map did not read each external Plan and Terrain exactly once from its Layers root.");

        // Saving the project changes its source SHA. The acknowledgement must
        // bind the live session capture to that new revision before any final
        // tree/catalog reads use it.
        JsonElement savedProject = await SessionReferenceRequireSuccessAsync(client, "project.save", new
        {
            projectPath, projectXml, projectXmlRevision, projectSessionID = sessionID.ToString("D"),
            revision = projectRevision, coreLayerContext = rebasedContext
        }, cancellation);
        string savedProjectPath = StringProperty(savedProject, "projectPath");
        string savedProjectXml = StringProperty(savedProject, "projectXml");
        string savedProjectRevision = StringProperty(savedProject, "revision");
        string savedProjectXmlRevision = Sha256(Encoding.UTF8.GetBytes(savedProjectXml));
        JsonElement projectSaveContext = Property(savedProject, "coreLayerContext").Clone();
        if (!PathEquals(savedProjectPath, projectPath) ||
            savedProjectRevision.Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            !FileSha256(projectPath).Equals(savedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(projectSaveContext, "projectRevision").Equals(savedProjectRevision, StringComparison.OrdinalIgnoreCase) ||
            !StringProperty(projectSaveContext, "projectSessionID").Equals(sessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            StringProperty(projectSaveContext, "identityToken").Equals(StringProperty(rebasedContext, "identityToken"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("project.save did not change the .ras SHA and return a rebased project-session context.");
        VerifySessionReferenceContext(projectSaveContext, sessionID, projectPath, savedProjectRevision,
            [externalGeometry, externalBoundary], [externalPlan, externalResult, externalTerrain]);
        VerifySessionReferenceCapturesPreserved(rebasedContext, projectSaveContext);
        bool oldProjectContextRejected = await SessionReferenceExpectRejectedAsync(client, "catalog.tree.load", new
        {
            projectPath = savedProjectPath, projectXml = savedProjectXml, mode = "Project",
            projectSessionID = sessionID.ToString("D"), coreLayerContext = rebasedContext
        }, cancellation);
        if (!oldProjectContextRejected)
            throw new InvalidDataException("catalog.tree.load accepted the pre-project-save context after the .ras SHA changed.");

        projectPath = savedProjectPath;
        projectXml = savedProjectXml;
        projectXmlRevision = savedProjectXmlRevision;
        projectRevision = savedProjectRevision;
        context = projectSaveContext.Clone();
        JsonElement finalTree = await SessionReferenceRequireSuccessAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation);
        JsonElement finalGeometryTree = await SessionReferenceRequireSuccessAsync(client, "map.tree.load",
            GeometryMapRequest(visualizationRevision), cancellation);
        JsonElement finalCatalog = await SessionReferenceRequireSuccessAsync(client, "catalog.tree.load", new
        {
            projectPath, projectXml, mode = "Project",
            projectSessionID = sessionID.ToString("D"), coreLayerContext = projectSaveContext
        }, cancellation);
        JsonElement finalProjectTree = await SessionReferenceRequireSuccessAsync(client, "project.tree.load", new
        {
            projectPath, projectXml, mode = "Project",
            projectSessionID = sessionID.ToString("D"), coreLayerContext = projectSaveContext
        }, cancellation);
        VerifySessionMapRevision(finalTree, visualizationRevision);
        VerifySessionMapRevision(finalGeometryTree, visualizationRevision);
        mapAndCatalogReadBack = mapAndCatalogReadBack &&
            new[] { externalPlan, externalGeometry, externalBoundary, externalTerrain }
                .All(path => SessionReferenceHasAsset(Property(finalTree, "roots"), path)) &&
            allExternal.All(path => SessionReferenceHasAsset(Property(finalCatalog, "roots"), path)) &&
            allExternal.All(path => SessionReferenceHasAsset(Property(finalProjectTree, "roots"), path)) &&
            SessionReferenceHasUniqueAssetUnderRoot(Property(finalGeometryTree, "roots"), "Layers", externalPlan, "Plan") &&
            SessionReferenceHasUniqueAssetUnderRoot(Property(finalGeometryTree, "roots"), "Layers", externalTerrain, "Terrain");
        if (new[] { externalPlan, externalGeometry, externalBoundary, externalTerrain }
                .Any(path => !SessionReferenceHasAsset(Property(finalTree, "roots"), path)) ||
            allExternal.Any(path => !SessionReferenceHasAsset(Property(finalCatalog, "roots"), path)) ||
            allExternal.Any(path => !SessionReferenceHasAsset(Property(finalProjectTree, "roots"), path)) ||
            !SessionReferenceHasUniqueAssetUnderRoot(Property(finalGeometryTree, "roots"), "Layers", externalPlan, "Plan") ||
            !SessionReferenceHasUniqueAssetUnderRoot(Property(finalGeometryTree, "roots"), "Layers", externalTerrain, "Terrain"))
            throw new InvalidDataException("Fresh map/catalog readback lost a reference after the Terrain or project save.");

        bool originalUnchanged = originalRevisions.All(pair =>
            File.Exists(pair.Key) && FileSha256(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase));
        if (!originalUnchanged)
            throw new InvalidDataException("The acceptance changed bytes in the original source fixture.");
        string reportPath = Path.Combine(outputDirectory, "session-reference-acceptance.json");
        var externalSources = new[]
        {
            new SessionReferenceAcceptanceSource("plan", externalPlan, Revision(externalPlan)),
            new SessionReferenceAcceptanceSource("result", externalResult, Revision(externalResult)),
            new SessionReferenceAcceptanceSource("geometry", externalGeometry, Revision(externalGeometry)),
            new SessionReferenceAcceptanceSource("boundaryCondition", externalBoundary, Revision(externalBoundary)),
            new SessionReferenceAcceptanceSource("terrain", externalTerrain, newTerrainRevision)
        };
        var report = new SessionReferenceAcceptanceReport("completed", reportPath, projectPath, projectXml,
            projectRevision, projectXmlRevision, sessionID, mapIndex, savedMapName, mapType,
            visualizationRevision, externalSources,
            ["plan", "result", "geometry", "boundaryCondition", "terrain"],
            true, mapAndCatalogReadBack, crossSessionRejected, duplicateRejected, staleRejected,
            true, geometryMapSessionHdfReadBack, true, oldProjectContextRejected, oldContextRejected, originalUnchanged);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellation);
        return report;
    }

    private static void VerifySessionReferenceContext(JsonElement context, Guid sessionID, string projectPath,
        string projectRevision, string[] corePaths, string[] hdfPaths)
    {
        if (!StringProperty(context, "projectSessionID").Equals(sessionID.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            !PathEquals(StringProperty(context, "projectPath"), projectPath) ||
            !StringProperty(context, "projectRevision").Equals(projectRevision, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(StringProperty(context, "identityToken")))
            throw new InvalidDataException("The compose receipt lost its session/project identity.");
        JsonElement[] core = Property(context, "references").EnumerateArray().ToArray();
        JsonElement[] hdf = Property(context, "sessionHdfReferences").EnumerateArray().ToArray();
        if (core.Length != corePaths.Length || hdf.Length != hdfPaths.Length ||
            corePaths.Any(path => !core.Any(item => PathEquals(StringProperty(item, "sourcePath"), path))) ||
            hdfPaths.Any(path => !hdf.Any(item => PathEquals(StringProperty(item, "sourcePath"), path))))
            throw new InvalidDataException("The compose receipt omitted one or more of the five source captures.");
    }

    private static string SessionReferenceAsset(JsonElement[] nodes, string sourceClass, string? expectedPath = null)
    {
        string[] matches = nodes.Where(node => StringProperty(node, "sourceClass") == sourceClass &&
                OptionalString(node, "assetID") is { Length: > 0 } &&
                (expectedPath is null || PathEquals(StringProperty(node, "assetID"), expectedPath)))
            .Select(node => Path.GetFullPath(StringProperty(node, "assetID")))
            .Where(path => File.Exists(path) && (expectedPath is null || PathEquals(path, expectedPath)))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected one unique real {sourceClass} source, found {matches.Length}.");
        return matches[0];
    }

    private static JsonElement[] SessionReferenceFlatten(JsonElement roots)
    {
        if (roots.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The tree omitted its roots array.");
        var nodes = new List<JsonElement>();
        void Visit(JsonElement node)
        {
            nodes.Add(node.Clone());
            // Embedded Result snapshots are not separate project-owned source HDFs.
            if (OptionalString(node, "sourceClass") == "ObservableResult") return;
            JsonElement children = PropertyOrNull(node, "children");
            if (children.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in children.EnumerateArray()) Visit(child);
        }
        foreach (JsonElement root in roots.EnumerateArray()) Visit(root);
        return nodes.ToArray();
    }

    private static bool SessionReferenceHasAsset(JsonElement roots, string path) =>
        SessionReferenceFlatten(roots).Any(node => OptionalString(node, "assetID") is { Length: > 0 } asset &&
            PathEquals(asset, path));

    private static bool SessionReferenceHasUniqueAssetUnderRoot(JsonElement roots, string rootName,
        string path, string nodeType)
    {
        JsonElement[] namedRoots = roots.ValueKind == JsonValueKind.Array
            ? roots.EnumerateArray().Where(node => OptionalString(node, "name") == rootName).ToArray()
            : [];
        if (namedRoots.Length != 1) return false;
        JsonElement[] matches = SessionReferenceFlatten(roots).Where(node =>
            OptionalString(node, "assetID") is { Length: > 0 } asset && PathEquals(asset, path)).ToArray();
        if (matches.Length != 1) return false;
        JsonElement locator = PropertyOrNull(matches[0], "locator");
        return locator.ValueKind == JsonValueKind.Object &&
            OptionalString(locator, "rootName") == rootName &&
            OptionalString(locator, "nodeType") == nodeType;
    }

    private static void VerifySessionReferenceCapturesPreserved(JsonElement before, JsonElement after)
    {
        foreach (string collectionName in new[] { "references", "sessionHdfReferences" })
        {
            JsonElement[] previous = Property(before, collectionName).EnumerateArray().ToArray();
            JsonElement[] current = Property(after, collectionName).EnumerateArray().ToArray();
            if (previous.Length != current.Length)
                throw new InvalidDataException("project.save changed the session's captured source set.");
            foreach (JsonElement capture in previous)
            {
                JsonElement[] matches = current.Where(candidate =>
                    OptionalString(candidate, "kind") == OptionalString(capture, "kind") &&
                    OptionalString(candidate, "canonicalSourcePath") == OptionalString(capture, "canonicalSourcePath") &&
                    OptionalString(candidate, "sourcePath") == OptionalString(capture, "sourcePath")).ToArray();
                if (matches.Length != 1 ||
                    !string.Equals(OptionalString(matches[0], "referenceID"), OptionalString(capture, "referenceID"), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(OptionalString(matches[0], "sourceRevision"), OptionalString(capture, "sourceRevision"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("project.save changed an external capture instead of only rebasing the project owner.");
            }
        }
    }

    private static void VerifySessionMapRevision(JsonElement response, string expectedPreviousRevision)
    {
        if (!string.Equals(OptionalString(response, "previousVisualizationRevision"), expectedPreviousRevision, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(OptionalString(response, "visualizationRevision")))
            throw new InvalidDataException("A map operation returned a stale or incomplete visualization receipt.");
    }

    private static async Task<JsonElement> SessionReferenceRequireSuccessAsync(HttpClient client,
        string method, object parameters, CancellationToken cancellation)
    {
        SessionReferenceOutcome outcome = await SubmitSessionReferenceAsync(client, method, parameters, cancellation);
        if (outcome.State is not ("completed" or "succeeded") ||
            outcome.Result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException($"Server operation {method} ended in {outcome.State}: {JsonSerializer.Serialize(outcome.Error, JsonOptions)}");
        return outcome.Result;
    }

    private static async Task<bool> SessionReferenceExpectRejectedAsync(HttpClient client,
        string method, object parameters, CancellationToken cancellation)
    {
        SessionReferenceOutcome outcome = await SubmitSessionReferenceAsync(client, method, parameters, cancellation);
        return outcome.State.Equals("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SessionReferenceOutcome> SubmitSessionReferenceAsync(HttpClient client,
        string method, object parameters, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/operations",
            new { method, parameters }, JsonOptions, cancellation);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new HttpRequestException($"POST api/operations ({method}) returned {(int)response.StatusCode}: {body}");
        using JsonDocument submitted = JsonDocument.Parse(body);
        string id = StringProperty(submitted.RootElement, "id");
        long cursor = 0;
        while (true)
        {
            using JsonDocument poll = await GetJsonAsync(client,
                $"api/operations/{Uri.EscapeDataString(id)}?after={cursor}", cancellation);
            JsonElement root = poll.RootElement;
            cursor = Math.Max(cursor, LongPropertyOrDefault(root, "cursor", cursor));
            if (PropertyOrNull(root, "hasMore").ValueKind == JsonValueKind.True) continue;
            string state = StringProperty(root, "state");
            if (IsTerminal(state))
                return new(state,
                    root.TryGetProperty("result", out JsonElement result) ? result.Clone() : JsonSerializer.SerializeToElement<object?>(null),
                    root.TryGetProperty("error", out JsonElement error) ? error.Clone() : JsonSerializer.SerializeToElement<object?>(null));
            await Task.Delay(100, cancellation);
        }
    }
}

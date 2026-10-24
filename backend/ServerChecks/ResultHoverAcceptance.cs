using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ras.Layers;
using Ras.Runtime;

namespace Ras.Backend.ServerChecks;

internal sealed record ResultHoverAcceptanceReport(
    string State,
    string ManifestPath,
    string ProjectPath,
    string ProjectRevision,
    string VisualizationRevision,
    string[] ResultPaths,
    string[] ResultRevisions,
    int[] ProfileCounts,
    bool CpuSolvesCompleted,
    bool HdfProfilesReadBack,
    bool PlanMapAndResultNodesResolved);

internal sealed record ResultHoverSourceManifest(
    int SelectionIndex,
    string ResultPath,
    string Revision,
    string ResultName,
    string[] Times,
    double?[] CellDepths,
    double?[] MapDepths);

internal sealed record ResultHoverNodeManifest(string Name, string Kind, object Locator);

internal sealed record ResultHoverSourceSelection(
    int SelectionIndex,
    string ResultPath,
    string Revision);

internal sealed record ResultHoverExpectedEntry(
    int SelectionIndex,
    string ResultPath,
    string Revision,
    int NativeProfileIndex,
    double ExpectedValue,
    string ExpectedDisplayValue,
    double Tolerance,
    double RawCellDepth);

internal sealed record ResultHoverCaseManifest(
    string Id,
    string Node,
    string MapType,
    int AnchorProfileIndex,
    ResultHoverSourceSelection[] SelectedSources,
    ResultHoverExpectedEntry[] Entries,
    int ExpectedEntryCount,
    ResultHoverPoint Point,
    string Time);

internal sealed record ResultHoverPoint(double X, double Y, int Row, int Column,
    int Width, int Height, double U, double V, double[] Extent);

internal sealed record ResultHoverMapManifest(string MapID, int MapIndex,
    string MapName, string MapType);

internal sealed record ResultHoverManifest(
    string Schema,
    string ProjectPath,
    string ProjectRevision,
    string ProjectXmlSHA,
    string PlanPath,
    string VisualizationPath,
    string VisualizationRevision,
    ResultHoverMapManifest Map,
    double[] ResultExtent,
    Dictionary<string, ResultHoverNodeManifest> Nodes,
    ResultHoverSourceManifest[] Sources,
    ResultHoverCaseManifest[] Cases,
    object UiSequence);

/// <summary>
/// Produces two genuine CPU Plan Results with overlapping but offset profile
/// times, then reads the persisted HDF cell profiles and resolves the saved
/// map-tree identities needed by the browser hover acceptance.
/// </summary>
internal static partial class ServerAcceptance
{
    private const string ResultHoverMapName = "结果悬停验收地图";
    private const float ResultHoverRainfallMillimetersPerHour = 3600f;

    public static async Task<ResultHoverAcceptanceReport> CompleteResultHoverFixtureAsync(
        Uri baseAddress, string fixtureManifestPath, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback ||
            (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("结果悬停验收仅连接环回 HTTP(S) 服务。", nameof(baseAddress));
        string fixturePath = Path.GetFullPath(fixtureManifestPath);
        if (!File.Exists(fixturePath)) throw new FileNotFoundException("找不到已完成求解的地图 fixture manifest。", fixturePath);
        FixtureManifest fixture = JsonSerializer.Deserialize<FixtureManifest>(
            await File.ReadAllTextAsync(fixturePath), JsonOptions)
            ?? throw new InvalidDataException("地图 fixture manifest 内容为空或格式无效。");
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory)) throw new IOException("结果悬停输出路径被同名文件占用。");
        Directory.CreateDirectory(outputDirectory);
        string manifestPath = Path.Combine(outputDirectory, "result-hover-manifest.json");
        string outputFixturePath = Path.Combine(outputDirectory, "map-fixture.json");
        if (File.Exists(manifestPath) || File.Exists(outputFixturePath))
            throw new IOException("结果悬停恢复输出已存在，不覆盖既有验收产物。");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("结果悬停验收服务未报告 ready。");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = Path.GetFullPath(fixture.ProjectPath) }, cancellation)).Result;
        string projectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        if (!PathEquals(projectPath, fixture.ProjectPath)) throw new SourceFileChangedException();
        string projectXml = StringProperty(opened, "projectXml");
        string projectRevision = StringProperty(opened, "revision");
        string projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));
        string planName = Path.GetFileNameWithoutExtension(fixture.PlanPath);

        JsonElement projectTree = (await SubmitOperationAsync(client, "project.tree.load", new
        {
            projectPath,
            projectXml,
            mode = "Project",
        }, cancellation)).Result;
        CatalogTreeNode[] projectRoots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(projectTree, "roots").GetRawText(), JsonOptions) ?? [];
        CatalogTreeNode resultGroup = FlattenMapNodes(projectRoots)
            .SingleOrDefault(node => node.SourceClass == "MultiResultNodeVM" && node.Name == planName)
            ?? throw new InvalidDataException("已完成工程目录没有公开该计划的结果组。");
        CatalogTreeNode[] resultRows = (resultGroup.Children ?? [])
            .Where(node => node.SourceClass == "ObservableResult" && node.AssetID is { Length: > 0 })
            .ToArray();
        if (resultRows.Length != 2)
            throw new InvalidDataException($"已完成计划结果组应恰有两个真实 HDF，实际为 {resultRows.Length} 个。");

        ResultHoverSourceManifest[] sources = [];
        var readSources = new List<ResultHoverSourceManifest>(resultRows.Length);
        foreach (CatalogTreeNode row in resultRows)
        {
            string resultPath = Path.GetFullPath(row.AssetID!);
            string revision = FileSha256(resultPath);
            if (row.ResultComputeState != "completed" ||
                !string.Equals(row.ResultComputeSourcePath, resultPath, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                !string.Equals(row.ResultComputeSourceRevision, revision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("结果树没有确认该 HDF 是当前字节的成功求解产物。");
            readSources.Add(await ReadResultSourceAsync(client, resultPath, revision,
                selectionIndex: readSources.Count, terrainElevation: fixture.TerrainElevation,
                cancellation: cancellation));
        }
        readSources.Sort((left, right) => StringComparer.Ordinal.Compare(left.Times[0], right.Times[0]));
        sources = readSources.Select((source, index) => source with { SelectionIndex = index }).ToArray();
        ResultHoverSourceManifest firstSource = sources[0];
        ResultHoverSourceManifest secondSource = sources[1];
        CatalogTreeNode firstSourceRow = resultRows.Single(row => PathEquals(row.AssetID, firstSource.ResultPath));
        CatalogTreeNode secondSourceRow = resultRows.Single(row => PathEquals(row.AssetID, secondSource.ResultPath));
        if (firstSourceRow.Name != firstSource.ResultName || secondSourceRow.Name != secondSource.ResultName)
            throw new InvalidDataException("Result catalog 名称与工程树源名称不一致，无法安全序列化 direct Result 行。");
        if (string.Equals(firstSource.ResultName, secondSource.ResultName, StringComparison.Ordinal))
            throw new InvalidDataException("结果树无法用唯一名称区分两个 HDF，不能安全构造 direct Result fixture 节点。");
        if (firstSource.Times.Length != 5 || secondSource.Times.Length != 5 ||
            !string.Equals(firstSource.Times[1], secondSource.Times[0], StringComparison.Ordinal) ||
            !string.Equals(firstSource.Times[4], secondSource.Times[3], StringComparison.Ordinal) ||
            string.Equals(secondSource.Times[4], firstSource.Times[0], StringComparison.Ordinal))
            throw new InvalidDataException("真实 HDF profile 未形成 A(t0…t4)、B(t1…t5) 的共享时间范围。");

        File.Copy(fixturePath, outputFixturePath);
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        if (!File.Exists(visualizationPath)) throw new FileNotFoundException("已完成 fixture 缺少 .vis 地图文档。", visualizationPath);
        JsonElement mapList = (await SubmitOperationAsync(client, "map.document.list", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedProjectRevision = projectRevision,
        }, cancellation)).Result;
        JsonElement[] matchingMaps = Property(mapList, "maps").EnumerateArray()
            .Where(item => StringProperty(item, "mapName") == ResultHoverMapName &&
                StringProperty(item, "mapType") == "Plan").ToArray();
        if (matchingMaps.Length != 1) throw new InvalidDataException("已完成 fixture 没有唯一的 Plan 验收地图。");
        int mapIndex = IntProperty(matchingMaps[0], "mapIndex");
        string mapName = StringProperty(matchingMaps[0], "mapName");
        string mapType = StringProperty(matchingMaps[0], "mapType");
        string? listedVisualizationRevision = OptionalString(mapList, "visualizationRevision");
        string diskVisualizationRevision = FileSha256(visualizationPath);
        if (listedVisualizationRevision is null ||
            !listedVisualizationRevision.Equals(diskVisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        EnsureSerializedDirectResultNode(visualizationPath, mapName, mapType, firstSource.ResultName);
        string visualizationRevision = FileSha256(visualizationPath);
        string mapID = Guid.NewGuid().ToString("D");
        object MapRequest(string expectedRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            mapID,
            mapIndex,
            mapName,
            mapType,
            expectedVisualizationRevision = expectedRevision,
            projectSessionID = mapID,
        };

        JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        CatalogTreeNode[] mapRoots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        CatalogTreeNode[] mapNodes = FlattenMapNodes(mapRoots).ToArray();
        CatalogTreeNode multiNode = mapNodes.SingleOrDefault(node => node.Locator?.NodeType == "MultiResult" &&
            node.Name == planName) ?? throw new InvalidDataException("计划地图没有公开唯一的 MultiResult 节点。");
        CatalogTreeNode ordinaryNode = mapNodes.SingleOrDefault(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator.AssetID, firstSource.ResultPath))
            ?? throw new InvalidDataException("fixture .vis 中的 direct Result 行没有解析成真实 HDF locator。");
        CatalogTreeNodeLocator multiLocator = multiNode.Locator
            ?? throw new InvalidDataException("MultiResult 节点缺少 locator。");
        CatalogTreeNodeLocator ordinaryLocator = ordinaryNode.Locator
            ?? throw new InvalidDataException("普通 Result 节点缺少 locator。");

        visualizationRevision = await EnsureNodeCheckedAsync(client, MapRequest,
            visualizationRevision, multiLocator, cancellation);
        tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        mapRoots = JsonSerializer.Deserialize<CatalogTreeNode[]>(Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        mapNodes = FlattenMapNodes(mapRoots).ToArray();
        multiNode = mapNodes.Single(node => node.Locator?.NodeType == "MultiResult" && node.Name == planName);
        ordinaryNode = mapNodes.Single(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator?.AssetID, firstSource.ResultPath));
        multiLocator = multiNode.Locator ?? throw new InvalidDataException("MultiResult 节点缺少 locator。");
        ordinaryLocator = ordinaryNode.Locator ?? throw new InvalidDataException("普通 Result 节点缺少 locator。");
        visualizationRevision = await EnsureNodeCheckedAsync(client, MapRequest,
            visualizationRevision, ordinaryLocator, cancellation);

        object[] selectionOverrides =
        [
            new { node = ordinaryLocator, selectionIndex = 0, mode = "Explicit", resultPath = firstSource.ResultPath, resultRevision = firstSource.Revision },
            new { node = multiLocator, selectionIndex = 0, mode = "Explicit", resultPath = firstSource.ResultPath, resultRevision = firstSource.Revision },
            new { node = multiLocator, selectionIndex = 1, mode = "Explicit", resultPath = secondSource.ResultPath, resultRevision = secondSource.Revision },
        ];
        JsonElement sourcesContext = (await SubmitOperationAsync(client, "map.resultSources",
            new { map = MapRequest(visualizationRevision), selectionOverrides }, cancellation)).Result;
        string contextToken = StringProperty(sourcesContext, "contextToken");
        JsonElement[] activeNodes = Property(sourcesContext, "activeNodes").EnumerateArray().ToArray();
        if (!activeNodes.Any(node => StringProperty(node, "nodeKind") == "Result" &&
                LocatorEqual(Property(node, "node"), ordinaryLocator)) ||
            !activeNodes.Any(node => StringProperty(node, "nodeKind") == "MultiResult" &&
                LocatorEqual(Property(node, "node"), multiLocator)))
            throw new InvalidDataException("结果上下文没有同时保留普通 Result 和 MultiResult 的活动节点。");

        ResultHoverPoint point = new(1.5, 2.5, 1, 1, 4, 4, 0.375, 0.375, [0, 0, 4, 4]);
        ResultHoverSourceSelection Choice(ResultHoverSourceManifest source, int selectionIndex) =>
            new(selectionIndex, source.ResultPath, source.Revision);
        ResultHoverExpectedEntry Entry(ResultHoverSourceManifest source, int selectionIndex, int profileIndex)
        {
            double rawDepth = source.CellDepths[profileIndex]
                ?? throw new InvalidDataException("HDF Cell Depth profile 不可用。");
            double mapDepth = source.MapDepths[profileIndex]
                ?? throw new InvalidDataException("DepthMapper 基准 profile 不可用。");
            return new(selectionIndex, source.ResultPath, source.Revision, profileIndex, mapDepth,
                mapDepth.ToString("G8", CultureInfo.InvariantCulture), 0.000001, rawDepth);
        }
        ResultHoverCaseManifest OrdinaryCase() => new("ordinary-A-profile-4", "ordinaryResult", "Depth", 4,
            [Choice(firstSource, 0)], [Entry(firstSource, 0, 4)], 1, point, firstSource.Times[4]);
        ResultHoverCaseManifest MultiCase(string id, int anchor, int firstIndex, int secondIndex, int count)
        {
            var entries = new List<ResultHoverExpectedEntry> { Entry(firstSource, 0, firstIndex) };
            if (count > 1) entries.Add(Entry(secondSource, 1, secondIndex));
            return new(id, "multiResult", "Depth", anchor,
                [Choice(firstSource, 0), Choice(secondSource, 1)], entries.ToArray(), count, point,
                firstSource.Times[anchor]);
        }
        ResultHoverCaseManifest[] cases =
        [
            OrdinaryCase(),
            MultiCase("multi-A0-no-B-match", 0, 0, 0, 1),
            MultiCase("multi-A1-to-B0", 1, 1, 0, 2),
            MultiCase("multi-A4-to-B3", 4, 4, 3, 2),
            new("multi-swapped-B0-to-A1", "multiResult", "Depth", 0,
                [Choice(secondSource, 0), Choice(firstSource, 1)],
                [Entry(secondSource, 0, 0), Entry(firstSource, 1, 1)], 2, point, secondSource.Times[0]),
            new("multi-swapped-B4-no-A-match", "multiResult", "Depth", 4,
                [Choice(secondSource, 0), Choice(firstSource, 1)],
                [Entry(secondSource, 0, 4)], 1, point, secondSource.Times[4]),
        ];
        var manifest = new ResultHoverManifest(
            "hecras-result-hover-e2e/v1", projectPath, projectRevision, projectXmlSHA,
            fixture.PlanPath, visualizationPath, visualizationRevision,
            new ResultHoverMapManifest(mapID, mapIndex, mapName, mapType), [0, 0, 4, 4],
            new Dictionary<string, ResultHoverNodeManifest>(StringComparer.Ordinal)
            {
                ["ordinaryResult"] = new(ordinaryNode.Name, "Result", ordinaryLocator),
                ["multiResult"] = new(multiNode.Name, "MultiResult", multiLocator),
            }, sources, cases,
            new
            {
                timeSwitch = new { fromCase = "multi-A1-to-B0", toCase = "multi-A4-to-B3" },
                slotSwap = new
                {
                    fromCase = "multi-A1-to-B0", toCase = "multi-swapped-B0-to-A1",
                    selections = new object[]
                    {
                        new { selectionIndex = 0, fromPath = firstSource.ResultPath, fromRevision = firstSource.Revision,
                            toPath = secondSource.ResultPath, toRevision = secondSource.Revision },
                        new { selectionIndex = 1, fromPath = secondSource.ResultPath, fromRevision = secondSource.Revision,
                            toPath = firstSource.ResultPath, toRevision = firstSource.Revision },
                    },
                },
                sourceSwitch = new
                {
                    selectionIndex = 0,
                    from = new { resultPath = firstSource.ResultPath, revision = firstSource.Revision,
                        nativeProfileIndex = 1, expectedValue = firstSource.MapDepths[1] },
                    to = new { resultPath = secondSource.ResultPath, revision = secondSource.Revision,
                        nativeProfileIndex = 0, expectedValue = secondSource.MapDepths[0] },
                    backTo = new { resultPath = firstSource.ResultPath, revision = firstSource.Revision,
                        nativeProfileIndex = 1, expectedValue = firstSource.MapDepths[1] },
                },
                activeContextToken = contextToken,
                ordinaryResultProfileIndex = 4,
                multiResultAnchorProfileIndex = 1,
                numericalBaseline = new
                {
                    hdfVariable = "Cell Depth",
                    rawCellDepthType = "float32",
                    cellMinimumElevationMeters = fixture.TerrainElevation,
                    terrainElevationMeters = fixture.TerrainElevation,
                    mapDepthExpression = "float32(float32(rawCellDepth + cellMinimumElevation) - terrainElevation)",
                },
            });
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellation);
        return new("completed", manifestPath, projectPath, projectRevision, visualizationRevision,
            sources.Select(source => source.ResultPath).ToArray(), sources.Select(source => source.Revision).ToArray(),
            sources.Select(source => source.Times.Length).ToArray(), CpuSolvesCompleted: true,
            HdfProfilesReadBack: true, PlanMapAndResultNodesResolved: true);
    }

    public static async Task<ResultHoverAcceptanceReport> RunResultHoverAsync(
        Uri baseAddress, string requestedOutputDirectory)
    {
        if (!baseAddress.IsLoopback ||
            (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("结果悬停验收仅连接环回 HTTP(S) 服务。", nameof(baseAddress));
        string outputDirectory = Path.GetFullPath(requestedOutputDirectory);
        if (File.Exists(outputDirectory))
            throw new IOException("结果悬停验收输出路径被同名文件占用。");
        Directory.CreateDirectory(outputDirectory);
        string manifestPath = Path.Combine(outputDirectory, "result-hover-manifest.json");
        if (File.Exists(manifestPath))
            throw new IOException("结果悬停验收manifest已存在，不覆盖已有验收产物。");
        string fixtureManifestPath = Path.Combine(outputDirectory, "map-fixture.json");
        if (File.Exists(fixtureManifestPath))
            throw new IOException("标准地图fixture manifest已存在，不覆盖已有验收产物。");

        string fixtureDirectory = Path.Combine(outputDirectory, "fixture-result-hover-" + Guid.NewGuid().ToString("N"));
        TextWriter originalOutput = Console.Out;
        FixtureManifest fixture;
        Console.SetOut(Console.Error);
        try { fixture = PlanComputeFixture.CreateMap(fixtureDirectory); }
        finally { Console.SetOut(originalOutput); }
        await File.WriteAllTextAsync(fixtureManifestPath, JsonSerializer.Serialize(fixture, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        SetFixtureRainfall(fixture.ProjectPath, ResultHoverRainfallMillimetersPerHour);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        CancellationToken cancellation = timeout.Token;
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using JsonDocument health = await GetWhenReadyAsync(client, "api/health", cancellation);
        if (StringProperty(health.RootElement, "status") != "ready")
            throw new InvalidDataException("结果悬停验收服务未报告 ready。");

        JsonElement opened = (await SubmitOperationAsync(client, "project.open",
            new { projectPath = fixture.ProjectPath }, cancellation)).Result;
        string projectPath = Path.GetFullPath(StringProperty(opened, "projectPath"));
        string projectXml = StringProperty(opened, "projectXml");
        string projectRevision = StringProperty(opened, "revision");
        string projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));

        JsonElement planSnapshot = (await SubmitOperationAsync(client, "model.load", new
        {
            projectPath,
            projectXml,
            assetPath = fixture.PlanPath,
            kind = "Plan",
        }, cancellation)).Result;
        string planRevision = StringProperty(planSnapshot, "revision");
        JsonElement windowIDs = Property(planSnapshot, "planTimeWindowFieldIDs");
        string startField = StringProperty(windowIDs, "start");
        string endField = StringProperty(windowIDs, "end");
        JsonElement planFields = Property(planSnapshot, "fields");
        string originalStartText = StringProperty(planFields, startField);
        string originalEndText = StringProperty(planFields, endField);
        string shiftedStart = ShiftPlanTime(originalStartText, 1);
        string shiftedEnd = ShiftPlanTime(originalEndText, 1);

        object Model(JsonElement snapshot, object[]? timeEdits = null) => new
        {
            assetPath = fixture.PlanPath,
            kind = "Plan",
            projectPath,
            revision = StringProperty(snapshot, "revision"),
            // The source Plan has no unsaved scalar/image draft. Keep fields
            // empty because its start/end values are sent only through the
            // typed ordered time-window edits below.
            fields = new Dictionary<string, string>(),
            images = new Dictionary<string, ImageData[]>(),
            projectXml,
            sourceState = OptionalString(snapshot, "sourceState"),
            planTimeWindowEdits = timeEdits,
        };

        JsonElement firstComputed = await ComputePlanAsync(client, Model(planSnapshot), cancellation);
        ResultHoverSourceManifest firstSource = await ReadResultSourceAsync(client, firstComputed,
            selectionIndex: 0, terrainElevation: fixture.TerrainElevation, cancellation: cancellation);

        // The Plan draft is in-memory only for compute. The saved Plan remains
        // the original source, so reload it and apply ordered +1s time edits.
        JsonElement secondSnapshot = (await SubmitOperationAsync(client, "model.load", new
        {
            projectPath,
            projectXml,
            assetPath = fixture.PlanPath,
            kind = "Plan",
        }, cancellation)).Result;
        if (StringProperty(secondSnapshot, "revision") != planRevision)
            throw new SourceFileChangedException();
        object[] timeWindowEdits =
        [
            new { id = "result-hover-start", fieldID = startField, value = shiftedStart },
            new { id = "result-hover-end", fieldID = endField, value = shiftedEnd },
        ];
        JsonElement secondComputed = await ComputePlanAsync(client, Model(secondSnapshot, timeWindowEdits), cancellation);
        ResultHoverSourceManifest secondSource = await ReadResultSourceAsync(client, secondComputed,
            selectionIndex: 1, terrainElevation: fixture.TerrainElevation, cancellation: cancellation);

        if (firstSource.Times.Length != 5 || secondSource.Times.Length != 5)
            throw new InvalidDataException("两个真实 Plan 结果都必须包含 t0 至 t4 五个输出 profile。");
        // The offset sources must share t1..t4. B's fifth profile is later
        // than A's final profile, while B's first profile matches A's second.
        if (!string.Equals(firstSource.Times[1], secondSource.Times[0], StringComparison.Ordinal) ||
            !string.Equals(firstSource.Times[4], secondSource.Times[3], StringComparison.Ordinal) ||
            string.Equals(secondSource.Times[4], firstSource.Times[0], StringComparison.Ordinal))
            throw new InvalidDataException("两个 Result 没有形成 A(t0…t4)、B(t1…t5) 的共享 profile 时间范围。");

        // Reopen after both computes so map creation binds to the current .ras
        // identity and its exact XML/revision.
        JsonElement finalProject = (await SubmitOperationAsync(client, "project.open",
            new { projectPath }, cancellation)).Result;
        projectXml = StringProperty(finalProject, "projectXml");
        projectRevision = StringProperty(finalProject, "revision");
        projectXmlSHA = Sha256(Encoding.UTF8.GetBytes(projectXml));
        string finalPlanRevision = FileSha256(fixture.PlanPath);

        JsonElement createdMap = (await SubmitOperationAsync(client, "map.document.create", new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            expectedVisualizationRevision = (string?)null,
            kind = "Plan",
            name = ResultHoverMapName,
            initialPlanPath = fixture.PlanPath,
            initialPlanRevision = finalPlanRevision,
        }, cancellation)).Result;
        int mapIndex = IntProperty(createdMap, "mapIndex");
        string mapName = StringProperty(createdMap, "mapName");
        string mapType = StringProperty(createdMap, "mapType");
        string visualizationRevision = StringProperty(createdMap, "visualizationRevision");
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        EnsureSerializedDirectResultNode(visualizationPath, mapName, mapType, firstSource.ResultName);
        visualizationRevision = FileSha256(visualizationPath);
        string mapID = Guid.NewGuid().ToString("D");

        object MapRequest(string expectedRevision) => new
        {
            projectPath,
            projectXml,
            projectXmlSHA,
            mapID,
            mapIndex,
            mapName,
            mapType,
            expectedVisualizationRevision = expectedRevision,
            projectSessionID = mapID,
        };

        JsonElement tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        CatalogTreeNode[] roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(
            Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        CatalogTreeNode[] nodes = FlattenMapNodes(roots).ToArray();
        CatalogTreeNode multiNode = nodes.SingleOrDefault(node => node.Locator?.NodeType == "MultiResult" &&
            string.Equals(node.Name, Path.GetFileNameWithoutExtension(fixture.PlanPath), StringComparison.Ordinal))
            ?? nodes.SingleOrDefault(node => node.Locator?.NodeType == "MultiResult")
            ?? throw new InvalidDataException("计划地图没有生成可用于双来源验收的 MultiResult 节点。");
        CatalogTreeNode ordinaryNode = nodes.Where(node => node.Locator?.NodeType == "Result")
            .SingleOrDefault(node => PathEquals(node.Locator?.AssetID, firstSource.ResultPath))
            ?? throw new InvalidDataException("计划地图没有公开与真实 HDF A 精确绑定的普通 Result 节点。");
        if (multiNode.Locator is null || ordinaryNode.Locator is null)
            throw new InvalidDataException("结果树缺少精确 locator。");

        visualizationRevision = await EnsureNodeCheckedAsync(client, MapRequest,
            visualizationRevision, multiNode.Locator, cancellation);
        // Reload after the first .vis mutation to obtain a fresh locator.
        tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        nodes = FlattenMapNodes(roots).ToArray();
        multiNode = nodes.Single(node => node.Locator?.NodeType == "MultiResult" &&
            string.Equals(node.Name, multiNode.Name, StringComparison.Ordinal));
        ordinaryNode = nodes.Single(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator?.AssetID, firstSource.ResultPath));
        visualizationRevision = await EnsureNodeCheckedAsync(client, MapRequest,
            visualizationRevision, ordinaryNode.Locator!, cancellation);
        tree = (await SubmitOperationAsync(client, "map.tree.load",
            MapRequest(visualizationRevision), cancellation)).Result;
        roots = JsonSerializer.Deserialize<CatalogTreeNode[]>(Property(tree, "roots").GetRawText(), JsonOptions) ?? [];
        nodes = FlattenMapNodes(roots).ToArray();
        multiNode = nodes.Single(node => node.Locator?.NodeType == "MultiResult" &&
            string.Equals(node.Name, multiNode.Name, StringComparison.Ordinal));
        ordinaryNode = nodes.Single(node => node.Locator?.NodeType == "Result" &&
            PathEquals(node.Locator?.AssetID, firstSource.ResultPath));

        object[] selectionOverrides =
        [
            new { node = ordinaryNode.Locator, selectionIndex = 0, mode = "Explicit", resultPath = firstSource.ResultPath, resultRevision = firstSource.Revision },
            new { node = multiNode.Locator, selectionIndex = 0, mode = "Explicit", resultPath = firstSource.ResultPath, resultRevision = firstSource.Revision },
            new { node = multiNode.Locator, selectionIndex = 1, mode = "Explicit", resultPath = secondSource.ResultPath, resultRevision = secondSource.Revision },
        ];
        JsonElement sourcesContext = (await SubmitOperationAsync(client, "map.resultSources",
            new { map = MapRequest(visualizationRevision), selectionOverrides }, cancellation)).Result;
        string contextToken = StringProperty(sourcesContext, "contextToken");
        JsonElement[] activeNodes = Property(sourcesContext, "activeNodes").EnumerateArray().ToArray();
        CatalogTreeNodeLocator ordinaryLocator = ordinaryNode.Locator
            ?? throw new InvalidDataException("普通 Result 节点缺少 locator。");
        CatalogTreeNodeLocator multiLocator = multiNode.Locator
            ?? throw new InvalidDataException("MultiResult 节点缺少 locator。");
        if (!activeNodes.Any(node => StringProperty(node, "nodeKind") == "Result" &&
                LocatorEqual(Property(node, "node"), ordinaryLocator)) ||
            !activeNodes.Any(node => StringProperty(node, "nodeKind") == "MultiResult" &&
                LocatorEqual(Property(node, "node"), multiLocator)))
            throw new InvalidDataException("结果上下文没有同时保留普通 Result 和 MultiResult 的活动节点。");

        ResultHoverSourceManifest[] sourceManifests = [firstSource, secondSource];
        // The result image is north-up, while fixture HDF cells are ordered
        // south-to-north. Image row 1/column 1 maps to cell center (1.5, 2.5),
        // which is fixture HDF element 9.
        ResultHoverPoint point = new(1.5, 2.5, 1, 1, 4, 4, 0.375, 0.375, [0, 0, 4, 4]);
        ResultHoverSourceSelection Choice(ResultHoverSourceManifest source, int selectionIndex) =>
            new(selectionIndex, source.ResultPath, source.Revision);
        ResultHoverExpectedEntry Entry(ResultHoverSourceManifest source, int selectionIndex, int profileIndex)
        {
            double rawDepth = source.CellDepths[profileIndex]
                ?? throw new InvalidDataException("HDF cell-depth profile is unavailable.");
            double mapDepth = source.MapDepths[profileIndex]
                ?? throw new InvalidDataException("DepthMapper baseline profile is unavailable.");
            return new(selectionIndex, source.ResultPath, source.Revision, profileIndex,
                mapDepth, mapDepth.ToString("G8", CultureInfo.InvariantCulture), 0.000001, rawDepth);
        }
        ResultHoverCaseManifest OrdinaryCase(int index) => new(
            "ordinary-A-profile-4", "ordinaryResult", "Depth", index,
            [Choice(firstSource, 0)],
            [Entry(firstSource, 0, index)], 1, point, firstSource.Times[index]);
        ResultHoverCaseManifest MultiCase(string id, int anchorIndex, int firstIndex, int secondIndex, int entryCount) =>
            new(id, "multiResult", "Depth", anchorIndex,
                [Choice(firstSource, 0), Choice(secondSource, 1)],
                MakeMatchedEntries(anchorIndex, firstIndex, secondIndex, entryCount), entryCount,
                point, firstSource.Times[anchorIndex]);

        ResultHoverExpectedEntry[] MakeMatchedEntries(int anchorIndex, int firstIndex, int secondIndex, int entryCount)
        {
            var matches = new List<ResultHoverExpectedEntry> { Entry(firstSource, 0, firstIndex) };
            if (entryCount > 1) matches.Add(Entry(secondSource, 1, secondIndex));
            return matches.ToArray();
        }

        ResultHoverCaseManifest[] cases =
        [
            OrdinaryCase(4),
            MultiCase("multi-A0-no-B-match", 0, 0, 0, 1),
            MultiCase("multi-A1-to-B0", 1, 1, 0, 2),
            MultiCase("multi-A4-to-B3", 4, 4, 3, 2),
            new("multi-swapped-B0-to-A1", "multiResult", "Depth", 0,
                [Choice(secondSource, 0), Choice(firstSource, 1)],
                [Entry(secondSource, 0, 0), Entry(firstSource, 1, 1)], 2, point, secondSource.Times[0]),
            new("multi-swapped-B4-no-A-match", "multiResult", "Depth", 4,
                [Choice(secondSource, 0), Choice(firstSource, 1)],
                [Entry(secondSource, 0, 4)], 1, point, secondSource.Times[4]),
        ];

        var manifest = new ResultHoverManifest(
            "hecras-result-hover-e2e/v1",
            projectPath,
            projectRevision,
            projectXmlSHA,
            fixture.PlanPath,
            visualizationPath,
            visualizationRevision,
            new ResultHoverMapManifest(mapID, mapIndex, mapName, mapType),
            [0, 0, 4, 4],
            new Dictionary<string, ResultHoverNodeManifest>(StringComparer.Ordinal)
            {
                ["ordinaryResult"] = new(ordinaryNode.Name, "Result", ordinaryLocator),
                ["multiResult"] = new(multiNode.Name, "MultiResult", multiLocator),
            },
            sourceManifests,
            cases,
            new
            {
                timeSwitch = new { fromCase = "multi-A1-to-B0", toCase = "multi-A4-to-B3" },
                slotSwap = new
                {
                    fromCase = "multi-A1-to-B0",
                    toCase = "multi-swapped-B0-to-A1",
                    selections = new object[]
                    {
                        new { selectionIndex = 0, fromPath = firstSource.ResultPath, fromRevision = firstSource.Revision,
                            toPath = secondSource.ResultPath, toRevision = secondSource.Revision },
                        new { selectionIndex = 1, fromPath = secondSource.ResultPath, fromRevision = secondSource.Revision,
                            toPath = firstSource.ResultPath, toRevision = firstSource.Revision },
                    },
                },
                sourceSwitch = new
                {
                    selectionIndex = 0,
                    from = new { resultPath = firstSource.ResultPath, revision = firstSource.Revision,
                        nativeProfileIndex = 1, expectedValue = firstSource.MapDepths[1] },
                    to = new { resultPath = secondSource.ResultPath, revision = secondSource.Revision,
                        nativeProfileIndex = 0, expectedValue = secondSource.MapDepths[0] },
                    backTo = new { resultPath = firstSource.ResultPath, revision = firstSource.Revision,
                        nativeProfileIndex = 1, expectedValue = firstSource.MapDepths[1] },
                },
                activeContextToken = contextToken,
                ordinaryResultProfileIndex = 4,
                multiResultAnchorProfileIndex = 1,
                numericalBaseline = new
                {
                    hdfVariable = "Cell Depth",
                    rawCellDepthType = "float32",
                    cellMinimumElevationMeters = fixture.TerrainElevation,
                    terrainElevationMeters = fixture.TerrainElevation,
                    mapDepthExpression = "float32(float32(rawCellDepth + cellMinimumElevation) - terrainElevation)",
                },
            });

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellation);
        return new("completed", manifestPath, projectPath, projectRevision,
            visualizationRevision, sourceManifests.Select(source => source.ResultPath).ToArray(),
            sourceManifests.Select(source => source.Revision).ToArray(),
            sourceManifests.Select(source => source.Times.Length).ToArray(),
            CpuSolvesCompleted: true, HdfProfilesReadBack: true, PlanMapAndResultNodesResolved: true);
    }

    private static void SetFixtureRainfall(string projectPath, float rainfallMillimetersPerHour)
    {
        RasRuntimeBootstrap.Configure();
        Project project = new(projectPath);
        try
        {
            BoundaryCondition boundary = project.BoundaryConditions.Single();
            boundary.Precipitation.ConstantValue = rainfallMillimetersPerHour;
            boundary.Save();
        }
        finally
        {
            foreach (Result result in project.Results) result.Dispose();
            foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
                         .Concat(project.Geometries).Concat(project.BoundaryConditions)
                         .Concat(project.Terrains).Concat(project.NValues).Concat(project.ClassificationLayers))
                layer.Dispose();
        }
    }

    private static async Task<JsonElement> ComputePlanAsync(HttpClient client, object model,
        CancellationToken cancellation)
    {
        JsonElement inspection = (await SubmitOperationAsync(client, "plan.compute.inspect",
            new { model }, cancellation)).Result;
        if (!BooleanProperty(inspection, "canExecute"))
            throw new InvalidOperationException("真实 CPU 计划未通过 compute 检查：" +
                JsonSerializer.Serialize(PropertyOrNull(inspection, "messages"), JsonOptions));
        JsonElement result = (await SubmitOperationAsync(client, "plan.compute.execute", new
        {
            model,
            inspectionId = StringProperty(inspection, "inspectionId"),
        }, cancellation)).Result;
        if (!string.Equals(StringProperty(result, "state"), "completed", StringComparison.OrdinalIgnoreCase) ||
            !BooleanProperty(Property(result, "summary"), "success"))
            throw new InvalidOperationException("真实 CPU 计划求解未成功：" + OptionalString(result, "errorMessage"));
        return result;
    }

    private static async Task<ResultHoverSourceManifest> ReadResultSourceAsync(HttpClient client,
        JsonElement computeResult, int selectionIndex, double terrainElevation, CancellationToken cancellation)
    {
        string resultPath = Path.GetFullPath(StringProperty(computeResult, "resultPath"));
        string revision = StringProperty(computeResult, "resultRevision");
        return await ReadResultSourceAsync(client, resultPath, revision, selectionIndex,
            terrainElevation, cancellation);
    }

    private static async Task<ResultHoverSourceManifest> ReadResultSourceAsync(HttpClient client,
        string resultPath, string revision, int selectionIndex, double terrainElevation,
        CancellationToken cancellation)
    {
        resultPath = Path.GetFullPath(resultPath);
        if (!File.Exists(resultPath) || !FileSha256(resultPath).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CPU Result HDF 不存在，或磁盘 SHA 与求解回执不匹配。");
        JsonElement catalog = (await SubmitOperationAsync(client, "result.profileCatalog",
            new { resultPath, revision }, cancellation)).Result;
        JsonElement block = Property(catalog, "outputBlocks").EnumerateArray()
            .Single(item => Property(item, "variables").EnumerateArray()
                .Any(variable => StringProperty(variable, "name") == "Cell Depth"));
        _ = Property(catalog, "flowAreas").EnumerateArray()
            .Single(area => IntProperty(area, "index") == 0);
        string outputBlockName = StringProperty(block, "name");
        JsonElement series = (await SubmitOperationAsync(client, "result.profile", new
        {
            resultPath,
            revision,
            outputBlockName,
            variable = "Cell Depth",
            flowAreaIndex = 0,
            elementIndex = 9,
        }, cancellation)).Result;
        if (!BooleanProperty(series, "available"))
            throw new InvalidDataException("真实 Result HDF 的 Cell Depth profile 无法读取：" + OptionalString(series, "unavailableReason"));
        JsonElement[] points = Property(series, "points").EnumerateArray().ToArray();
        string[] times = points.Select(point => StringProperty(point, "time")).ToArray();
        double?[] values = points.Select(point =>
        {
            JsonElement value = PropertyOrNull(point, "value");
            return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : (double?)null;
        }).ToArray();
        if (times.Length != IntProperty(block, "profileCount") || values.Any(value => value is null || !double.IsFinite(value.Value)))
            throw new InvalidDataException("真实 HDF profile 时间和值数量不匹配，或含不可用值。");
        double?[] mapDepths = values.Select(value =>
        {
            if (value is null) return (double?)null;
            // The source Depth map reads Cell Depth as float32, reconstructs
            // stage using the cell minimum elevation, then subtracts the
            // float32 terrain sample. This fixture is flat at one elevation.
            float elevation = (float)terrainElevation;
            float stage = (float)value.Value + elevation;
            return (double)(stage - elevation);
        }).ToArray();
        if (!FileSha256(resultPath).Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return new(selectionIndex, resultPath, revision,
            StringProperty(catalog, "resultName"), times, values, mapDepths);
    }

    private static void EnsureSerializedDirectResultNode(string visualizationPath, string mapName,
        string mapType, string resultName)
    {
        XDocument document = XDocument.Load(visualizationPath, LoadOptions.PreserveWhitespace);
        XElement maps = document.Root?.Element("Maps")
            ?? throw new InvalidDataException(".vis 文件缺少 Maps 容器。");
        XElement[] mapMatches = maps.Elements("Map").Where(element =>
            string.Equals((string?)element.Attribute("Name"), mapName, StringComparison.Ordinal) &&
            string.Equals((string?)element.Attribute("Type"), mapType, StringComparison.Ordinal)).ToArray();
        if (mapMatches.Length != 1) throw new SourceFileChangedException();
        XElement nodes = mapMatches[0].Element("Tree")?.Element("Nodes")
            ?? throw new InvalidDataException("验收地图缺少树节点容器。");
        XElement[] layerRoots = nodes.Elements("Node").Where(element =>
            string.Equals((string?)element.Attribute("Type"), "Root", StringComparison.Ordinal) &&
            string.Equals((string?)element.Attribute("Name"), "Layers", StringComparison.Ordinal)).ToArray();
        if (layerRoots.Length != 1) throw new InvalidDataException("验收地图没有唯一 Layers 树根。");
        XElement[] directResults = layerRoots[0].Elements("Node").Where(element =>
            string.Equals((string?)element.Attribute("Type"), "Result", StringComparison.Ordinal) &&
            string.Equals((string?)element.Attribute("Name"), resultName, StringComparison.Ordinal)).ToArray();
        if (directResults.Length > 1) throw new InvalidDataException("验收地图含重复的 direct Result 序列化行。");
        if (directResults.Length == 1) return;

        // This row is a fixture-only legacy/custom serialized tree shape. The
        // Host resolver still has to find the exact, unique Result candidate
        // and bind its locator AssetID to the selected source HDF.
        layerRoots[0].Add(new XElement("Node", new XAttribute("Type", "Result"),
            new XAttribute("Name", resultName)));
        string stagePath = Path.Combine(Path.GetDirectoryName(visualizationPath)!,
            ".result-hover-vis-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            document.Save(stagePath, SaveOptions.DisableFormatting);
            using (var staged = new FileStream(stagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                staged.Flush(flushToDisk: true);
            File.Move(stagePath, visualizationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagePath)) File.Delete(stagePath);
        }
    }

    private static async Task<string> EnsureNodeCheckedAsync(HttpClient client,
        Func<string, object> mapRequest, string visualizationRevision,
        CatalogTreeNodeLocator locator, CancellationToken cancellation)
    {
        JsonElement result = (await SubmitOperationAsync(client, "map.tree.perform", new
        {
            map = mapRequest(visualizationRevision),
            action = "visibility",
            node = locator,
            @checked = true,
        }, cancellation)).Result;
        return StringProperty(result, "visualizationRevision");
    }

    private static IEnumerable<CatalogTreeNode> FlattenMapNodes(IEnumerable<CatalogTreeNode> roots)
    {
        foreach (CatalogTreeNode node in roots)
        {
            yield return node;
            if (node.Children is null) continue;
            foreach (CatalogTreeNode child in FlattenMapNodes(node.Children)) yield return child;
        }
    }

    private static bool LocatorEqual(JsonElement actual, CatalogTreeNodeLocator expected)
    {
        CatalogTreeNodeLocator? locator = JsonSerializer.Deserialize<CatalogTreeNodeLocator>(actual.GetRawText(), JsonOptions);
        return locator is not null && locator.NodeType == expected.NodeType &&
            locator.NodeName == expected.NodeName && locator.RootName == expected.RootName &&
            PathEquals(locator.AssetID, expected.AssetID) &&
            ((locator.NodePath ?? []).SequenceEqual(expected.NodePath ?? []));
    }

    private static string ShiftPlanTime(string value, int seconds)
    {
        DateTimeOffset date = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return date.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FileSha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool PathEquals(string? left, string? right) =>
        left is not null && right is not null && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

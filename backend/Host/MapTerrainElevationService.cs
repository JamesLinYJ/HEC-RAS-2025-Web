using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Terrains;
using Utility.Files;

namespace Ras.Backend;

/// <summary>
/// Samples the actual retained Terrain object for the final segment of the
/// source map length tool. It never uses associated-map preview pixels.
/// </summary>
public static class MapTerrainElevationService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static MapTerrainElevationResult Sample(MapTerrainElevationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        RejectProtectedPathsBeforeRead(request);

        MapResultGeometryOwner[] owners = request.ResultOwners ?? [];
        MapResultGeometryOwner? contextOwner = owners.FirstOrDefault();
        if (owners.Any(owner => owner.ContextToken != contextOwner?.ContextToken ||
                !SameSelectionOverrides(owner.SelectionOverrides, contextOwner?.SelectionOverrides)))
            throw new SourceFileChangedException();

        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(
            request.Map, contextOwner?.SelectionOverrides, contextOwner?.ContextToken);
        foreach (MapResultGeometryOwner owner in owners)
        {
            MapResultActiveNode[] resultNodes = context.Sources.ActiveNodes.Where(active =>
                SameLocator(active.Node, owner.ResultNode)).ToArray();
            if (resultNodes.Length != 1 || !resultNodes[0].Selections.Any(selection =>
                    selection.SelectionIndex == owner.SelectionIndex && selection.SelectedResult is { } result &&
                    PathComparer.Equals(SourceFilePaths.Canonical(result.ResultPath),
                        SourceFilePaths.Canonical(owner.SelectedResultPath)) &&
                    string.Equals(result.Revision, owner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase)))
                throw new SourceFileChangedException();
        }
        CatalogTreeNode[] matches = Flatten(context.Tree.Roots).Where(node =>
            node.Locator is { } locator && SameLocator(locator, request.TerrainNode)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        CatalogTreeNode selected = matches[0];
        CatalogTreeNodeLocator selectedLocator = selected.Locator ?? throw new SourceFileChangedException();
        if (selectedLocator.NodeType != "Terrain" ||
            selectedLocator.AssociationKind != "Terrain" || selected.Checked != "checked" ||
            !MapTreeService.IsEffectivelyVisible(context.Tree.Roots, selected) ||
            string.IsNullOrWhiteSpace(selectedLocator.AssetID))
            throw new SourceFileChangedException();

        string selectedPath = ProjectAssetPath(context.Project, selectedLocator.AssetID);
        string expectedPath = SourceFilePaths.Canonical(request.ExpectedTerrainPath);
        if (!PathComparer.Equals(selectedPath, expectedPath))
            throw new SourceFileChangedException();

        Terrain[] terrains = context.Project.Terrains.Where(terrain =>
            PathComparer.Equals(ProjectAssetPath(context.Project, terrain.Filename), expectedPath)).ToArray();
        if (terrains.Length != 1) throw new SourceFileChangedException();
        Terrain terrain = terrains[0];

        MapResultRenderService.TerrainSourceSnapshot capture =
            MapResultRenderService.CaptureTerrainSources(terrain);
        if (!PathComparer.Equals(capture.Path, expectedPath)) throw new SourceFileChangedException();
        ValidateExpectedDependencies(request, terrain, capture);

        Point[] points = request.Endpoints.Select(point => new Point(point.X, point.Y)).ToArray();
        var values = new float[points.Length];
        terrain.SamplePoints(points.AsMemory(), values.AsMemory());

        MapResultRenderService.VerifyTerrainSources(capture);
        MapResultRenderService.TerrainSourceSnapshot current =
            MapResultRenderService.CaptureTerrainSources(terrain);
        if (!PathComparer.Equals(current.Path, capture.Path) ||
            !string.Equals(current.Revision, capture.Revision, StringComparison.OrdinalIgnoreCase) ||
            !current.Files.SequenceEqual(capture.Files))
            throw new SourceFileChangedException();
        context.EnsureUnchanged();

        AssociatedMapLayerDependency[] dependencies = ExpectedTerrainDependencies(terrain, capture);
        return new MapTerrainElevationResult(context.Sources.ProjectPath, context.Sources.ProjectRevision,
            context.Sources.ProjectXmlSHA, context.Sources.VisualizationRevision, context.Sources.MapID,
            context.Sources.MapIndex, context.Sources.MapName, context.Sources.MapType,
            context.Sources.ContextToken, selectedLocator, capture.Path, capture.Revision,
            dependencies, values.Select(value => float.IsFinite(value) ? (double?)value : null).ToArray());
    }

    private static void ValidateRequest(MapTerrainElevationRequest request)
    {
        if (request.Map is null || request.TerrainNode is null ||
            request.TerrainNode.NodeType != "Terrain" || request.TerrainNode.AssociationKind != "Terrain" ||
            string.IsNullOrWhiteSpace(request.ExpectedTerrainPath) ||
            !Path.IsPathFullyQualified(request.ExpectedTerrainPath) ||
            !IsSHA256(request.ExpectedTerrainRevision) ||
            request.ExpectedDependencies is null || request.ExpectedDependencies.Length is < 1 or > 128 ||
            request.Endpoints is null || request.Endpoints.Length != 2 ||
            request.Endpoints.Any(point => point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("A map length slope requires one exact Terrain row, its captured sources, and two finite project-world endpoints.");
        if (request.ResultOwners is { Length: > 16 })
            throw new ArgumentException("The Terrain row has too many Result owners.", nameof(request.ResultOwners));
        foreach (AssociatedMapLayerDependency dependency in request.ExpectedDependencies)
        {
            if (dependency is null || string.IsNullOrWhiteSpace(dependency.Path) ||
                !Path.IsPathFullyQualified(dependency.Path) || !IsSHA256(dependency.Revision) ||
                dependency.Role is not ("source-hdf" or "terrain-input-raster"))
                throw new ArgumentException("The Terrain source dependency capture is invalid.", nameof(request.ExpectedDependencies));
        }
    }

    /// <summary>
    /// The map resolver opens and hashes project/source files. Match caller-
    /// supplied source paths against the run's protected writer set before
    /// invoking it so a writer-owned file is never read first.
    /// </summary>
    private static void RejectProtectedPathsBeforeRead(MapTerrainElevationRequest request)
    {
        string[] protectedPaths = (request.Map.ComputeReadLease?.ProtectedSourcePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(SourceFilePaths.Canonical).ToArray();
        if (protectedPaths.Length == 0) return;
        string projectPath = Path.GetFullPath(request.Map.ProjectPath);
        string? projectDirectory = Path.GetDirectoryName(projectPath);
        var requestedPaths = new List<string> { SourceFilePaths.Canonical(projectPath) };
        requestedPaths.Add(SourceFilePaths.Canonical(request.ExpectedTerrainPath));
        if (!string.IsNullOrWhiteSpace(request.TerrainNode.AssetID))
            requestedPaths.Add(ResolveRequestPath(projectDirectory, request.TerrainNode.AssetID));
        if (!string.IsNullOrWhiteSpace(request.TerrainNode.AssociationAssetID))
            requestedPaths.Add(ResolveRequestPath(projectDirectory, request.TerrainNode.AssociationAssetID));
        if (request.ExpectedDependencies is not null)
            requestedPaths.AddRange(request.ExpectedDependencies.Where(dependency => dependency is not null)
                .Select(dependency => SourceFilePaths.Canonical(dependency.Path)));
        if (requestedPaths.Any(path => protectedPaths.Contains(path, PathComparer)))
            throw new NotSupportedException("The selected Terrain source is currently owned by an active writer.");
    }

    private static string ResolveRequestPath(string? projectDirectory, string path) =>
        SourceFilePaths.Canonical(Path.IsPathRooted(path) ? path :
            Path.Combine(projectDirectory ?? throw new ArgumentException("The project path has no parent directory."), path));

    private static void ValidateExpectedDependencies(MapTerrainElevationRequest request, Terrain terrain,
        MapResultRenderService.TerrainSourceSnapshot capture)
    {
        AssociatedMapLayerDependency[] actual = ExpectedTerrainDependencies(terrain, capture);
        AssociatedMapLayerDependency[] expected = request.ExpectedDependencies
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => SourceFilePaths.Canonical(item.Path), PathComparer).ToArray();
        if (!PathComparer.Equals(capture.Path, SourceFilePaths.Canonical(request.ExpectedTerrainPath)) ||
            !string.Equals(capture.Files[capture.Path], request.ExpectedTerrainRevision, StringComparison.OrdinalIgnoreCase) ||
            actual.Length != expected.Length || actual.Where((dependency, index) =>
                !string.Equals(dependency.Role, expected[index].Role, StringComparison.Ordinal) ||
                !PathComparer.Equals(dependency.Path, SourceFilePaths.Canonical(expected[index].Path)) ||
                !string.Equals(dependency.Revision, expected[index].Revision, StringComparison.OrdinalIgnoreCase)).Any())
            throw new SourceFileChangedException();
    }

    private static AssociatedMapLayerDependency[] ExpectedTerrainDependencies(Terrain terrain,
        MapResultRenderService.TerrainSourceSnapshot capture)
    {
        var dependencies = new List<AssociatedMapLayerDependency>
        {
            new(capture.Path, capture.Files[capture.Path], "source-hdf")
        };
        foreach (RasterTerrainInput input in terrain.AllRasterInputs())
        {
            string path = SourceFilePaths.Canonical(input.Filename);
            if (!capture.Files.TryGetValue(path, out string? revision) || revision is null)
                throw new SourceFileChangedException();
            if (!dependencies.Any(existing => existing.Role == "terrain-input-raster" &&
                    PathComparer.Equals(existing.Path, path)))
                dependencies.Add(new AssociatedMapLayerDependency(path, revision, "terrain-input-raster"));
        }
        return dependencies.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, PathComparer).ToArray();
    }

    private static string ProjectAssetPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename) ? filename : Path.Combine(project.ProjectDirectory, filename);
        return SourceFilePaths.Canonical(path);
    }

    private static IEnumerable<CatalogTreeNode> Flatten(IEnumerable<CatalogTreeNode> roots)
    {
        foreach (CatalogTreeNode node in roots)
        {
            yield return node;
            foreach (CatalogTreeNode descendant in Flatten(node.Children ?? [])) yield return descendant;
        }
    }

    private static bool SameLocator(CatalogTreeNodeLocator left, CatalogTreeNodeLocator right) =>
        string.Equals(left.RootName, right.RootName, StringComparison.Ordinal) &&
        string.Equals(left.NodeType, right.NodeType, StringComparison.Ordinal) &&
        string.Equals(left.NodeName, right.NodeName, StringComparison.Ordinal) &&
        (left.NodePath ?? []).SequenceEqual(right.NodePath ?? []) &&
        SameOptionalPath(left.AssetID, right.AssetID) &&
        string.Equals(left.MapType, right.MapType, StringComparison.Ordinal) &&
        SameOptionalPath(left.Filename, right.Filename) &&
        SameOptionalPath(left.FilenameRelative, right.FilenameRelative) &&
        SameOptionalPath(left.ParentGeometryAssetID, right.ParentGeometryAssetID) &&
        (left.ParentGeometryNodePath ?? []).SequenceEqual(right.ParentGeometryNodePath ?? []) &&
        string.Equals(left.ParentGeometryNodeName, right.ParentGeometryNodeName, StringComparison.Ordinal) &&
        SameOptionalPath(left.AssociationAssetID, right.AssociationAssetID) &&
        string.Equals(left.AssociationKind, right.AssociationKind, StringComparison.Ordinal) &&
        SameOptionalPath(left.ParentPlanAssetID, right.ParentPlanAssetID) &&
        (left.ParentPlanNodePath ?? []).SequenceEqual(right.ParentPlanNodePath ?? []) &&
        string.Equals(left.ParentPlanNodeName, right.ParentPlanNodeName, StringComparison.Ordinal) &&
        string.Equals(left.ParentResultNodeType, right.ParentResultNodeType, StringComparison.Ordinal) &&
        SameOptionalPath(left.ParentResultAssetID, right.ParentResultAssetID) &&
        (left.ParentResultNodePath ?? []).SequenceEqual(right.ParentResultNodePath ?? []) &&
        string.Equals(left.ParentResultNodeName, right.ParentResultNodeName, StringComparison.Ordinal) &&
        string.Equals(left.ParentResultRevision, right.ParentResultRevision, StringComparison.Ordinal) &&
        SameOptionalPath(left.ParentBoundaryConditionAssetID, right.ParentBoundaryConditionAssetID) &&
        (left.ParentBoundaryConditionNodePath ?? []).SequenceEqual(right.ParentBoundaryConditionNodePath ?? []) &&
        string.Equals(left.ParentBoundaryConditionNodeName, right.ParentBoundaryConditionNodeName, StringComparison.Ordinal);

    private static bool SameOptionalPath(string? left, string? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.Length == 0 || right.Length == 0
                ? string.Equals(left, right, StringComparison.Ordinal)
                : Path.IsPathRooted(left) && Path.IsPathRooted(right)
                    ? PathComparer.Equals(SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right))
                    : string.Equals(left, right, StringComparison.Ordinal);

    private static bool SameSelectionOverrides(MapResultSelectionOverride[]? left,
        MapResultSelectionOverride[]? right)
    {
        MapResultSelectionOverride[] first = left ?? [];
        MapResultSelectionOverride[] second = right ?? [];
        if (first.Length != second.Length) return false;
        return first.Zip(second).All(pair => SameLocator(pair.First.Node, pair.Second.Node) &&
            pair.First.SelectionIndex == pair.Second.SelectionIndex &&
            string.Equals(pair.First.Mode, pair.Second.Mode, StringComparison.Ordinal) &&
            SameOptionalPath(pair.First.ResultPath, pair.Second.ResultPath) &&
            string.Equals(pair.First.ResultRevision, pair.Second.ResultRevision, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSHA256(string value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');
}

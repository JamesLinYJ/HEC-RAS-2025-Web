using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Classifiers;
using Ras.Layers.Terrains;
using Utility.DataStructures;
using Utility.Units;

namespace Ras.Backend;

/// <summary>
/// Read-only viewport sampling for the actual Terrain, NValue and
/// Classification source objects referenced by saved map-tree rows. Geometry
/// and association locators are resolved inside one map snapshot and then
/// checked against the retained Project object graph; equal display names are
/// never used as association identity.
/// </summary>
public static class AssociatedMapLayersService
{
    private const int MaximumRasterSide = 2048;
    private const long MaximumRasterPixels = 2_097_152;
    private const string Available = "available";
    private const string Hidden = "hidden";
    private const string Empty = "empty";
    private const string Unavailable = "unavailable";
    private const string Failed = "failed";

    internal sealed class GeoprocessingTerrainSourceLease : IDisposable
    {
        private readonly Action ensureUnchanged;
        private Project? project;
        private ResolvedMapResultContext? resultContext;

        internal Terrain Terrain { get; }
        internal string SourcePath { get; }
        internal string SourceRevision { get; }

        internal GeoprocessingTerrainSourceLease(Terrain terrain, string sourcePath,
            string sourceRevision, Project project, ResolvedMapResultContext? resultContext,
            Action ensureUnchanged)
        {
            Terrain = terrain;
            SourcePath = sourcePath;
            SourceRevision = sourceRevision;
            this.project = project;
            this.resultContext = resultContext;
            this.ensureUnchanged = ensureUnchanged;
        }

        internal void EnsureUnchanged() => ensureUnchanged();

        public void Dispose()
        {
            ResolvedMapResultContext? result = Interlocked.Exchange(ref resultContext, null);
            Project? owner = Interlocked.Exchange(ref project, null);
            result?.Dispose();
            if (owner is not null) DisposeProject(owner);
        }
    }

    public static AssociatedMapLayersSnapshot Load(AssociatedMapLayersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        string projectAliasPath = Path.GetFullPath(request.Map.ProjectPath);
        if (!File.Exists(projectAliasPath))
            throw new FileNotFoundException("The project file was not found.", projectAliasPath);
        string projectCanonicalPath = SourceFilePaths.Canonical(projectAliasPath);
        string projectRevision = Revision(projectAliasPath);
        string projectXmlSHA = ResolveProjectXmlSHA(request.Map);
        MapTreeRequest selectedMap = request.Map;
        MapTreeSnapshot mapTree;

        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectAliasPath,
            request.Map.ProjectXml is { Length: > 0 } projectXml
                ? XElement.Parse(projectXml, LoadOptions.PreserveWhitespace) : null,
            request.Map.CoreLayerContext);
        ResolvedMapResultContext? resultContext = null;
        try
        {

            CatalogTreeNodeLocator[] requestedPlans = request.PlanNodes ?? [];
            MapResultGeometryOwner[] requestedResultOwners = request.ResultOwners ?? [];
            ResolvedResult[] resolvedResults = [];
            if (requestedResultOwners.Length > 0)
            {
                ValidateResultOwnerBatch(request.Map, requestedResultOwners);
                MapResultGeometryOwner firstOwner = requestedResultOwners[0];
                resultContext = MapResultChartService.ResolveContext(request.Map,
                    firstOwner.SelectionOverrides, firstOwner.ContextToken);
                // Result-owned child locators include the selected Result HDF
                // revision. ResolveContext merges the slot-zero selection into
                // Map.ResultSelectionOverrides and returns the matching tree;
                // rebuilding from request.Map alone would silently use the
                // default Latest Result and reject Previous/Explicit rows.
                selectedMap = resultContext.Map;
                mapTree = resultContext.Tree;
                resolvedResults = requestedResultOwners
                    .Select(owner => ResolveResultOwner(resultContext, mapTree.Roots, owner)).ToArray();
                if (resolvedResults.Select(result => NodeIdentity(result.Owner.ResultNode))
                    .Distinct(StringComparer.Ordinal).Count() != resolvedResults.Length)
                    throw new ArgumentException("The selected Result owners contain duplicates.");
            }
            else
            {
                mapTree = MapTreeService.Load(selectedMap);
            }
            string visualizationRevision = mapTree.VisualizationRevision;
            CatalogTreeNode[] geometryRows = request.GeometryNodes
                .Select(locator => ResolveExactNode(mapTree.Roots, locator, request.Map.MapType,
                    geometry: true, resultOwners: resolvedResults))
                .ToArray();
            if (geometryRows.Select(node => NodeIdentity(node.Locator!)).Distinct(StringComparer.Ordinal).Count()
                != geometryRows.Length)
                throw new ArgumentException("The selected Geometry rows contain duplicates.");

            ResolvedGeometry[] selectedGeometries = geometryRows
                .Select(node => ResolveGeometry(project, node, mapTree.Roots, resolvedResults)).ToArray();
            ResolvedPlan[] selectedPlans = requestedPlans
                .Select(locator => ResolvePlan(project, mapTree.Roots, locator)).ToArray();
            if (selectedPlans.Select(plan => NodeIdentity(plan.Locator)).Distinct(StringComparer.Ordinal).Count()
                != selectedPlans.Length)
                throw new ArgumentException("The selected Plan rows contain duplicates.");
            if (request.Map.MapType == "Plan")
                VerifyCompleteFocusedPlanOwners(mapTree.Roots, selectedPlans);
            ResolvedAssociation[] resolvedLayers = request.AssociationNodes.Select(locator =>
            {
                CatalogTreeNode row = ResolveExactNode(mapTree.Roots, locator, request.Map.MapType, geometry: false);
                return ResolveAssociation(project, resultContext?.Project, request.Map.MapType, geometryRows,
                    selectedGeometries, selectedPlans, resolvedResults, mapTree.Roots, row);
            }).ToArray();

            if (resolvedLayers.Select(layer => NodeIdentity(layer.Locator)).Distinct(StringComparer.Ordinal).Count()
                != resolvedLayers.Length)
                throw new ArgumentException("The selected association rows contain duplicates.");

            resolvedLayers = ApplyRasterRuntimeOptions(resolvedLayers, request.RasterRuntimeOptions ?? []);

            string contextToken = ContextToken(request, projectCanonicalPath, projectRevision,
                projectXmlSHA, visualizationRevision, selectedGeometries, selectedPlans,
                resolvedResults, resolvedLayers);
            if (request.ContextToken is { Length: > 0 } expectedToken &&
                !string.Equals(expectedToken, contextToken, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            // The request's dimensions remain part of its context identity and
            // retain the per-request validation above. Only the actual render
            // copy is constrained, so its viewport, locators, source leases,
            // dependencies and context token keep their original meaning.
            (int renderWidth, int renderHeight) = AssociatedMapRasterBudget.Constrain(
                request.Width, request.Height, resolvedLayers.Length);
            AssociatedMapLayersRequest renderRequest = request with
            {
                Map = selectedMap,
                Width = renderWidth,
                Height = renderHeight
            };
            AssociatedMapLayerSnapshot[] snapshots = resolvedLayers.Select(layer =>
                RenderLayer(layer, renderRequest, project.Units, visualizationRevision)).ToArray();

            // Source objects may keep open raster readers, so check every path
            // and byte revision after rendering as well as before it.
            VerifyProjectIdentity(projectAliasPath, projectCanonicalPath, projectRevision);
            foreach (ResolvedGeometry geometry in selectedGeometries)
                VerifyGeometryRevision(geometry);
            foreach (ResolvedPlan plan in selectedPlans)
                VerifyPlanRevision(plan);
            foreach (ResolvedResult result in resolvedResults)
                VerifyResultOwner(result);
            foreach (ResolvedAssociation layer in resolvedLayers)
                VerifySourceRevisions(layer);
            resultContext?.EnsureUnchanged();

            MapTreeSnapshot finalTree = MapTreeService.Load(selectedMap with
            { ExpectedVisualizationRevision = visualizationRevision });
            foreach (ResolvedAssociation layer in resolvedLayers)
                _ = ResolveExactNode(finalTree.Roots, layer.Locator, request.Map.MapType, geometry: false);
            foreach (CatalogTreeNode row in geometryRows)
                _ = ResolveExactNode(finalTree.Roots, row.Locator!, request.Map.MapType,
                    geometry: true, resultOwners: resolvedResults);
            foreach (ResolvedPlan plan in selectedPlans)
                _ = ResolvePlan(project, finalTree.Roots, plan.Locator);
            if (resultContext is not null)
            {
                foreach (ResolvedResult result in resolvedResults)
                    _ = ResolveResultOwner(resultContext, finalTree.Roots, result.Owner);
            }
            foreach (ResolvedGeometry geometry in selectedGeometries)
                VerifyGeometryRevision(geometry);
            foreach (ResolvedPlan plan in selectedPlans)
                VerifyPlanRevision(plan);
            foreach (ResolvedResult result in resolvedResults)
                VerifyResultOwner(result);
            foreach (ResolvedAssociation layer in resolvedLayers)
                VerifySourceRevisions(layer);
            resultContext?.EnsureUnchanged();
            VerifyProjectIdentity(projectAliasPath, projectCanonicalPath, projectRevision);
            if (!string.Equals(finalTree.VisualizationRevision, visualizationRevision, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            return new AssociatedMapLayersSnapshot(request.Map.MapID, request.Map.MapIndex,
                request.Map.MapName, request.Map.MapType, projectCanonicalPath, projectRevision,
                projectXmlSHA, visualizationRevision, contextToken,
                request.Viewport, snapshots);
        }
        finally
        {
            resultContext?.Dispose();
            DisposeProject(project);
        }
    }

    /// <summary>
    /// Opens the actual retained Terrain sampler for geoprocessing after
    /// resolving the complete current map association and its Geometry/Plan/
    /// Result owners. This shares the same source identity and revision rules
    /// as map association rendering but returns no rendered pixels.
    /// </summary>
    internal static GeoprocessingTerrainSourceLease OpenGeoprocessingTerrainSource(
        GeoprocessingTerrainRasterSourceCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        AssociatedMapLayersRequest request = capture.Request
            ?? throw new ArgumentException("A current associated-layer request is required.");
        ValidateRequest(request);
        if (capture.Node is null || capture.Node.NodeType != "Terrain" ||
            request.AssociationNodes.Count(locator => SameLocator(locator, capture.Node)) != 1 ||
            string.IsNullOrWhiteSpace(capture.SourcePath) || !Path.IsPathFullyQualified(capture.SourcePath) ||
            !IsSHA256(capture.SourceRevision) || capture.ExpectedExtent is not { Length: 4 } expectedExtent ||
            expectedExtent.Any(value => !double.IsFinite(value)) ||
            expectedExtent[0] >= expectedExtent[2] || expectedExtent[1] >= expectedExtent[3] ||
            capture.Dependencies is null || capture.Dependencies.Length is 0 or > 256)
            throw new ArgumentException("The exact Terrain source capture is incomplete.");

        string projectAliasPath = Path.GetFullPath(request.Map.ProjectPath);
        if (!File.Exists(projectAliasPath))
            throw new FileNotFoundException("The project file was not found.", projectAliasPath);
        string projectCanonicalPath = SourceFilePaths.Canonical(projectAliasPath);
        string projectRevision = Revision(projectAliasPath);
        string projectXmlSHA = ResolveProjectXmlSHA(request.Map);
        MapResultComputeReadLeaseScope? readLease = MapResultComputeReadLeaseScope.Resolve(
            request.Map, projectCanonicalPath);
        if (readLease?.IsProtected(capture.SourcePath) == true ||
            capture.Dependencies.Any(dependency => !string.IsNullOrWhiteSpace(dependency.Path) &&
                readLease?.IsProtected(dependency.Path) == true))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectAliasPath,
            request.Map.ProjectXml is { Length: > 0 } xml
                ? XElement.Parse(xml, LoadOptions.PreserveWhitespace) : null,
            request.Map.CoreLayerContext,
            sourcePath => readLease?.IsProtected(sourcePath) != true);
        ResolvedMapResultContext? resultContext = null;
        bool transferred = false;
        try
        {
            MapTreeRequest selectedMap = request.Map;
            MapTreeSnapshot mapTree;
            MapResultGeometryOwner[] resultOwners = request.ResultOwners ?? [];
            ResolvedResult[] resolvedResults = [];
            if (resultOwners.Length > 0)
            {
                ValidateResultOwnerBatch(request.Map, resultOwners);
                MapResultGeometryOwner owner = resultOwners[0];
                resultContext = MapResultChartService.ResolveContext(request.Map,
                    owner.SelectionOverrides, owner.ContextToken);
                selectedMap = resultContext.Map;
                mapTree = resultContext.Tree;
                resolvedResults = resultOwners.Select(item =>
                    ResolveResultOwner(resultContext, mapTree.Roots, item)).ToArray();
            }
            else
            {
                mapTree = MapTreeService.Load(selectedMap);
            }

            CatalogTreeNodeLocator[] geometryLocators = request.GeometryNodes;
            CatalogTreeNode[] geometryRows = geometryLocators.Select(locator =>
                ResolveExactNode(mapTree.Roots, locator, request.Map.MapType,
                    geometry: true, resultOwners: resolvedResults)).ToArray();
            ResolvedGeometry[] geometries = geometryRows
                .Select(node => ResolveGeometry(project, node, mapTree.Roots, resolvedResults)).ToArray();
            ResolvedPlan[] plans = (request.PlanNodes ?? []).Select(locator =>
                ResolvePlan(project, mapTree.Roots, locator)).ToArray();
            if (request.Map.MapType == "Plan") VerifyCompleteFocusedPlanOwners(mapTree.Roots, plans);

            ResolvedAssociation[] associations = request.AssociationNodes.Select(locator =>
            {
                CatalogTreeNode row = ResolveExactNode(mapTree.Roots, locator,
                    request.Map.MapType, geometry: false);
                return ResolveAssociation(project, resultContext?.Project, request.Map.MapType,
                    geometryRows, geometries, plans, resolvedResults, mapTree.Roots, row);
            }).ToArray();
            associations = ApplyRasterRuntimeOptions(associations, request.RasterRuntimeOptions ?? []);
            string contextToken = ContextToken(request, projectCanonicalPath, projectRevision,
                projectXmlSHA, mapTree.VisualizationRevision, geometries, plans,
                resolvedResults, associations);
            if (request.ContextToken is { Length: > 0 } expectedToken &&
                !string.Equals(expectedToken, contextToken, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            ResolvedAssociation[] sourceMatches = associations.Where(layer =>
                SameLocator(layer.Locator, capture.Node)).ToArray();
            if (sourceMatches.Length != 1 || sourceMatches[0].Source is not Terrain terrain ||
                !sourceMatches[0].IsChecked || !sourceMatches[0].EffectiveVisible ||
                !SamePath(sourceMatches[0].AssetPath, capture.SourcePath) ||
                !string.Equals(sourceMatches[0].Revision, capture.SourceRevision,
                    StringComparison.OrdinalIgnoreCase) ||
                !SameDependencies(sourceMatches[0], capture.Dependencies))
                throw new SourceFileChangedException();

            Extent extent = terrain.ValidExtent;
            if (!extent.IsFiniteAndValid() || extent.MinX != expectedExtent[0] ||
                extent.MinY != expectedExtent[1] || extent.MaxX != expectedExtent[2] ||
                extent.MaxY != expectedExtent[3])
                throw new SourceFileChangedException();

            void EnsureCurrent()
            {
                VerifyProjectIdentity(projectAliasPath, projectCanonicalPath, projectRevision);
                foreach (ResolvedGeometry geometry in geometries) VerifyGeometryRevision(geometry);
                foreach (ResolvedPlan plan in plans) VerifyPlanRevision(plan);
                foreach (ResolvedResult result in resolvedResults) VerifyResultOwner(result);
                foreach (ResolvedAssociation association in associations) VerifySourceRevisions(association);
                resultContext?.EnsureUnchanged();
                MapTreeSnapshot currentTree = MapTreeService.Load(selectedMap with
                { ExpectedVisualizationRevision = mapTree.VisualizationRevision });
                if (!string.Equals(currentTree.VisualizationRevision, mapTree.VisualizationRevision,
                        StringComparison.Ordinal))
                    throw new SourceFileChangedException();
                _ = ResolveExactNode(currentTree.Roots, capture.Node, request.Map.MapType, geometry: false);
                foreach (CatalogTreeNodeLocator locator in geometryLocators)
                    _ = ResolveExactNode(currentTree.Roots, locator, request.Map.MapType,
                        geometry: true, resultOwners: resolvedResults);
                foreach (ResolvedPlan plan in plans) _ = ResolvePlan(project, currentTree.Roots, plan.Locator);
                foreach (ResolvedResult result in resolvedResults)
                    _ = ResolveResultOwner(resultContext!, currentTree.Roots, result.Owner);
                if (!string.Equals(ContextToken(request, projectCanonicalPath, projectRevision,
                        projectXmlSHA, mapTree.VisualizationRevision, geometries, plans,
                        resolvedResults, associations), contextToken, StringComparison.Ordinal))
                    throw new SourceFileChangedException();
            }

            EnsureCurrent();
            var lease = new GeoprocessingTerrainSourceLease(terrain, sourceMatches[0].AssetPath,
                sourceMatches[0].Revision, project, resultContext, EnsureCurrent);
            transferred = true;
            return lease;
        }
        finally
        {
            if (!transferred)
            {
                resultContext?.Dispose();
                DisposeProject(project);
            }
        }
    }

    private static bool SameDependencies(ResolvedAssociation source,
        AssociatedMapLayerDependency[] expected)
    {
        AssociatedMapLayerDependency[] actual = source.Dependencies.Items
            .Concat(source.RelationDependencies)
            .GroupBy(item => (item.Path, item.Role), new DependencyKeyComparer())
            .Select(group => group.First())
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, PathComparer).ToArray();
        AssociatedMapLayerDependency[] orderedExpected = expected
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, PathComparer).ToArray();
        return actual.Length == orderedExpected.Length && actual.Zip(orderedExpected).All(pair =>
            SamePathNullable(pair.First.Path, pair.Second.Path) &&
            string.Equals(pair.First.Role, pair.Second.Role, StringComparison.Ordinal) &&
            string.Equals(pair.First.Revision, pair.Second.Revision, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateRequest(AssociatedMapLayersRequest request)
    {
        if (request.Map is null || request.GeometryNodes is null || request.AssociationNodes is null)
            throw new ArgumentException("A saved map and exact source association locators are required.");
        if (request.Map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("Associated source rendering is supported only for saved Project, Buildable, Geometry and Plan maps.");
        if (!Path.IsPathFullyQualified(request.Map.ProjectPath) || string.IsNullOrWhiteSpace(request.Map.MapID) ||
            request.Map.MapIndex < 0 || string.IsNullOrWhiteSpace(request.Map.MapName))
            throw new ArgumentException("The saved map identity is incomplete.");
        CatalogTreeNodeLocator[] planNodes = request.PlanNodes ?? [];
        MapResultGeometryOwner[] resultOwners = request.ResultOwners ?? [];
        bool planRoute = request.Map.MapType == "Plan";
        if (request.GeometryNodes.Length > 256 || (!planRoute && request.GeometryNodes.Length == 0 && resultOwners.Length == 0))
            throw new ArgumentOutOfRangeException(nameof(request.GeometryNodes), "Select between 1 and 256 source Geometry rows.");
        if (planNodes.Length > 256 || planRoute && planNodes.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(request.PlanNodes), "Select between 1 and 256 source Plan rows.");
        if (resultOwners.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(request.ResultOwners), "At most 256 source Result owners can be rendered at once.");
        if (request.AssociationNodes.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(request.AssociationNodes), "At most 256 source association rows can be rendered at once.");
        if (request.Width is < 1 or > MaximumRasterSide || request.Height is < 1 or > MaximumRasterSide ||
            (long)request.Width * request.Height > MaximumRasterPixels)
            throw new ArgumentOutOfRangeException(nameof(request.Width), "The associated-layer viewport exceeds the supported raster dimensions.");
        MapGeometryViewport viewport = request.Viewport;
        if (!double.IsFinite(viewport.MinX) || !double.IsFinite(viewport.MinY) ||
            !double.IsFinite(viewport.MaxX) || !double.IsFinite(viewport.MaxY) ||
            viewport.MaxX <= viewport.MinX || viewport.MaxY <= viewport.MinY ||
            !double.IsFinite(viewport.MaxX - viewport.MinX) || !double.IsFinite(viewport.MaxY - viewport.MinY))
            throw new ArgumentException("The map viewport must be a finite, non-empty world-coordinate extent.");
        if (request.GeometryNodes.Any(locator => locator is null) ||
            request.AssociationNodes.Any(locator => locator is null) ||
            planNodes.Any(locator => locator is null) || resultOwners.Any(owner => owner is null))
            throw new ArgumentException("A map node locator is missing.");
        AssociatedMapLayerRasterRuntimeOption[] rasterOptions = request.RasterRuntimeOptions ?? [];
        if (rasterOptions.Length > request.AssociationNodes.Length ||
            rasterOptions.Any(option => option is null || option.Locator is null ||
                string.IsNullOrWhiteSpace(option.AssetPath) || !Path.IsPathFullyQualified(option.AssetPath) ||
                option.Locator.NodeType is not ("Terrain" or "NValue")) ||
            rasterOptions.Select(option => NodeIdentity(option.Locator))
                .Distinct(StringComparer.Ordinal).Count() != rasterOptions.Length)
            throw new ArgumentException("Raster runtime options require active, visible Terrain or N-value source rows.");
        foreach (AssociatedMapLayerRasterRuntimeOption option in rasterOptions)
        {
            if (request.AssociationNodes.Count(locator => SameLocator(locator, option.Locator)) != 1)
                throw new ArgumentException("A raster runtime option does not match one exact selected association row.");
        }
    }

    private static ResolvedAssociation[] ApplyRasterRuntimeOptions(
        ResolvedAssociation[] layers,
        AssociatedMapLayerRasterRuntimeOption[] options)
    {
        var byIdentity = new Dictionary<string, AssociatedMapLayerRasterRuntimeOption>(StringComparer.Ordinal);
        foreach (AssociatedMapLayerRasterRuntimeOption option in options)
        {
            string identity = NodeIdentity(option.Locator);
            ResolvedAssociation[] matches = layers.Where(layer =>
                SameLocator(layer.Locator, option.Locator)).ToArray();
            if (matches.Length != 1 || matches[0].Source is not (Terrain or NValueLayer) ||
                !SamePath(matches[0].AssetPath, option.AssetPath) || !byIdentity.TryAdd(identity, option))
                throw new SourceFileChangedException();
            if (!matches[0].IsChecked || !matches[0].EffectiveVisible)
                throw new ArgumentException("Raster runtime options require active, visible Terrain or N-value source rows.");
        }

        return layers.Select(layer =>
        {
            bool isRasterRenderer = layer.Source is Terrain or NValueLayer;
            AssociatedMapLayerRasterRuntimeOption? option = byIdentity.GetValueOrDefault(NodeIdentity(layer.Locator));
            return layer with
            {
                PlotRasterTileOutlines = isRasterRenderer
                    ? option?.PlotRasterTileOutlines ?? false
                    : null
            };
        }).ToArray();
    }

    private static string ResolveProjectXmlSHA(MapTreeRequest request)
    {
        if (request.ProjectXml is not { } xml)
            return request.ProjectXmlSHA ?? "";
        string computed = Hash(Encoding.UTF8.GetBytes(xml));
        if (request.ProjectXmlSHA is { Length: > 0 } expected &&
            !string.Equals(expected, computed, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        return computed;
    }

    private static CatalogTreeNode ResolveExactNode(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator requested, string mapType, bool geometry,
        IReadOnlyList<ResolvedResult>? resultOwners = null)
    {
        bool planOwner = geometry && mapType == "Plan" && requested.NodeType == "Plan";
        bool hasResultParentIdentity = requested.ParentResultNodeType is not null ||
            requested.ParentResultAssetID is not null || requested.ParentResultNodePath is not null ||
            requested.ParentResultNodeName is not null || requested.ParentResultRevision is not null;
        bool resultOwnedGeometry = false;
        if (geometry && hasResultParentIdentity)
        {
            if (requested.NodeType != "Geometry" ||
                requested.ParentResultNodeType is not ("Result" or "MultiResult") ||
                requested.ParentResultAssetID is not { Length: > 0 } parentAsset ||
                !Path.IsPathFullyQualified(parentAsset) ||
                requested.ParentResultNodePath is not { Length: > 0 } parentPath ||
                requested.ParentResultNodeName is not { Length: > 0 } ||
                requested.ParentResultRevision is not { Length: > 0 } parentRevision ||
                requested.AssetID is not { Length: > 0 } geometryAsset ||
                !Path.IsPathFullyQualified(geometryAsset) ||
                !IsSHA256(parentRevision))
                throw new SourceFileChangedException();

            ResolvedResult resultOwner = ResolveResultForChild(requested, resultOwners ?? []);
            if (!string.Equals(requested.RootName, resultOwner.Owner.ResultNode.RootName, StringComparison.Ordinal) ||
                !SamePath(geometryAsset, resultOwner.Owner.SelectedResultPath) ||
                !string.Equals(requested.NodeName, resultOwner.Geometry.Name, StringComparison.Ordinal) ||
                !SameIndexPath(parentPath, resultOwner.Owner.ResultNode.NodePath))
                throw new SourceFileChangedException();
            resultOwnedGeometry = true;
        }

        string[] validRoots = planOwner ? ["Focused Plans"] :
            geometry ? GeometryRoots(mapType) : AssociationRoots(mapType);
        if ((!validRoots.Contains(requested.RootName, StringComparer.Ordinal) && !resultOwnedGeometry) ||
            geometry && !planOwner && requested.NodeType != "Geometry" ||
            !geometry && requested.NodeType is not ("Terrain" or "NValue" or "Classification") ||
            string.IsNullOrWhiteSpace(requested.AssetID) || string.IsNullOrWhiteSpace(requested.NodeName))
            throw new SourceFileChangedException();

        CatalogTreeNode[] matches = Walk(roots).Where(node => node.Locator is { } locator &&
            SameLocator(locator, requested)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static bool SameLocator(CatalogTreeNodeLocator actual, CatalogTreeNodeLocator expected)
    {
        if (!string.Equals(actual.RootName, expected.RootName, StringComparison.Ordinal) ||
            !string.Equals(actual.NodeType, expected.NodeType, StringComparison.Ordinal) ||
            !string.Equals(actual.NodeName, expected.NodeName, StringComparison.Ordinal) ||
            !SamePathNullable(actual.AssetID, expected.AssetID) ||
            !SamePathNullable(actual.AssociationAssetID, expected.AssociationAssetID) ||
            !string.Equals(actual.AssociationKind, expected.AssociationKind, StringComparison.Ordinal) ||
            !string.Equals(actual.MapType, expected.MapType, StringComparison.Ordinal) ||
            !string.Equals(actual.Filename, expected.Filename, StringComparison.Ordinal) ||
            !string.Equals(actual.FilenameRelative, expected.FilenameRelative, StringComparison.Ordinal) ||
            !SamePathNullable(actual.ParentGeometryAssetID, expected.ParentGeometryAssetID) ||
            !SameIndexPath(actual.NodePath, expected.NodePath) ||
            !SameIndexPath(actual.ParentGeometryNodePath, expected.ParentGeometryNodePath) ||
            !string.Equals(actual.ParentGeometryNodeName, expected.ParentGeometryNodeName, StringComparison.Ordinal) ||
            !SamePathNullable(actual.ParentPlanAssetID, expected.ParentPlanAssetID) ||
            !SameIndexPath(actual.ParentPlanNodePath, expected.ParentPlanNodePath) ||
            !string.Equals(actual.ParentPlanNodeName, expected.ParentPlanNodeName, StringComparison.Ordinal) ||
            !string.Equals(actual.ParentResultNodeType, expected.ParentResultNodeType, StringComparison.Ordinal) ||
            !SamePathNullable(actual.ParentResultAssetID, expected.ParentResultAssetID) ||
            !SameIndexPath(actual.ParentResultNodePath, expected.ParentResultNodePath) ||
            !string.Equals(actual.ParentResultNodeName, expected.ParentResultNodeName, StringComparison.Ordinal) ||
            !string.Equals(actual.ParentResultRevision, expected.ParentResultRevision, StringComparison.OrdinalIgnoreCase) ||
            !SamePathNullable(actual.ParentBoundaryConditionAssetID, expected.ParentBoundaryConditionAssetID) ||
            !SameIndexPath(actual.ParentBoundaryConditionNodePath, expected.ParentBoundaryConditionNodePath) ||
            !string.Equals(actual.ParentBoundaryConditionNodeName, expected.ParentBoundaryConditionNodeName, StringComparison.Ordinal))
            return false;
        return true;
    }

    private static string[] GeometryRoots(string mapType) => mapType switch
    {
        "Project" => ["Geometries"],
        "Buildable" => ["Layers"],
        "Geometry" => ["Focused Geometries"],
        "Plan" => ["Focused Plans"],
        _ => []
    };

    private static string[] AssociationRoots(string mapType) => mapType switch
    {
        "Project" => ["Geometries", "Terrains", "NValues", "Classifications"],
        "Buildable" => ["Layers"],
        "Geometry" => ["Focused Geometries", "Matching Layers", "Layers"],
        "Plan" => ["Focused Plans", "Matching Layers", "Layers"],
        _ => []
    };

    private static ResolvedPlan ResolvePlan(Project project, CatalogTreeNode[] roots,
        CatalogTreeNodeLocator locator)
    {
        if (!FocusedPlanLocators(roots).Any(candidate => SameLocator(candidate, locator)))
            throw new SourceFileChangedException();
        CatalogTreeNode row = ResolveExactNode(roots, locator, "Plan", geometry: true);
        if (locator.NodeType != "Plan")
            throw new SourceFileChangedException();
        Plan[] matches = project.Plans.Where(plan =>
            SamePath(ProjectPlanPath(project, plan.Filename), locator.AssetID!) &&
            string.Equals(plan.Name, locator.NodeName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        string path = CanonicalExisting(locator.AssetID!);
        if (!SamePath(path, ProjectPlanPath(project, matches[0].Filename)))
            throw new SourceFileChangedException();
        return new(locator, path, Revision(path), matches[0],
            string.Equals(row.Checked, "checked", StringComparison.Ordinal),
            MapTreeService.IsEffectivelyVisible(roots, row));
    }

    private static void VerifyCompleteFocusedPlanOwners(CatalogTreeNode[] roots,
        IReadOnlyList<ResolvedPlan> requested)
    {
        CatalogTreeNodeLocator[] actual = FocusedPlanLocators(roots);
        CatalogTreeNodeLocator[] supplied = requested.Select(plan => plan.Locator).ToArray();
        if (actual.Length != supplied.Length || !actual.Zip(supplied).All(pair => SameLocator(pair.First, pair.Second)))
            throw new SourceFileChangedException();
    }

    private static CatalogTreeNodeLocator[] FocusedPlanLocators(CatalogTreeNode[] roots)
    {
        CatalogTreeNode[] focusedRoots = roots.Where(root => root.Locator is
            { RootName: "Focused Plans", NodeType: "Root", NodeName: "Focused Plans" }).ToArray();
        if (focusedRoots.Length != 1) throw new SourceFileChangedException();

        var plans = new List<CatalogTreeNodeLocator>();
        VisitFolders(focusedRoots[0].Children ?? []);
        return plans.ToArray();

        void VisitFolders(IEnumerable<CatalogTreeNode> nodes)
        {
            foreach (CatalogTreeNode node in nodes)
            {
                if (node.Locator is not { RootName: "Focused Plans" } locator ||
                    locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
                    locator.ParentPlanNodeName is not null || locator.ParentResultNodeType is not null ||
                    locator.ParentResultAssetID is not null || locator.ParentResultNodePath is not null ||
                    locator.ParentResultNodeName is not null || locator.ParentResultRevision is not null)
                    continue;
                if (locator.NodeType == "Plan")
                {
                    plans.Add(locator);
                    continue;
                }
                if (locator.NodeType == "Folder")
                    VisitFolders(node.Children ?? []);
            }
        }
    }

    private static ResolvedResult ResolveResultOwner(ResolvedMapResultContext context,
        CatalogTreeNode[] roots, MapResultGeometryOwner owner)
    {
        if (!string.Equals(owner.ContextToken, context.Sources.ContextToken, StringComparison.Ordinal) ||
            owner.SelectionIndex != 0 || owner.ResultNode.NodeType is not ("Result" or "MultiResult"))
            throw new SourceFileChangedException();
        CatalogTreeNode[] rows = Walk(roots).Where(node => node.Locator is { } locator &&
            SameLocator(locator, owner.ResultNode)).ToArray();
        if (rows.Length != 1)
            throw new SourceFileChangedException();
        ResolvedMapResultNode[] matches = context.ResultContexts.Where(item =>
            item.Selections.Any(selection => selection.SelectionIndex == 0 &&
                SameLocator(selection.Node, owner.ResultNode)) &&
            SamePath(item.ResultPath, owner.SelectedResultPath) &&
            string.Equals(item.Revision, owner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || owner.ResultNode.ParentResultNodeType is not null ||
            owner.ResultNode.ParentResultAssetID is not null || owner.ResultNode.ParentResultRevision is not null)
            throw new SourceFileChangedException();
        MapResultChartService.EnsureUnchanged(owner.SelectedResultPath, owner.SelectedResultRevision);
        return new(owner, matches[0], matches[0].Result.Geometry,
            string.Equals(rows[0].Checked, "checked", StringComparison.Ordinal),
            MapTreeService.IsEffectivelyVisible(roots, rows[0]));
    }

    private static ResolvedResult ResolveResultForChild(CatalogTreeNodeLocator child,
        IReadOnlyList<ResolvedResult> results)
    {
        int[]? parentPath = child.ParentResultNodePath;
        if (child.ParentResultNodeType is not { Length: > 0 } parentType ||
            child.ParentResultAssetID is not { Length: > 0 } parentAsset ||
            child.ParentResultNodeName is not { Length: > 0 } parentName ||
            child.ParentResultRevision is not { Length: > 0 } parentRevision)
            throw new SourceFileChangedException();
        ResolvedResult[] matches = results.Where(result =>
            result.Owner.ResultNode.NodeType == parentType &&
            string.Equals(result.Owner.ResultNode.RootName, child.RootName, StringComparison.Ordinal) &&
            SamePath(result.Owner.SelectedResultPath, parentAsset) &&
            result.Owner.ResultNode.NodeName == parentName &&
            string.Equals(result.Owner.SelectedResultRevision, parentRevision, StringComparison.OrdinalIgnoreCase) &&
            SameIndexPath(result.Owner.ResultNode.NodePath, parentPath) &&
            (child.NodePath is null || parentPath is null || IsDescendant(child.NodePath, parentPath))).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static bool IsDescendant(int[]? child, int[] parent) => child is { Length: > 0 } &&
        child.Length > parent.Length && child.Take(parent.Length).SequenceEqual(parent);

    private static ResolvedPlan ResolvePlanForChild(CatalogTreeNodeLocator child,
        IReadOnlyList<ResolvedPlan> plans)
    {
        int[]? parentPath = child.ParentPlanNodePath;
        if (child.ParentPlanAssetID is not { Length: > 0 } parentAsset ||
            child.ParentPlanNodeName is not { Length: > 0 } parentName)
            throw new SourceFileChangedException();
        ResolvedPlan[] matches = plans.Where(plan => SamePath(plan.Locator.AssetID!, parentAsset) &&
            plan.Locator.NodeName == parentName && SameIndexPath(plan.Locator.NodePath, parentPath) &&
            (child.NodePath is null || parentPath is null || IsDescendant(child.NodePath, parentPath))).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static void ValidateResultOwnerBatch(MapTreeRequest map,
        IReadOnlyList<MapResultGeometryOwner> owners)
    {
        if (map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan") ||
            owners.Any(owner => owner.SelectionIndex != 0 || string.IsNullOrWhiteSpace(owner.ContextToken) ||
                string.IsNullOrWhiteSpace(owner.SelectedResultPath) ||
                !Path.IsPathFullyQualified(owner.SelectedResultPath) || !IsSHA256(owner.SelectedResultRevision)) ||
            owners.Select(owner => owner.ContextToken).Distinct(StringComparer.Ordinal).Count() != 1 ||
            owners.Any(owner => !SameOverrides(owners[0].SelectionOverrides, owner.SelectionOverrides)))
            throw new SourceFileChangedException();
        if (owners.Select(owner => NodeIdentity(owner.ResultNode)).Distinct(StringComparer.Ordinal).Count() != owners.Count)
            throw new SourceFileChangedException();
    }

    private static bool SameOverrides(MapResultSelectionOverride[]? left,
        MapResultSelectionOverride[]? right)
    {
        left ??= [];
        right ??= [];
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
        {
            MapResultSelectionOverride a = left[index];
            MapResultSelectionOverride b = right[index];
            if (a.SelectionIndex != b.SelectionIndex || a.Mode != b.Mode ||
                !SameLocator(a.Node, b.Node) || !SamePathNullable(a.ResultPath, b.ResultPath) ||
                !string.Equals(a.ResultRevision, b.ResultRevision, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static ResolvedGeometry ResolveGeometry(Project project, CatalogTreeNode node,
        CatalogTreeNode[] roots, IReadOnlyList<ResolvedResult> resultOwners)
    {
        CatalogTreeNodeLocator locator = node.Locator!;
        if (locator.ParentResultNodeType is not null)
        {
            ResolvedResult owner = ResolveResultForChild(locator, resultOwners);
            Geometry geometry = owner.Geometry;
            string resultPath = CanonicalExisting(owner.Owner.SelectedResultPath);
            if (!SamePath(locator.AssetID!, resultPath) || !SamePath(geometry.Filename, resultPath) ||
                !string.Equals(locator.NodeName, geometry.Name, StringComparison.Ordinal))
                throw new SourceFileChangedException();
            return new ResolvedGeometry(locator, resultPath, owner.Owner.SelectedResultRevision,
                geometry, string.Equals(node.Checked, "checked", StringComparison.Ordinal),
                MapTreeService.IsEffectivelyVisible(roots, node), owner, null);
        }
        Geometry[] matches = project.Geometries.Where(geometry =>
            SamePath(geometry.Filename, locator.AssetID!) &&
            string.Equals(geometry.Name, locator.NodeName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new SourceFileChangedException();
        string path = CanonicalExisting(locator.AssetID!);
        if (!SamePath(path, matches[0].Filename)) throw new SourceFileChangedException();
        return new ResolvedGeometry(locator, path, Revision(path), matches[0],
            string.Equals(node.Checked, "checked", StringComparison.Ordinal),
            MapTreeService.IsEffectivelyVisible(roots, node), null, null);
    }

    private static ResolvedAssociation ResolveAssociation(Project project, Project? resultProject,
        string mapType,
        CatalogTreeNode[] geometryRows, ResolvedGeometry[] selectedGeometries,
        ResolvedPlan[] selectedPlans, ResolvedResult[] selectedResults,
        CatalogTreeNode[] roots, CatalogTreeNode row)
    {
        CatalogTreeNodeLocator locator = row.Locator!;
        string nodeType = locator.NodeType;
        if (locator.AssociationKind is { Length: > 0 } explicitKind &&
            !string.Equals(explicitKind, nodeType, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        string nodeAssetPath = row.AssetID ?? locator.AssetID!;
        string canonicalNodeAssetPath = CanonicalExisting(nodeAssetPath);
        if (locator.AssociationAssetID is { Length: > 0 } associationAsset &&
            !SamePath(associationAsset, canonicalNodeAssetPath))
            throw new SourceFileChangedException();

        Project sourceProject = project;
        object source;
        CatalogTreeNodeLocator[] matchedPlanNodes = [];
        MapResultGeometryOwner[] matchedResultOwners = [];
        AssociatedMapLayerDependency[] ownerDependencies = [];
        CatalogTreeNodeLocator[] matchedGeometryNodes;
        AssociatedMapLayerDependency[] geometryDependencies;
        if (locator.ParentResultNodeType is not null)
        {
            ResolvedResult owner = ResolveResultForChild(locator, selectedResults);
            // Result.Geometry and its associated source objects belong to the
            // Project instance held by the result resolver. Resolve the layer
            // in that same object graph so ReferenceEquals remains meaningful.
            sourceProject = resultProject ?? throw new SourceFileChangedException();
            object? related = AssociatedSource(owner.Geometry, nodeType);
            source = ResolveLayer(sourceProject, nodeType, canonicalNodeAssetPath, locator.NodeName);
            if (related is null || !ReferenceEquals(related, source) ||
                !SamePath(SourcePath(related), canonicalNodeAssetPath))
                throw new SourceFileChangedException();
            matchedResultOwners = [owner.Owner];
            ownerDependencies = [new AssociatedMapLayerDependency(owner.Owner.SelectedResultPath,
                owner.Owner.SelectedResultRevision, "result-hdf")];
            matchedGeometryNodes = geometryRows.Where(geometry => geometry.Locator is { } geometryLocator &&
                    SamePath(geometryLocator.AssetID ?? "", owner.Owner.SelectedResultPath) &&
                    geometryLocator.ParentResultNodeType == owner.Owner.ResultNode.NodeType &&
                    SamePathNullable(geometryLocator.ParentResultAssetID, owner.Owner.SelectedResultPath))
                .Select(geometry => geometry.Locator!).ToArray();
            geometryDependencies = [];
        }
        else if (locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
                 locator.ParentPlanNodeName is not null || mapType == "Plan")
        {
            ResolvedPlan[] candidates = locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
                locator.ParentPlanNodeName is not null
                ? [ResolvePlanForChild(locator, selectedPlans)]
                : selectedPlans;
            if (candidates.Length == 0) throw new SourceFileChangedException();
            source = ResolveLayer(project, nodeType, canonicalNodeAssetPath, locator.NodeName);
            bool sharedSourceFound = false;
            foreach (ResolvedPlan plan in candidates)
            {
                object? related = AssociatedSource(plan.Source, nodeType);
                if (related is null)
                {
                    if (locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
                        locator.ParentPlanNodeName is not null)
                        throw new SourceFileChangedException();
                    continue;
                }
                if (!ReferenceEquals(related, source) || !SamePath(SourcePath(related), canonicalNodeAssetPath))
                    throw new SourceFileChangedException();
                sharedSourceFound = true;
            }
            if (!sharedSourceFound) throw new SourceFileChangedException();
            // The retained matcher ignores null associations while finding a
            // common layer, but every focused Plan still contributed to that
            // decision and remains a dependency of the shared row.
            matchedPlanNodes = candidates.Select(owner => owner.Locator).ToArray();
            ownerDependencies = candidates.Select(owner => new AssociatedMapLayerDependency(
                owner.AssetPath, owner.Revision, "plan-hdf")).ToArray();
            matchedGeometryNodes = [];
            geometryDependencies = [];
        }
        else
        {
            source = ResolveLayer(project, nodeType, canonicalNodeAssetPath, locator.NodeName);
            var matchedGeometryNodeList = new List<CatalogTreeNodeLocator>();
            var matchedGeometryList = new List<Geometry>();
            for (int index = 0; index < selectedGeometries.Length; index++)
            {
                ResolvedGeometry selected = selectedGeometries[index];
                Geometry geometry = selected.Source;
                if (locator.ParentGeometryAssetID is { Length: > 0 } parentPath &&
                    !SamePath(parentPath, geometry.Filename)) continue;
                object? associated = AssociatedSource(geometry, nodeType);
                if (associated is null || !ReferenceEquals(associated, source)) continue;
                matchedGeometryList.Add(geometry);
                matchedGeometryNodeList.Add(selected.Locator);
            }
            if (matchedGeometryNodeList.Count == 0)
                throw new SourceFileChangedException();
            if (locator.ParentGeometryNodeName is { Length: > 0 } parentName &&
                !matchedGeometryList.Any(geometry => string.Equals(geometry.Name, parentName, StringComparison.Ordinal)))
                throw new SourceFileChangedException();
            if (locator.ParentGeometryNodePath is { } parentNodePath &&
                !matchedGeometryNodeList.Any(geometryLocator => SameIndexPath(geometryLocator.NodePath, parentNodePath)))
                throw new SourceFileChangedException();
            matchedGeometryNodes = matchedGeometryNodeList.ToArray();
            geometryDependencies = selectedGeometries
                .Where(selected => matchedGeometryNodes.Any(matched => SameLocator(matched, selected.Locator)))
                .Select(selected => new AssociatedMapLayerDependency(
                    selected.AssetPath, selected.Revision, "geometry-hdf")).ToArray();
        }
        string sourcePath = SourcePath(source);
        string canonicalSourcePath = CanonicalExisting(sourcePath);
        if (!SamePath(canonicalNodeAssetPath, canonicalSourcePath))
            throw new SourceFileChangedException();

        bool isChecked = string.Equals(row.Checked, "checked", StringComparison.Ordinal);
        bool effectiveVisible = MapTreeService.IsEffectivelyVisible(roots, row);
        string revision = Revision(canonicalSourcePath);
        DependencyCapture dependencyCapture = CaptureDependencies(sourceProject, source);
        string? sourceDiagnostic = SourceDiagnostic(source);
        return new ResolvedAssociation(locator, nodeType, canonicalSourcePath, revision, source,
            matchedGeometryNodes, matchedPlanNodes, matchedResultOwners,
            isChecked, effectiveVisible, dependencyCapture,
            [.. geometryDependencies, .. ownerDependencies], sourceDiagnostic);
    }

    private static object ResolveLayer(Project project, string nodeType, string canonicalPath, string name)
    {
        object[] matches = nodeType switch
        {
            "Terrain" => project.Terrains.Where(item => SamePath(item.Filename, canonicalPath) && item.Name == name)
                .Cast<object>().ToArray(),
            "NValue" => project.NValues.Where(item => SamePath(item.Filename, canonicalPath) && item.Name == name)
                .Cast<object>().ToArray(),
            "Classification" => project.ClassificationLayers.Where(item => SamePath(item.Filename, canonicalPath) && item.Name == name)
                .Cast<object>().ToArray(),
            _ => []
        };
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static object? AssociatedSource(Geometry geometry, string nodeType) => nodeType switch
    {
        "Terrain" => geometry.AssociatedTerrain,
        "NValue" => geometry.AssociatedNValueLayer,
        "Classification" => geometry.AssociatedNValueLayer?.AssociatedClassificationLayer,
        _ => null
    };

    private static object? AssociatedSource(Plan plan, string nodeType)
    {
        Geometry? geometry = plan.PlanAssociation.Geometry;
        return geometry is null ? null : AssociatedSource(geometry, nodeType);
    }

    private static string SourcePath(object source) => source switch
    {
        Terrain terrain => terrain.Filename,
        NValueLayer nvalue => nvalue.Filename,
        ClassificationLayer classification => classification.Filename,
        _ => throw new SourceFileChangedException()
    };

    private static DependencyCapture CaptureDependencies(Project project, object source)
    {
        var dependencies = new List<AssociatedMapLayerDependency>
        {
            new(SourceFilePaths.Canonical(SourcePath(source)), Revision(SourceFilePaths.Canonical(SourcePath(source))), "source-hdf")
        };
        string? diagnostic = null;
        switch (source)
        {
            case Terrain terrain:
                foreach (RasterTerrainInput input in terrain.AllRasterInputs())
                {
                    AssociatedMapLayerDependency dependency = RasterDependency(project, input.Filename, "terrain-input-raster");
                    dependencies.Add(dependency);
                    if (!input.IsValid(out string error)) diagnostic ??= error;
                }
                break;
            case ClassificationLayer classification:
                foreach (RasterClassificationInput input in classification.AllRasterInputs())
                {
                    AssociatedMapLayerDependency dependency = RasterDependency(project, input.Filename, "classification-input-raster");
                    dependencies.Add(dependency);
                    if (!input.IsValid(out string error)) diagnostic ??= error;
                }
                break;
            case NValueLayer nvalue when nvalue.Mode == NValueSource.ClassificationLayer:
            {
                ClassificationLayer? classification = nvalue.AssociatedClassificationLayer;
                if (classification is null || string.IsNullOrWhiteSpace(classification.Filename))
                {
                    diagnostic ??= "The N-value source has no available associated Classification source.";
                    break;
                }
                ClassificationLayer[] owned = project.ClassificationLayers.Where(candidate =>
                    SamePath(candidate.Filename, classification.Filename)).ToArray();
                if (owned.Length != 1 || !ReferenceEquals(owned[0], classification))
                {
                    diagnostic ??= "The N-value Classification source is not uniquely owned by the current project.";
                    break;
                }
                string classificationPath = SourceFilePaths.Canonical(classification.Filename);
                dependencies.Add(new(classificationPath, Revision(classificationPath), "classification-source-hdf"));
                foreach (RasterClassificationInput input in classification.AllRasterInputs())
                {
                    AssociatedMapLayerDependency dependency = RasterDependency(project, input.Filename, "classification-input-raster");
                    dependencies.Add(dependency);
                    if (!input.IsValid(out string error)) diagnostic ??= error;
                }
                if (classification.HasLoadErrors)
                    diagnostic ??= string.Join("\n", classification.LayerErrors.Select(error => error.EvaluatedMessage));
                break;
            }
        }
        dependencies = dependencies
            .GroupBy(item => (item.Path, item.Role), new DependencyKeyComparer())
            .Select(group => group.First())
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, PathComparer)
            .ToList();
        return new DependencyCapture(dependencies.ToArray(), diagnostic);
    }

    private static AssociatedMapLayerDependency RasterDependency(Project project, string inputPath, string role)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return new AssociatedMapLayerDependency("", "missing", role);
        string path = Path.IsPathRooted(inputPath) ? inputPath : Path.Combine(project.ProjectDirectory, inputPath);
        string canonical = SourceFilePaths.Canonical(path);
        return File.Exists(path)
            ? new AssociatedMapLayerDependency(canonical, Revision(path), role)
            : new AssociatedMapLayerDependency(canonical, "missing", role);
    }

    private static string? SourceDiagnostic(object source) => source switch
    {
        CoreLayerBase layer when layer.HasLoadErrors => string.Join("\n", layer.LayerErrors.Select(error => error.EvaluatedMessage)),
        NValueLayer nvalue when nvalue.Mode is not (NValueSource.Constant or NValueSource.ClassificationLayer) =>
            "The selected N-value source mode has no retained viewport resampler.",
        _ => null
    };

    private static AssociatedMapLayerSnapshot RenderLayer(ResolvedAssociation layer,
        AssociatedMapLayersRequest request, UnitSystem units, string visualizationRevision)
    {
        string status = Failed;
        string? diagnostic = layer.Diagnostic ?? layer.Dependencies.Diagnostic;
        byte[] rgba = [];
        AssociatedMapLayerLegend[] legend = [];
        MapGeometryViewport? sourceExtent = null;

        try
        {
            sourceExtent = SourceExtent(layer.Source);
            if (!layer.EffectiveVisible)
            {
                status = Hidden;
            }
            else if (diagnostic is not null)
            {
                status = layer.Diagnostic is not null && layer.Diagnostic.StartsWith("The selected N-value source mode", StringComparison.Ordinal)
                    ? Unavailable : Failed;
            }
            else
            {
                RenderedPixels pixels = layer.Source switch
                {
                    Terrain terrain => RenderTerrain(terrain, layer.Locator, layer.AssetPath,
                        layer.Revision, request, units, visualizationRevision,
                        layer.PlotRasterTileOutlines ?? false),
                    NValueLayer nvalue => RenderNValue(nvalue, layer.Locator, layer.AssetPath,
                        layer.Revision, request, units, visualizationRevision,
                        layer.PlotRasterTileOutlines ?? false),
                    ClassificationLayer classification => RenderClassification(classification,
                        request.Viewport, request.Width, request.Height),
                    _ => throw new SourceFileChangedException()
                };
                legend = pixels.Legend;
                if (pixels.ValidPixelCount == 0)
                {
                    status = Empty;
                }
                else
                {
                    status = Available;
                    rgba = pixels.Rgba;
                }
            }
        }
        catch (SourceFileChangedException)
        {
            throw;
        }
        catch (Exception error)
        {
            status = Failed;
            diagnostic = error.Message;
            rgba = [];
            legend = [];
        }

        return new AssociatedMapLayerSnapshot(layer.Locator, layer.Kind, layer.AssetPath,
            layer.Revision, layer.Dependencies.Items.Concat(layer.RelationDependencies)
                .GroupBy(item => (item.Path, item.Role), new DependencyKeyComparer())
                .Select(group => group.First())
                .OrderBy(item => item.Role, StringComparer.Ordinal)
                .ThenBy(item => item.Path, PathComparer)
                .ToArray(), status, diagnostic, layer.IsChecked,
            layer.EffectiveVisible, layer.GeometryNodes, request.Width, request.Height,
            rgba, legend, sourceExtent, layer.PlanNodes, layer.ResultOwners,
            layer.PlotRasterTileOutlines);
    }

    private static RenderedPixels RenderTerrain(Terrain terrain, CatalogTreeNodeLocator locator,
        string sourcePath, string sourceRevision, AssociatedMapLayersRequest request,
        UnitSystem units, string visualizationRevision, bool plotRasterTileOutlines)
    {
        Extent sourceExtent = terrain.ValidExtent;
        if (!IsFiniteExtent(sourceExtent)) return new([], [], 0);
        RasterSymbology symbology = AssociatedRasterStyleService.ResolveForRender(request.Map,
            locator, terrain, sourcePath, sourceRevision, visualizationRevision, units);
        symbology.ContourProperties.UnitLabel = DefaultUnits.FromDefault(units).ShortLengthUnits;
        float[] values = Sample(terrain, request.Viewport, request.Width, request.Height);
        float noData = terrain.NoDataValue ?? -9999f;
        int valid = values.Count(value => float.IsFinite(value) && value != noData);
        if (valid == 0) return new([], Legend(symbology.SurfaceFill), 0);

        byte[] rgba = RenderRasterPixels(terrain, symbology, request.Viewport,
            request.Width, request.Height, plotRasterTileOutlines);
        return new(rgba, Legend(symbology.SurfaceFill), valid);
    }

    /// <summary>
    /// Renders through the retained raster layer, applying the selected
    /// source RasterSymbology (including fill alpha, interpolation, hillshade
    /// and contours). Raster queries require square cells; a one-cell halo is
    /// rendered at the request's coarser world-units-per-pixel scale, then
    /// sampled back at the requested viewport's exact pixel centers.
    /// </summary>
    private static byte[] RenderRasterPixels(IResample<float> raster, RasterSymbology symbology,
        MapGeometryViewport viewport, int width, int height, bool plotRasterTileOutlines)
    {
        double pixelWidth = (viewport.MaxX - viewport.MinX) / width;
        double pixelHeight = (viewport.MaxY - viewport.MinY) / height;
        double cellSize = Math.Max(pixelWidth, pixelHeight);
        if (!double.IsFinite(cellSize) || cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewport),
                "The map viewport must be a finite, non-empty world-coordinate extent.");
        int columns = checked((int)Math.Ceiling((viewport.MaxX - viewport.MinX) / cellSize) + 2);
        int rows = checked((int)Math.Ceiling((viewport.MaxY - viewport.MinY) / cellSize) + 2);
        if (rows > MaximumRasterSide + 2 || columns > MaximumRasterSide + 2 ||
            (long)rows * columns > MaximumRasterPixels + 4L * MaximumRasterSide + 4)
            throw new ArgumentOutOfRangeException(nameof(width),
                "The associated-layer viewport exceeds the supported raster dimensions.");

        var query = new RasterDefinition(
            new Point(viewport.MinX - cellSize, viewport.MaxY + cellSize), cellSize, rows, columns);
        var renderLayer = new RasterRenderLayer(raster, symbology)
        {
            RegisterDataOnPlotRequest = false,
            PlotRasterTileOutlines = plotRasterTileOutlines
        };

        using var plot = new PlotRequest(query);
        if (!renderLayer.Render(plot))
            throw new InvalidOperationException("The source raster renderer returned an incomplete image.");

        ReadOnlySpan<int> sourceArgb = plot.ColorBufferARGB.Span;
        if (sourceArgb.Length != query.CellCount)
            throw new InvalidOperationException("The source raster renderer returned an incomplete image.");

        byte[] rgba = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
        {
            double x = viewport.MinX + (column + 0.5) * pixelWidth;
            double y = viewport.MaxY - (row + 0.5) * pixelHeight;
            int sourceRow = query.Row(y);
            int sourceColumn = query.Col(x);
            if ((uint)sourceRow >= (uint)query.Rows || (uint)sourceColumn >= (uint)query.Cols)
                throw new InvalidOperationException("The source raster renderer returned an incomplete image.");
            uint argb = unchecked((uint)sourceArgb[sourceRow * query.Cols + sourceColumn]);
            int target = (row * width + column) * 4;
            rgba[target] = (byte)(argb >> 16);
            rgba[target + 1] = (byte)(argb >> 8);
            rgba[target + 2] = (byte)argb;
            rgba[target + 3] = (byte)(argb >> 24);
        }
        return rgba;
    }

    private static RenderedPixels RenderNValue(NValueLayer layer, CatalogTreeNodeLocator locator,
        string sourcePath, string sourceRevision, AssociatedMapLayersRequest request,
        UnitSystem units, string visualizationRevision, bool plotRasterTileOutlines)
    {
        if (layer.Mode is not (NValueSource.Constant or NValueSource.ClassificationLayer))
            throw new NotSupportedException("The selected N-value source mode has no retained viewport resampler.");
        if (layer.Mode != NValueSource.Constant && !IsFiniteExtent(layer.ValidExtent))
            return new([], [], 0);
        RasterSymbology symbology = AssociatedRasterStyleService.ResolveForRender(request.Map,
            locator, layer, sourcePath, sourceRevision, visualizationRevision, units);
        float[] values = Sample(layer, request.Viewport, request.Width, request.Height);
        float noData = layer.NoDataValue ?? -9999f;
        int valid = values.Count(value => float.IsFinite(value) && value != noData);
        if (valid == 0) return new([], Legend(symbology.SurfaceFill), 0);
        byte[] rgba = RenderRasterPixels(layer, symbology, request.Viewport,
            request.Width, request.Height, plotRasterTileOutlines);
        return new(rgba, Legend(symbology.SurfaceFill), valid);
    }

    private static RenderedPixels RenderClassification(ClassificationLayer layer,
        MapGeometryViewport viewport, int width, int height)
    {
        if (!IsFiniteExtent(layer.ValidExtent)) return new([], [], 0);
        Point[] points = PixelCenters(viewport, width, height);
        int[] values = new int[points.Length];
        layer.SamplePoints(points, values);

        ColorMap<int> sourceColors = SourceClassificationColors(layer);
        int[] argb = new int[values.Length];
        sourceColors.ParallelPaint(values, argb);
        byte[] rgba = ToRgba(argb);
        int valid = 0;
        foreach (int value in values) if (value != (layer.NoDataValue ?? 0)) valid++;
        return new(rgba, ClassificationLegend(layer, sourceColors), valid);
    }

    private static InterpolatedSurfaceFill TerrainSurfaceFill(Terrain terrain, Extent extent)
    {
        double area = extent.Area;
        double cellSize = Math.Sqrt(Math.Sqrt(area));
        if (!double.IsFinite(cellSize) || cellSize <= 0)
            return new InterpolatedSurfaceFill(ColorPalettes.Terrain,
                new double[] { 0, 100 }, name: ColorPalettes.Terrain.Name);
        int sampleCount = new RasterDefinition(extent, cellSize).CellCount;
        if (sampleCount <= 0)
            return new InterpolatedSurfaceFill(ColorPalettes.Terrain,
                new double[] { 0, 100 }, name: ColorPalettes.Terrain.Name);
        Histogram histogram = terrain.ComputeHistogram(sampleCount, 1024);
        return histogram.IsNull()
            ? new InterpolatedSurfaceFill(ColorPalettes.Terrain,
                new double[] { 0, 100 }, name: ColorPalettes.Terrain.Name)
            : new InterpolatedSurfaceFill(ColorPalettes.Terrain, histogram,
                isStretched: true, regenForScreen: false, name: ColorPalettes.Terrain.Name);
    }

    private static float[] Sample(Terrain terrain, MapGeometryViewport viewport, int width, int height)
    {
        Point[] points = PixelCenters(viewport, width, height);
        float[] values = new float[points.Length];
        terrain.SamplePoints(points, values);
        return values;
    }

    private static float[] Sample(NValueLayer layer, MapGeometryViewport viewport, int width, int height)
    {
        Point[] points = PixelCenters(viewport, width, height);
        float[] values = new float[points.Length];
        layer.SamplePoints(points, values);
        return values;
    }

    private static RenderedPixels ColorFloat(float[] values, float noData,
        InterpolatedSurfaceFill fill, AssociatedMapLayerLegend[] legend)
    {
        byte[] rgba = new byte[values.Length * 4];
        int valid = 0;
        for (int index = 0; index < values.Length; index++)
        {
            float value = values[index];
            if (!float.IsFinite(value) || value == noData) continue;
            // Available means at least one valid source sample. A valid
            // discrete class may intentionally map to transparent alpha when
            // the retained standard color map has no category for it.
            valid++;
            uint argb = unchecked((uint)fill.ColorARGB(value));
            int offset = index * 4;
            rgba[offset] = (byte)(argb >> 16);
            rgba[offset + 1] = (byte)(argb >> 8);
            rgba[offset + 2] = (byte)argb;
            rgba[offset + 3] = (byte)(argb >> 24);
        }
        return new(rgba, legend, valid);
    }

    private static ColorMap<int> SourceClassificationColors(ClassificationLayer layer)
    {
        ColorMap<int> colors = new(0);
        IReadOnlyMap<int, string> standard = ClassificationStandards.NLCD2011.KeyMap;
        foreach (KeyValuePair<string, int> standardClass in standard.Backward)
        {
            if (!layer.ClassificationMap.TryGetID(standardClass.Key, out int localValue)) continue;
            int paletteIndex = ColorPalettes.NLCDCategories.IndexOf(standardClass.Value);
            if (paletteIndex >= 0)
                colors.SetColor(localValue, ColorPalettes.NLCD.Colors[paletteIndex]);
        }
        return colors;
    }

    private static AssociatedMapLayerLegend[] ClassificationLegend(ClassificationLayer layer,
        ColorMap<int> colorMap)
    {
        return layer.ClassificationMap.Map.Forward.OrderBy(item => item.Key).Select(item =>
        {
            int[] value = [item.Key];
            int[] argb = [0];
            colorMap.ParallelPaint(value, argb);
            uint color = unchecked((uint)argb[0]);
            return new AssociatedMapLayerLegend(item.Key.ToString(CultureInfo.InvariantCulture), item.Value,
                (byte)(color >> 16), (byte)(color >> 8), (byte)color, (byte)(color >> 24));
        }).ToArray();
    }

    private static AssociatedMapLayerLegend[] Legend(InterpolatedSurfaceFill fill)
    {
        int count = Math.Min(fill.ColorValues.Length, fill.Colors.Count);
        return Enumerable.Range(0, count).Select(index =>
        {
            System.Drawing.Color color = fill.Colors[index];
            string value = fill.ColorValues[index].ToString("R", CultureInfo.InvariantCulture);
            return new AssociatedMapLayerLegend(value, value, color.R, color.G, color.B, color.A);
        }).ToArray();
    }

    private static byte[] ToRgba(int[] argb)
    {
        byte[] rgba = new byte[argb.Length * 4];
        for (int index = 0; index < argb.Length; index++)
        {
            uint color = unchecked((uint)argb[index]);
            int offset = index * 4;
            rgba[offset] = (byte)(color >> 16);
            rgba[offset + 1] = (byte)(color >> 8);
            rgba[offset + 2] = (byte)color;
            rgba[offset + 3] = (byte)(color >> 24);
        }
        return rgba;
    }

    private static Point[] PixelCenters(MapGeometryViewport viewport, int width, int height)
    {
        Point[] points = new Point[checked(width * height)];
        double stepX = (viewport.MaxX - viewport.MinX) / width;
        double stepY = (viewport.MaxY - viewport.MinY) / height;
        for (int row = 0; row < height; row++)
        for (int column = 0; column < width; column++)
        {
            points[row * width + column] = new Point(
                viewport.MinX + (column + 0.5) * stepX,
                viewport.MaxY - (row + 0.5) * stepY);
        }
        return points;
    }

    private static bool IsFiniteExtent(Extent extent)
    {
        if (!double.IsFinite(extent.MinX) || !double.IsFinite(extent.MinY) ||
            !double.IsFinite(extent.MaxX) || !double.IsFinite(extent.MaxY) ||
            extent.MaxX <= extent.MinX || extent.MaxY <= extent.MinY)
            return false;
        double area = (extent.MaxX - extent.MinX) * (extent.MaxY - extent.MinY);
        return double.IsFinite(area) && area > 0;
    }

    private static MapGeometryViewport? SourceExtent(object source)
    {
        if (source is NValueLayer unsupportedNValue &&
            unsupportedNValue.Mode is not (NValueSource.Constant or NValueSource.ClassificationLayer))
            return null;
        Extent extent = source switch
        {
            Terrain terrain => terrain.ValidExtent,
            NValueLayer nvalue => nvalue.ValidExtent,
            ClassificationLayer classification => classification.ValidExtent,
            _ => Extent.NullExtent
        };
        return IsFiniteExtent(extent)
            ? new MapGeometryViewport(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY)
            : null;
    }

    private static AssociatedMapLayerDependency[] DependencyList(DependencyCapture capture,
        string sourcePath, string sourceRevision)
    {
        return [new AssociatedMapLayerDependency(sourcePath, sourceRevision, "source-hdf"), ..capture.Items];
    }

    private static string ContextToken(AssociatedMapLayersRequest request, string projectPath,
        string projectRevision, string projectXmlSHA, string visualizationRevision,
        ResolvedGeometry[] geometries, ResolvedPlan[] plans,
        ResolvedResult[] results, ResolvedAssociation[] layers)
    {
        StringBuilder material = new();
        AddToken(material, request.Map.MapID);
        AddToken(material, request.Map.MapIndex.ToString(CultureInfo.InvariantCulture));
        AddToken(material, request.Map.MapName);
        AddToken(material, request.Map.MapType);
        AddToken(material, projectPath);
        AddToken(material, projectRevision);
        AddToken(material, projectXmlSHA);
        AddToken(material, visualizationRevision);
        AddToken(material, request.Map.CoreLayerContext?.IdentityToken ?? "");
        AddToken(material, request.Width.ToString(CultureInfo.InvariantCulture));
        AddToken(material, request.Height.ToString(CultureInfo.InvariantCulture));
        AddToken(material, Number(request.Viewport.MinX));
        AddToken(material, Number(request.Viewport.MinY));
        AddToken(material, Number(request.Viewport.MaxX));
        AddToken(material, Number(request.Viewport.MaxY));
        foreach (ResolvedGeometry geometry in geometries)
        {
            AddToken(material, NodeIdentity(geometry.Locator));
            AddToken(material, geometry.AssetPath);
            AddToken(material, geometry.Revision);
            AddToken(material, geometry.IsChecked ? "checked" : "unchecked");
            AddToken(material, geometry.EffectiveVisible ? "visible" : "hidden");
        }
        foreach (ResolvedPlan plan in plans)
        {
            AddToken(material, NodeIdentity(plan.Locator));
            AddToken(material, plan.AssetPath);
            AddToken(material, plan.Revision);
            AddToken(material, plan.IsChecked ? "checked" : "unchecked");
            AddToken(material, plan.EffectiveVisible ? "visible" : "hidden");
        }
        foreach (ResolvedResult result in results)
        {
            AddToken(material, NodeIdentity(result.Owner.ResultNode));
            AddToken(material, result.Owner.ContextToken);
            AddToken(material, result.Owner.SelectionIndex.ToString(CultureInfo.InvariantCulture));
            AddToken(material, result.Owner.SelectedResultPath);
            AddToken(material, result.Owner.SelectedResultRevision);
            AddToken(material, result.IsChecked ? "checked" : "unchecked");
            AddToken(material, result.EffectiveVisible ? "visible" : "hidden");
        }
        foreach (ResolvedAssociation layer in layers)
        {
            AddToken(material, NodeIdentity(layer.Locator));
            AddToken(material, layer.AssetPath);
            AddToken(material, layer.Revision);
            AddToken(material, layer.IsChecked ? "checked" : "unchecked");
            AddToken(material, layer.EffectiveVisible ? "visible" : "hidden");
            AddToken(material, layer.PlotRasterTileOutlines switch
            {
                true => "tile-outlines-on",
                false => "tile-outlines-off",
                null => "not-a-raster-renderer"
            });
            foreach (CatalogTreeNodeLocator geometry in layer.GeometryNodes)
                AddToken(material, NodeIdentity(geometry));
            foreach (CatalogTreeNodeLocator plan in layer.PlanNodes)
                AddToken(material, NodeIdentity(plan));
            foreach (MapResultGeometryOwner result in layer.ResultOwners)
            {
                AddToken(material, NodeIdentity(result.ResultNode));
                AddToken(material, result.ContextToken);
                AddToken(material, result.SelectionIndex.ToString(CultureInfo.InvariantCulture));
                AddToken(material, result.SelectedResultPath);
                AddToken(material, result.SelectedResultRevision);
            }
            foreach (AssociatedMapLayerDependency dependency in layer.Dependencies.Items)
            {
                AddToken(material, dependency.Role);
                AddToken(material, dependency.Path);
                AddToken(material, dependency.Revision);
            }
        }
        return Hash(Encoding.UTF8.GetBytes(material.ToString()));
    }

    private static string NodeIdentity(CatalogTreeNodeLocator locator)
    {
        StringBuilder identity = new();
        AddToken(identity, locator.RootName);
        AddToken(identity, locator.NodeType);
        AddToken(identity, locator.NodeName);
        AddToken(identity, locator.AssetID is null ? "" : SafeCanonical(locator.AssetID));
        AddToken(identity, locator.AssociationKind ?? "");
        AddToken(identity, locator.AssociationAssetID is null ? "" : SafeCanonical(locator.AssociationAssetID));
        AddToken(identity, locator.NodePath is null ? "" : string.Join('.', locator.NodePath));
        AddToken(identity, locator.ParentGeometryAssetID is null ? "" : SafeCanonical(locator.ParentGeometryAssetID));
        AddToken(identity, locator.ParentGeometryNodeName ?? "");
        AddToken(identity, locator.ParentGeometryNodePath is null ? "" : string.Join('.', locator.ParentGeometryNodePath));
        AddToken(identity, locator.ParentPlanAssetID is null ? "" : SafeCanonical(locator.ParentPlanAssetID));
        AddToken(identity, locator.ParentPlanNodeName ?? "");
        AddToken(identity, locator.ParentPlanNodePath is null ? "" : string.Join('.', locator.ParentPlanNodePath));
        AddToken(identity, locator.ParentResultNodeType ?? "");
        AddToken(identity, locator.ParentResultAssetID is null ? "" : SafeCanonical(locator.ParentResultAssetID));
        AddToken(identity, locator.ParentResultNodeName ?? "");
        AddToken(identity, locator.ParentResultNodePath is null ? "" : string.Join('.', locator.ParentResultNodePath));
        AddToken(identity, locator.ParentResultRevision ?? "");
        AddToken(identity, locator.ParentBoundaryConditionAssetID is null ? "" : SafeCanonical(locator.ParentBoundaryConditionAssetID));
        AddToken(identity, locator.ParentBoundaryConditionNodeName ?? "");
        AddToken(identity, locator.ParentBoundaryConditionNodePath is null ? "" : string.Join('.', locator.ParentBoundaryConditionNodePath));
        return identity.ToString();
    }

    private static string SafeCanonical(string path)
    {
        try { return SourceFilePaths.Canonical(path); }
        catch (Exception) { return Path.GetFullPath(path); }
    }

    private static void AddToken(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string CanonicalExisting(string path)
    {
        string canonical = SourceFilePaths.Canonical(path);
        if (!File.Exists(canonical)) throw new SourceFileChangedException();
        return canonical;
    }

    private static bool SamePath(string left, string right) =>
        PathComparer.Equals(SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right));

    private static bool SamePathNullable(string? left, string? right) => left is null || right is null
        ? left is null && right is null
        : SamePath(left, right);

    private static bool SameIndexPath(int[]? left, int[]? right) => left is null || right is null
        ? left is null && right is null
        : left.SequenceEqual(right);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static IEnumerable<CatalogTreeNode> Walk(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 } children)
                foreach (CatalogTreeNode nested in Walk(children)) yield return nested;
        }
    }

    private static string Revision(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void VerifyProjectIdentity(string aliasPath, string canonicalPath, string revision)
    {
        if (!File.Exists(aliasPath) ||
            !PathComparer.Equals(SourceFilePaths.Canonical(aliasPath), canonicalPath) ||
            !string.Equals(Revision(aliasPath), revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void VerifySourceRevisions(ResolvedAssociation layer)
    {
        if (!File.Exists(layer.AssetPath) ||
            !string.Equals(SourceFilePaths.Canonical(layer.AssetPath), layer.AssetPath, StringComparison.Ordinal) ||
            !string.Equals(Revision(layer.AssetPath), layer.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        foreach (AssociatedMapLayerDependency dependency in layer.Dependencies.Items)
        {
            if (dependency.Revision == "missing")
            {
                if (File.Exists(dependency.Path)) throw new SourceFileChangedException();
                continue;
            }
            if (!File.Exists(dependency.Path) ||
                !string.Equals(SourceFilePaths.Canonical(dependency.Path), dependency.Path, StringComparison.Ordinal) ||
                !string.Equals(Revision(dependency.Path), dependency.Revision, StringComparison.Ordinal))
                throw new SourceFileChangedException();
        }
    }

    private static void VerifyGeometryRevision(ResolvedGeometry geometry)
    {
        if (!File.Exists(geometry.AssetPath) ||
            !string.Equals(SourceFilePaths.Canonical(geometry.AssetPath), geometry.AssetPath, StringComparison.Ordinal) ||
            !string.Equals(Revision(geometry.AssetPath), geometry.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void VerifyPlanRevision(ResolvedPlan plan)
    {
        if (!File.Exists(plan.AssetPath) ||
            !string.Equals(SourceFilePaths.Canonical(plan.AssetPath), plan.AssetPath, StringComparison.Ordinal) ||
            !string.Equals(Revision(plan.AssetPath), plan.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void VerifyResultOwner(ResolvedResult result) =>
        MapResultChartService.EnsureUnchanged(result.Owner.SelectedResultPath,
            result.Owner.SelectedResultRevision);

    private static string ProjectPlanPath(Project project, string filename)
    {
        string path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(project.ProjectDirectory, filename);
        return SourceFilePaths.Canonical(path);
    }

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value == value.ToUpperInvariant() && value.All(Uri.IsHexDigit);

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
            .Concat(project.Geometries).Concat(project.Terrains).Concat(project.BoundaryConditions)
            .Concat(project.NValues).Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (Result result in project.Results) result.Dispose();
    }

    private sealed record DependencyCapture(AssociatedMapLayerDependency[] Items, string? Diagnostic);
    private sealed record RenderedPixels(byte[] Rgba, AssociatedMapLayerLegend[] Legend, int ValidPixelCount);
    private sealed record ResolvedGeometry(CatalogTreeNodeLocator Locator, string AssetPath,
        string Revision, Geometry Source, bool IsChecked, bool EffectiveVisible,
        ResolvedResult? ResultOwner = null, ResolvedPlan? PlanOwner = null);
    private sealed record ResolvedPlan(CatalogTreeNodeLocator Locator, string AssetPath,
        string Revision, Plan Source, bool IsChecked, bool EffectiveVisible);
    private sealed record ResolvedResult(MapResultGeometryOwner Owner,
        ResolvedMapResultNode Context, Geometry Geometry, bool IsChecked, bool EffectiveVisible);
    private sealed record ResolvedAssociation(CatalogTreeNodeLocator Locator, string Kind,
        string AssetPath, string Revision, object Source, CatalogTreeNodeLocator[] GeometryNodes,
        CatalogTreeNodeLocator[] PlanNodes, MapResultGeometryOwner[] ResultOwners,
        bool IsChecked, bool EffectiveVisible, DependencyCapture Dependencies,
        AssociatedMapLayerDependency[] RelationDependencies, string? Diagnostic)
    {
        public bool? PlotRasterTileOutlines { get; init; }
    }

    private sealed class DependencyKeyComparer : IEqualityComparer<(string Path, string Role)>
    {
        public bool Equals((string Path, string Role) left, (string Path, string Role) right) =>
            PathComparer.Equals(left.Path, right.Path) && string.Equals(left.Role, right.Role, StringComparison.Ordinal);
        public int GetHashCode((string Path, string Role) value) =>
            HashCode.Combine(PathComparer.GetHashCode(value.Path), StringComparer.Ordinal.GetHashCode(value.Role));
    }
}

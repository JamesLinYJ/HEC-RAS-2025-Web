using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rendering;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Classifiers;
using Ras.Layers.Terrains;
using Utility.Files;
using Utility.Units;

namespace Ras.Backend;

/// <summary>
/// Read/write adapter for the retained RasterVisualContext sparse .vis schema.
/// It patches only the exact source node and optional project palette entry;
/// HDF sources and the caller's project XML draft are read-only.
/// </summary>
public static class AssociatedRasterStyleService
{
    private const string DefaultTerrainValueSource = "Histogram";
    private const string DefaultNValueValueSource = "Manual";
    private const string TerrainDefaultPalette = "Terrain";
    private const string NValueDefaultPalette = "Mannings N";
    // RasterVisualContext.MakeRasterSymbology's plain ColorPalette branch
    // intentionally ignores NValueVisualContext._defaultRamp and supplies 1.0.
    private static readonly double[] NValueInitialRamp = [0.02, 0.03, 0.04, 0.05, 0.06, 0.08, 1.0];
    // NValueVisualContext.MakeHistogramValues falls back to its independent
    // _defaultRamp when the source resampler has no finite extent.
    private static readonly double[] NValueHistogramFallback = [0.02, 0.03, 0.04, 0.05, 0.06, 0.08, 0.1];
    private static readonly double[] NValueResetRamp = [0.02, 0.03, 0.05, 0.06, 0.08, 0.12, 0.16];

    public static AssociatedRasterStyleSnapshot Load(AssociatedRasterStyleRequest request)
    {
        ValidateRequest(request);
        string projectAliasPath = Path.GetFullPath(request.Map.ProjectPath);
        string projectCanonicalPath = SourceFilePaths.Canonical(projectAliasPath);
        string projectRevision = Revision(projectCanonicalPath);
        string sourceCanonicalPath = CanonicalExisting(request.SourcePath);
        VerifyRevision(sourceCanonicalPath, request.SourceRevision);

        MapTreeRequest selectedMap = MapForSelectedResult(request.Map, request.ResultOwner);
        CapturedNode captured = CaptureNode(selectedMap, request.Node);
        if (!string.Equals(captured.VisualizationRevision,
                request.Map.ExpectedVisualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectAliasPath,
            request.Map.ProjectXml is { Length: > 0 } projectXml
                ? XElement.Parse(projectXml, LoadOptions.PreserveWhitespace) : null,
            request.Map.CoreLayerContext);
        ResolvedStyleSource? resolved = null;
        try
        {
            resolved = ResolveSource(project, new StyleSourceRequest(request.Map, request.Node,
                request.SourcePath, request.SourceRevision, request.ContextToken,
                request.GeometryNodes, request.PlanNodes, request.ResultOwner), captured,
                sourceCanonicalPath);
            object source = resolved.Source;
            AssociatedRasterStyleDependency[] dependencies = MergeDependencies(
                CaptureDependencies(resolved.Project, source), resolved.OwnerDependencies);
            RasterStyleRead read = ReadStyle(request.Node, captured.Node, captured.Root,
                source, resolved.Project.Units);
            string contextToken = ContextToken(request.Map, projectCanonicalPath,
                projectRevision, request.Map.ProjectXmlSHA ?? "", captured, sourceCanonicalPath,
                request.SourceRevision, dependencies, resolved.OwnerDependencies,
                request.GeometryNodes ?? [], request.PlanNodes ?? [], request.ResultOwner);
            if (request.ContextToken is { Length: > 0 } expected &&
                !string.Equals(expected, contextToken, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            VerifyRevision(sourceCanonicalPath, request.SourceRevision);
            VerifyDependencies(dependencies);
            VerifyProjectRevision(projectCanonicalPath, projectRevision);
            resolved.ResultContext?.EnsureUnchanged();
            return Snapshot(request.Map, request.Node,
                request.GeometryNodes ?? [], request.PlanNodes ?? [], request.ResultOwner,
                projectCanonicalPath,
                request.Map.ProjectXmlSHA ?? "", contextToken, sourceCanonicalPath,
                request.SourceRevision, captured.VisualizationRevision, read, dependencies);
        }
        finally
        {
            resolved?.ResultContext?.Dispose();
            MapResultChartService.DisposeProject(project);
        }
    }

    public static AssociatedRasterStyleApplyResult Apply(AssociatedRasterStyleApplyRequest request)
    {
        ValidateRequest(new AssociatedRasterStyleRequest(request.Map, request.Node,
            request.SourcePath, request.SourceRevision, request.ExpectedContextToken,
            request.GeometryNodes, request.PlanNodes, request.ResultOwner));
        ValidateDraft(request.Draft);
        string projectAliasPath = Path.GetFullPath(request.Map.ProjectPath);
        string projectCanonicalPath = SourceFilePaths.Canonical(projectAliasPath);
        string projectRevision = Revision(projectCanonicalPath);
        string sourceCanonicalPath = CanonicalExisting(request.SourcePath);
        VerifyRevision(sourceCanonicalPath, request.SourceRevision);

        MapTreeRequest selectedMap = MapForSelectedResult(request.Map, request.ResultOwner);
        CapturedNode before = CaptureNode(selectedMap, request.Node);
        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectAliasPath,
            request.Map.ProjectXml is { Length: > 0 } projectXml
                ? XElement.Parse(projectXml, LoadOptions.PreserveWhitespace) : null,
            request.Map.CoreLayerContext);
        ResolvedStyleSource? resolved = null;
        try
        {
            StyleSourceRequest sourceRequest = new(request.Map, request.Node,
                request.SourcePath, request.SourceRevision, request.ExpectedContextToken,
                request.GeometryNodes, request.PlanNodes, request.ResultOwner);
            resolved = ResolveSource(project, sourceRequest, before, sourceCanonicalPath);
            object source = resolved.Source;
            AssociatedRasterStyleDependency[] dependencies = MergeDependencies(
                CaptureDependencies(resolved.Project, source), resolved.OwnerDependencies);
            string beforeToken = ContextToken(request.Map, projectCanonicalPath,
                projectRevision, request.Map.ProjectXmlSHA ?? "", before, sourceCanonicalPath,
                request.SourceRevision, dependencies, resolved.OwnerDependencies,
                request.GeometryNodes ?? [], request.PlanNodes ?? [], request.ResultOwner);
            if (!string.Equals(beforeToken, request.ExpectedContextToken, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            AssociatedRasterStyleDraft draft = EffectiveDraft(request.Draft, source);
            ValidateDraft(draft);

            bool changed = false;
            MapTreeMutationResult mutation = MapTreeService.MutateNode(selectedMap,
                request.Node, (map, node, root) =>
                {
                    string liveToken = ContextToken(request.Map, projectCanonicalPath,
                        projectRevision, request.Map.ProjectXmlSHA ?? "",
                        before with { Node = new XElement(node), Root = new XElement(root) },
                        sourceCanonicalPath, request.SourceRevision, dependencies,
                        resolved.OwnerDependencies, request.GeometryNodes ?? [],
                        request.PlanNodes ?? [], request.ResultOwner);
                    if (!string.Equals(liveToken, request.ExpectedContextToken, StringComparison.Ordinal))
                        throw new SourceFileChangedException();
                    VerifyRevision(sourceCanonicalPath, request.SourceRevision);
                    VerifyDependencies(dependencies);
                    VerifyProjectRevision(projectCanonicalPath, projectRevision);
                    resolved.ResultContext?.EnsureUnchanged();
                    changed = ApplyPaletteMutation(root, request.PaletteMutation, draft,
                        resolved.Project.Units);
                    AssociatedRasterStyleDraft nodeDraft = request.PaletteMutation is
                        { Action: "saveCustom", Name: { Length: > 0 } paletteName }
                        ? draft with { PaletteID = paletteName.Trim(), Name = paletteName.Trim() }
                        : draft;
                    changed |= PatchNode(node, nodeDraft,
                        ReadPalettes(root, resolved.Project.Units, request.Node.NodeType, source));
                    return changed;
                }, materializeIfImplicit: true);

            if (mutation.PreviousVisualizationRevision is { } oldRevision &&
                !oldRevision.Equals(request.Map.ExpectedVisualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            VerifyRevision(sourceCanonicalPath, request.SourceRevision);
            VerifyDependencies(dependencies);
            VerifyProjectRevision(projectCanonicalPath, projectRevision);
            if (request.ResultOwner is { } resultOwner)
                VerifyResultOwnerAfterMutation(selectedMap, mutation.VisualizationRevision, resultOwner);
            if (changed && string.Equals(mutation.VisualizationRevision,
                    mutation.PreviousVisualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The source visualization settings did not advance after applying the style.");

            return new(mutation.PreviousVisualizationRevision ?? "",
                mutation.VisualizationRevision, request.SourceRevision);
        }
        finally
        {
            resolved?.ResultContext?.Dispose();
            MapResultChartService.DisposeProject(project);
        }
    }

    /// <summary>Loads a source raster style for the map composition without writing .vis.</summary>
    internal static RasterSymbology ResolveForRender(MapTreeRequest map,
        CatalogTreeNodeLocator node, object source, string sourcePath, string sourceRevision,
        string visualizationRevision, UnitSystem units)
    {
        string canonicalSource = CanonicalExisting(sourcePath);
        VerifyRevision(canonicalSource, sourceRevision);
        CapturedNode captured = CaptureNode(map with { ExpectedVisualizationRevision = visualizationRevision }, node);
        if (!string.Equals(captured.VisualizationRevision, visualizationRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        RasterStyleRead read = ReadStyle(node, captured.Node, captured.Root, source, units);
        VerifyRevision(canonicalSource, sourceRevision);
        return read.Symbology;
    }

    private static AssociatedRasterStyleSnapshot Snapshot(MapTreeRequest map,
        CatalogTreeNodeLocator node, CatalogTreeNodeLocator[] geometryNodes,
        CatalogTreeNodeLocator[] planNodes, MapResultGeometryOwner? resultOwner,
        string projectCanonicalPath, string projectXmlSHA,
        string token, string sourcePath, string sourceRevision, string visualizationRevision,
        RasterStyleRead read, AssociatedRasterStyleDependency[] dependencies) => new(
            map.MapID, map.MapIndex, map.MapName, map.MapType, projectCanonicalPath,
            projectXmlSHA, node, geometryNodes, planNodes, resultOwner,
            sourcePath, token, node.NodeType, sourceRevision,
            visualizationRevision, read.Draft, read.ResetDraft, read.Palettes,
            read.CanComputeHistogram, read.HistogramValues, dependencies);

    private static void ValidateRequest(AssociatedRasterStyleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Map is null || request.Node is null)
            throw new ArgumentException("A saved map and exact Terrain or N-value node are required.");
        if (request.Node.NodeType is not ("Terrain" or "NValue") ||
            request.Node.AssociationKind is { Length: > 0 } associationKind && associationKind != request.Node.NodeType)
            throw new NotSupportedException("Only Terrain and N-value source rows support continuous raster styles.");
        if (request.Map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan") ||
            request.Map.MapIndex < 0 || string.IsNullOrWhiteSpace(request.Map.MapID) ||
            string.IsNullOrWhiteSpace(request.Map.MapName) || string.IsNullOrWhiteSpace(request.Map.ExpectedVisualizationRevision) ||
            !Path.IsPathFullyQualified(request.Map.ProjectPath) || !File.Exists(request.Map.ProjectPath))
            throw new ArgumentException("The saved map style identity is incomplete.");
        if (!Path.IsPathFullyQualified(request.SourcePath) || string.IsNullOrWhiteSpace(request.SourceRevision) ||
            !IsSHA256(request.SourceRevision))
            throw new ArgumentException("The selected source raster identity is incomplete.");
        if (!string.Equals(request.Node.AssetID, request.SourcePath, StringComparison.Ordinal) &&
            !string.Equals(request.Node.AssociationAssetID, request.SourcePath, StringComparison.Ordinal))
        {
            if (request.Node.AssetID is not { Length: > 0 } nodePath ||
                !SourceFilePaths.SameSourceFile(nodePath, request.SourcePath))
                throw new SourceFileChangedException();
        }
        if (request.Node.AssociationAssetID is { Length: > 0 } associatedPath &&
            !SourceFilePaths.SameSourceFile(associatedPath, request.SourcePath))
            throw new SourceFileChangedException();
        if (!string.Equals(request.Node.NodeType, request.Node.AssociationKind ?? request.Node.NodeType,
                StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static MapTreeRequest MapForSelectedResult(MapTreeRequest map, MapResultGeometryOwner? owner)
    {
        if (owner is null) return map;
        ValidateResultOwner(map, owner);
        using ResolvedMapResultContext context = MapResultChartService.ResolveContext(map,
            owner.SelectionOverrides, owner.ContextToken);
        if (!TryResolveResultOwner(context, context.Tree.Roots, owner,
                out ResolvedMapResultNode? selected) || selected is null)
            throw new SourceFileChangedException();
        context.EnsureUnchanged();
        // A Previous/Explicit slot has its own embedded Geometry and rasters.
        // Capture and mutation must rebuild that same tree instead of Latest.
        // Keep the original request identity for the public snapshot/token.
        return context.Map;
    }

    private static CapturedNode CaptureNode(MapTreeRequest request, CatalogTreeNodeLocator locator)
    {
        XElement? selected = null;
        XElement? rootCopy = null;
        MapTreeMutationResult tree = MapTreeService.MutateNode(request, locator,
            (map, node, root) =>
            {
                selected = new XElement(node);
                rootCopy = new XElement(root);
                return false;
            }, materializeIfImplicit: true);
        if (selected is null || rootCopy is null || tree.VisualizationRevision is not { Length: 64 } revision)
            throw new SourceFileChangedException();
        return new(selected, rootCopy, revision, tree.Roots);
    }

    private static ResolvedStyleSource ResolveSource(Project project, StyleSourceRequest request,
        CapturedNode captured, string canonicalPath)
    {
        CatalogTreeNodeLocator locator = request.Node;
        CatalogTreeNode row = ResolveTreeRow(captured.Roots, locator);
        string kind = locator.NodeType;
        var ownerDependencies = new List<AssociatedRasterStyleDependency>();
        ResolvedMapResultContext? resultContext = null;
        bool hasResultParentIdentity = locator.ParentResultNodeType is not null ||
            locator.ParentResultAssetID is not null || locator.ParentResultNodePath is not null ||
            locator.ParentResultNodeName is not null || locator.ParentResultRevision is not null;
        if (hasResultParentIdentity && locator.ParentResultNodeType is not { Length: > 0 })
            throw new SourceFileChangedException();
        try
        {
            if (locator.ParentResultNodeType is { Length: > 0 })
            {
                MapResultGeometryOwner owner = request.ResultOwner ?? throw new SourceFileChangedException();
                ValidateResultOwner(request.Map, owner);
                resultContext = MapResultChartService.ResolveContext(request.Map,
                    owner.SelectionOverrides, owner.ContextToken);
                if (!TryResolveResultOwner(resultContext, captured.Roots, owner,
                        out ResolvedMapResultNode? selectedResult) || selectedResult is null ||
                    !MatchesResultParent(locator, owner))
                    throw new SourceFileChangedException();

                Project resultProject = resultContext.Project;
                Geometry geometry = selectedResult.Result.Geometry;
                object? associated = AssociatedSource(geometry, kind);
                object source = ResolveLayer(resultProject, kind, canonicalPath, locator.NodeName);
                if (associated is null || !ReferenceEquals(associated, source) ||
                    !SameModelPath(resultProject, SourcePath(source), canonicalPath))
                    throw new SourceFileChangedException();
                ownerDependencies.Add(new(SourceFilePaths.Canonical(owner.SelectedResultPath),
                    owner.SelectedResultRevision, "result-hdf"));

                if (HasPlanParent(locator))
                {
                    Plan plan = ResolvePlanForResultChild(resultProject, locator, selectedResult);
                    if (request.PlanNodes is { Length: > 0 } planNodes)
                    {
                        ResolvedPlanOwner[] plans = ResolvePlanOwners(resultProject,
                            captured.Roots, planNodes,
                            requireComplete: request.Map.MapType == "Plan" &&
                                locator.RootName == "Matching Layers");
                        ResolvedPlanOwner planOwner = ResolvePlanParent(locator, plans);
                        if (!SameModelPath(resultProject, planOwner.Source.Filename,
                                ModelPath(resultProject, plan.Filename)) ||
                            !string.Equals(planOwner.Source.Name, plan.Name, StringComparison.Ordinal))
                            throw new SourceFileChangedException();
                        ownerDependencies.Add(new(planOwner.AssetPath, planOwner.Revision, "plan-hdf"));
                    }
                    else
                    {
                        string planPath = CanonicalExisting(ModelPath(resultProject, plan.Filename));
                        ownerDependencies.Add(new(planPath, Revision(planPath), "plan-hdf"));
                    }
                }
                ResolvedStyleSource resolvedResult = new(resultProject, source,
                    resultContext, ownerDependencies.ToArray());
                resultContext = null;
                return resolvedResult;
            }

            if (request.ResultOwner is not null) throw new SourceFileChangedException();
            object projectSource = ResolveLayer(project, kind, canonicalPath, locator.NodeName);

            bool planOwnedRow = HasPlanParent(locator) ||
                (request.Map.MapType == "Plan" && locator.RootName != "Layers");
            if (planOwnedRow)
            {
                if (HasPlanParent(locator) && request.PlanNodes is not { Length: 1 })
                    throw new SourceFileChangedException();
                ResolvedPlanOwner[] plans = ResolvePlanOwners(project, captured.Roots,
                    request.PlanNodes ?? [], requireComplete: request.Map.MapType == "Plan" &&
                        locator.RootName == "Matching Layers");
                if (HasPlanParent(locator) && plans.Length != 1)
                    throw new SourceFileChangedException();
                ResolvedPlanOwner[] relatedPlans = HasPlanParent(locator)
                    ? [ResolvePlanParent(locator, plans)]
                    : locator.RootName == "Matching Layers" ? plans : [];
                if (relatedPlans.Length == 0) throw new SourceFileChangedException();
                bool foundSharedSource = false;
                foreach (ResolvedPlanOwner plan in relatedPlans)
                {
                    object? associated = AssociatedSource(plan.Source, kind);
                    if (associated is null)
                    {
                        if (HasPlanParent(locator)) throw new SourceFileChangedException();
                        continue;
                    }
                    if (!ReferenceEquals(associated, projectSource) ||
                        !SameModelPath(project, SourcePath(projectSource), canonicalPath))
                        throw new SourceFileChangedException();
                    foundSharedSource = true;
                }
                if (!foundSharedSource) throw new SourceFileChangedException();
                ownerDependencies.AddRange(relatedPlans.Select(plan =>
                    new AssociatedRasterStyleDependency(plan.AssetPath, plan.Revision, "plan-hdf")));
            }
            else if (HasGeometryParent(locator) || locator.RootName == "Matching Layers")
            {
                ResolvedGeometryOwner[] geometries = ResolveGeometryOwners(project,
                    captured.Roots, request.GeometryNodes ?? [],
                    requireComplete: locator.RootName == "Matching Layers");
                ResolvedGeometryOwner[] relatedGeometries = HasGeometryParent(locator)
                    ? [ResolveGeometryParent(locator, geometries)]
                    : locator.RootName == "Matching Layers" ? geometries : [];
                if (relatedGeometries.Length == 0) throw new SourceFileChangedException();
                bool foundSharedSource = false;
                foreach (ResolvedGeometryOwner geometry in relatedGeometries)
                {
                    object? associated = AssociatedSource(geometry.Source, kind);
                    if (associated is null)
                    {
                        if (HasGeometryParent(locator)) throw new SourceFileChangedException();
                        continue;
                    }
                    if (!ReferenceEquals(associated, projectSource) ||
                        !SameModelPath(project, SourcePath(projectSource), canonicalPath))
                        throw new SourceFileChangedException();
                    foundSharedSource = true;
                }
                if (!foundSharedSource) throw new SourceFileChangedException();
                ownerDependencies.AddRange(relatedGeometries.Select(geometry =>
                    new AssociatedRasterStyleDependency(geometry.AssetPath, geometry.Revision, "geometry-hdf")));
            }
            else if (HasGeometryParent(locator) || HasPlanParent(locator) ||
                     request.Map.MapType == "Plan" && locator.RootName != "Layers")
            {
                throw new SourceFileChangedException();
            }

            return new(project, projectSource, null, ownerDependencies.ToArray());
        }
        catch
        {
            resultContext?.Dispose();
            throw;
        }
    }

    private static object ResolveLayer(Project project, string kind, string canonicalPath, string name)
    {
        object[] matches = kind switch
        {
            "Terrain" => project.Terrains.Where(item => SameModelPath(project, item.Filename, canonicalPath)
                && string.Equals(item.Name, name, StringComparison.Ordinal)).Cast<object>().ToArray(),
            "NValue" => project.NValues.Where(item => SameModelPath(project, item.Filename, canonicalPath)
                && string.Equals(item.Name, name, StringComparison.Ordinal)).Cast<object>().ToArray(),
            _ => []
        };
        if (matches.Length != 1) throw new SourceFileChangedException();
        return matches[0];
    }

    private static CatalogTreeNode ResolveTreeRow(IEnumerable<CatalogTreeNode> roots,
        CatalogTreeNodeLocator locator)
    {
        CatalogTreeNode[] matches = Walk(roots).Where(node => node.Locator is { } candidate &&
            SameLocator(candidate, locator)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static ResolvedPlanOwner[] ResolvePlanOwners(Project project,
        CatalogTreeNode[] roots, CatalogTreeNodeLocator[] requested, bool requireComplete)
    {
        if (requested.Length is 0 or > 256 || requested.Any(locator => locator.NodeType != "Plan" ||
                locator.RootName != "Focused Plans" || string.IsNullOrWhiteSpace(locator.AssetID) ||
                HasPlanParent(locator) || HasResultParent(locator)))
            throw new SourceFileChangedException();
        CatalogTreeNodeLocator[] actual = FocusedPlanLocators(roots);
        if (requested.Any(locator => !actual.Any(candidate => SameLocator(candidate, locator))))
            throw new SourceFileChangedException();
        if (requireComplete && (actual.Length != requested.Length ||
            !actual.Zip(requested).All(pair => SameLocator(pair.First, pair.Second))))
            throw new SourceFileChangedException();
        if (requested.Select(LocatorIdentity).Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw new SourceFileChangedException();
        return requested.Select(locator =>
        {
            _ = ResolveTreeRow(roots, locator);
            Plan[] matches = project.Plans.Where(plan =>
                SameModelPath(project, plan.Filename, locator.AssetID!) && plan.Name == locator.NodeName).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            string path = CanonicalExisting(ModelPath(project, matches[0].Filename));
            return new ResolvedPlanOwner(locator, path, Revision(path), matches[0]);
        }).ToArray();
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
                    HasPlanParent(locator) || HasResultParent(locator))
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

    private static ResolvedPlanOwner ResolvePlanParent(CatalogTreeNodeLocator child,
        ResolvedPlanOwner[] plans)
    {
        if (child.ParentPlanAssetID is not { Length: > 0 } parentAssetID ||
            child.ParentPlanNodeName is not { Length: > 0 } parentName)
            throw new SourceFileChangedException();
        ResolvedPlanOwner[] matches = plans.Where(plan =>
            SamePath(plan.AssetPath, parentAssetID) &&
            plan.Locator.NodeName == parentName &&
            SameIndexPath(plan.Locator.NodePath, child.ParentPlanNodePath)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static ResolvedGeometryOwner[] ResolveGeometryOwners(Project project,
        CatalogTreeNode[] roots, CatalogTreeNodeLocator[] requested, bool requireComplete)
    {
        if (requested.Length is 0 or > 256 || requested.Any(locator => locator.NodeType != "Geometry" ||
                locator.RootName != "Focused Geometries" || locator.ParentResultNodeType is not null ||
                string.IsNullOrWhiteSpace(locator.AssetID)))
            throw new SourceFileChangedException();
        if (requested.Select(LocatorIdentity).Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw new SourceFileChangedException();
        if (requireComplete)
        {
            CatalogTreeNodeLocator[] actual = Walk(roots).Where(node => node.Locator is
                { NodeType: "Geometry", ParentResultNodeType: null } locator &&
                locator.RootName == "Focused Geometries").Select(node => node.Locator!).ToArray();
            if (actual.Length != requested.Length || !actual.Zip(requested)
                    .All(pair => SameLocator(pair.First, pair.Second)))
                throw new SourceFileChangedException();
        }
        return requested.Select(locator =>
        {
            _ = ResolveTreeRow(roots, locator);
            Geometry[] matches = project.Geometries.Where(geometry =>
                SameModelPath(project, geometry.Filename, locator.AssetID!) && geometry.Name == locator.NodeName).ToArray();
            if (matches.Length != 1) throw new SourceFileChangedException();
            string path = CanonicalExisting(ModelPath(project, matches[0].Filename));
            return new ResolvedGeometryOwner(locator, path, Revision(path), matches[0]);
        }).ToArray();
    }

    private static ResolvedGeometryOwner ResolveGeometryParent(CatalogTreeNodeLocator child,
        ResolvedGeometryOwner[] geometries)
    {
        if (child.ParentGeometryAssetID is not { Length: > 0 } parentAssetID ||
            child.ParentGeometryNodeName is not { Length: > 0 } parentName)
            throw new SourceFileChangedException();
        ResolvedGeometryOwner[] matches = geometries.Where(geometry =>
            SamePath(geometry.AssetPath, parentAssetID) &&
            geometry.Locator.NodeName == parentName &&
            SameIndexPath(geometry.Locator.NodePath, child.ParentGeometryNodePath)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new SourceFileChangedException();
    }

    private static bool TryResolveResultOwner(ResolvedMapResultContext context,
        CatalogTreeNode[] roots, MapResultGeometryOwner owner, out ResolvedMapResultNode? selected)
    {
        selected = null;
        if (owner.SelectionIndex != 0 || owner.ResultNode.NodeType is not ("Result" or "MultiResult") ||
            owner.ContextToken != context.Sources.ContextToken) return false;
        _ = ResolveTreeRow(roots, owner.ResultNode);
        ResolvedMapResultNode[] matches = context.ResultContexts.Where(result =>
            result.SelectionIndex == 0 && SameLocator(result.ActiveNode.Node, owner.ResultNode) &&
            SamePath(result.ResultPath, owner.SelectedResultPath) &&
            result.Revision.Equals(owner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) return false;
        MapResultChartService.EnsureUnchanged(owner.SelectedResultPath, owner.SelectedResultRevision);
        selected = matches[0];
        return true;
    }

    private static void ValidateResultOwner(MapTreeRequest map, MapResultGeometryOwner owner)
    {
        if (map.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan") ||
            owner.SelectionIndex != 0 || string.IsNullOrWhiteSpace(owner.ContextToken) ||
            !Path.IsPathFullyQualified(owner.SelectedResultPath) || !IsSHA256(owner.SelectedResultRevision) ||
            owner.ResultNode.NodeType is not ("Result" or "MultiResult"))
            throw new SourceFileChangedException();
    }

    private static bool MatchesResultParent(CatalogTreeNodeLocator child,
        MapResultGeometryOwner owner)
    {
        if (child.ParentResultNodeType is not { Length: > 0 } parentType ||
            child.ParentResultAssetID is not { Length: > 0 } parentAssetID ||
            child.ParentResultNodeName is not { Length: > 0 } parentName ||
            child.ParentResultRevision is not { Length: > 0 } parentRevision)
            return false;
        return parentType == owner.ResultNode.NodeType &&
            SamePath(parentAssetID, owner.SelectedResultPath) &&
            parentName == owner.ResultNode.NodeName &&
            SameIndexPath(child.ParentResultNodePath, owner.ResultNode.NodePath) &&
            string.Equals(parentRevision, owner.SelectedResultRevision, StringComparison.OrdinalIgnoreCase) &&
            (child.NodePath is null || owner.ResultNode.NodePath is null ||
                IsDescendant(child.NodePath, owner.ResultNode.NodePath));
    }

    private static Plan ResolvePlanForResultChild(Project project,
        CatalogTreeNodeLocator child, ResolvedMapResultNode selectedResult)
    {
        if (child.ParentPlanAssetID is not { Length: > 0 } assetID ||
            child.ParentPlanNodeName is not { Length: > 0 } name)
            throw new SourceFileChangedException();
        Plan[] matches = project.Plans.Where(plan =>
            SameModelPath(project, plan.Filename, assetID) && plan.Name == name).ToArray();
        Plan? embeddedPlan = selectedResult.Result.Plan;
        CatalogTreeNodeLocator resultLocator = selectedResult.ActiveNode.Node;
        if (matches.Length != 1 || embeddedPlan is null ||
            resultLocator.ParentPlanAssetID is not { Length: > 0 } resultPlanAssetID ||
            !SamePath(resultPlanAssetID, assetID) ||
            !string.Equals(resultLocator.ParentPlanNodeName, name, StringComparison.Ordinal) ||
            !SameIndexPath(resultLocator.ParentPlanNodePath, child.ParentPlanNodePath) ||
            !string.Equals(matches[0].Name, embeddedPlan.Name, StringComparison.Ordinal) ||
            !SameModelPath(project, embeddedPlan.Filename, selectedResult.ResultPath))
            throw new SourceFileChangedException();
        return matches[0];
    }

    private static bool HasGeometryParent(CatalogTreeNodeLocator locator) =>
        locator.ParentGeometryAssetID is not null || locator.ParentGeometryNodePath is not null ||
        locator.ParentGeometryNodeName is not null;

    private static bool HasPlanParent(CatalogTreeNodeLocator locator) =>
        locator.ParentPlanAssetID is not null || locator.ParentPlanNodePath is not null ||
        locator.ParentPlanNodeName is not null;

    private static bool HasResultParent(CatalogTreeNodeLocator locator) =>
        locator.ParentResultNodeType is not null || locator.ParentResultAssetID is not null ||
        locator.ParentResultNodePath is not null || locator.ParentResultNodeName is not null ||
        locator.ParentResultRevision is not null;

    private static object? AssociatedSource(Geometry geometry, string kind) => kind switch
    {
        "Terrain" => geometry.AssociatedTerrain,
        "NValue" => geometry.AssociatedNValueLayer,
        _ => null
    };

    private static object? AssociatedSource(Plan plan, string kind) =>
        plan.PlanAssociation.Geometry is { } geometry ? AssociatedSource(geometry, kind) : null;

    private static string ModelPath(Project project, string filename) =>
        SourceFilePaths.Canonical(Path.IsPathFullyQualified(filename)
            ? filename : Path.Combine(project.ProjectDirectory, filename));

    private static bool SameModelPath(Project project, string modelPath, string canonicalPath) =>
        SamePath(ModelPath(project, modelPath), canonicalPath);

    private static IEnumerable<CatalogTreeNode> Walk(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 } children)
                foreach (CatalogTreeNode child in Walk(children)) yield return child;
        }
    }

    private static bool SameLocator(CatalogTreeNodeLocator actual, CatalogTreeNodeLocator expected) =>
        actual.RootName == expected.RootName && actual.NodeType == expected.NodeType &&
        actual.NodeName == expected.NodeName && SamePathNullable(actual.AssetID, expected.AssetID) &&
        actual.MapType == expected.MapType && actual.Filename == expected.Filename &&
        actual.FilenameRelative == expected.FilenameRelative &&
        SamePathNullable(actual.ParentGeometryAssetID, expected.ParentGeometryAssetID) &&
        SameIndexPath(actual.ParentGeometryNodePath, expected.ParentGeometryNodePath) &&
        actual.ParentGeometryNodeName == expected.ParentGeometryNodeName &&
        SamePathNullable(actual.AssociationAssetID, expected.AssociationAssetID) &&
        actual.AssociationKind == expected.AssociationKind &&
        SamePathNullable(actual.ParentPlanAssetID, expected.ParentPlanAssetID) &&
        SameIndexPath(actual.ParentPlanNodePath, expected.ParentPlanNodePath) &&
        actual.ParentPlanNodeName == expected.ParentPlanNodeName &&
        actual.ParentResultNodeType == expected.ParentResultNodeType &&
        SamePathNullable(actual.ParentResultAssetID, expected.ParentResultAssetID) &&
        SameIndexPath(actual.ParentResultNodePath, expected.ParentResultNodePath) &&
        actual.ParentResultNodeName == expected.ParentResultNodeName &&
        string.Equals(actual.ParentResultRevision, expected.ParentResultRevision, StringComparison.OrdinalIgnoreCase) &&
        SameIndexPath(actual.NodePath, expected.NodePath);

    private static bool SamePath(string left, string right) =>
        string.Equals(SourceFilePaths.Canonical(left), SourceFilePaths.Canonical(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool SamePathNullable(string? left, string? right) => left is null || right is null
        ? left is null && right is null : SamePath(left, right);

    private static bool SameIndexPath(int[]? left, int[]? right) => left is null || right is null
        ? left is null && right is null : left.SequenceEqual(right);

    private static bool IsDescendant(int[] child, int[] parent) => child.Length > parent.Length &&
        child.Take(parent.Length).SequenceEqual(parent);

    private static string LocatorIdentity(CatalogTreeNodeLocator locator)
    {
        StringBuilder identity = new();
        void Add(string value) => identity.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(value).Append(';');
        Add(locator.RootName);
        Add(locator.NodeType);
        Add(locator.NodeName);
        Add(locator.AssetID is null ? "" : SourceFilePaths.Canonical(locator.AssetID));
        Add(locator.MapType ?? "");
        Add(locator.Filename ?? "");
        Add(locator.FilenameRelative ?? "");
        Add(locator.NodePath is null ? "" : string.Join('.', locator.NodePath));
        Add(locator.AssociationKind ?? "");
        Add(locator.AssociationAssetID is null ? "" : SourceFilePaths.Canonical(locator.AssociationAssetID));
        Add(locator.ParentGeometryAssetID is null ? "" : SourceFilePaths.Canonical(locator.ParentGeometryAssetID));
        Add(locator.ParentGeometryNodeName ?? "");
        Add(locator.ParentGeometryNodePath is null ? "" : string.Join('.', locator.ParentGeometryNodePath));
        Add(locator.ParentPlanAssetID is null ? "" : SourceFilePaths.Canonical(locator.ParentPlanAssetID));
        Add(locator.ParentPlanNodeName ?? "");
        Add(locator.ParentPlanNodePath is null ? "" : string.Join('.', locator.ParentPlanNodePath));
        Add(locator.ParentResultNodeType ?? "");
        Add(locator.ParentResultAssetID is null ? "" : SourceFilePaths.Canonical(locator.ParentResultAssetID));
        Add(locator.ParentResultNodeName ?? "");
        Add(locator.ParentResultNodePath is null ? "" : string.Join('.', locator.ParentResultNodePath));
        Add(locator.ParentResultRevision ?? "");
        return identity.ToString();
    }

    private static RasterStyleRead ReadStyle(CatalogTreeNodeLocator locator, XElement node,
        XElement root, object source, UnitSystem units)
    {
        List<PaletteInfo> palettes = ReadPalettes(root, units, locator.NodeType, source);
        string selectedName = (string?)node.Attribute("Symbology") ?? "";
        string sourceValueSource = locator.NodeType == "Terrain"
            ? DefaultTerrainValueSource : DefaultNValueValueSource;
        RasterSymbology symbology = DefaultSymbology(locator.NodeType, source,
            selectedName, palettes, out sourceValueSource);
        XElement? saved = node.Element("RasterSymbology");
        if (saved is not null)
            ApplySavedSymbology(saved, node, source, locator.NodeType, palettes,
                ref symbology, ref sourceValueSource);

        double[]? histogramValues = MakeHistogramValues(source, locator.NodeType,
            symbology.SurfaceFill.Colors.Count);
        AssociatedRasterStyleDraft draft = DraftFrom(symbology, sourceValueSource, null);
        draft = draft with { PaletteID = PaletteID(draft, palettes) };
        RasterSymbology reset = ResetSymbology(locator.NodeType, source, units);
        AssociatedRasterStyleDraft resetDraft = DraftFrom(reset,
            locator.NodeType == "Terrain" ? DefaultTerrainValueSource : DefaultNValueValueSource, null);
        resetDraft = resetDraft with { PaletteID = PaletteID(resetDraft, palettes) };

        return new(symbology, draft, resetDraft,
            palettes.Select(ToContract).ToArray(),
            CanComputeHistogram(source) && histogramValues is not null, histogramValues);
    }

    private static RasterSymbology DefaultSymbology(string kind, object source,
        string selectedName, IReadOnlyList<PaletteInfo> palettes, out string valueSource)
    {
        valueSource = kind == "Terrain" ? DefaultTerrainValueSource : DefaultNValueValueSource;
        PaletteInfo? selected = FindPalette(palettes, selectedName);
        InterpolatedSurfaceFill fill;
        if (selected is null)
        {
            fill = kind == "Terrain"
                ? TerrainHistogramFill((Terrain)source, ColorPalettes.Terrain)
                : new InterpolatedSurfaceFill(ColorPalettes.ManningsN, NValueInitialRamp,
                    isStretched: true, regenForScreen: false, NValueDefaultPalette);
        }
        else if (selected.Values is { Length: > 0 } values &&
                 selected.ValueSource is "Manual" or "Linear")
        {
            fill = new InterpolatedSurfaceFill(selected.Colors, values,
                isStretched: true, regenForScreen: false, selected.Name);
            valueSource = selected.ValueSource;
        }
        else if (selected.ValueSource == "Histogram" && selected.IsCustom)
        {
            double[] histogramValues = MakeHistogramValues(source, kind, selected.Colors.Length) ??
                throw new NotSupportedException("The source does not provide histogram values for this raster style.");
            fill = new InterpolatedSurfaceFill(selected.Colors, histogramValues,
                isStretched: true, regenForScreen: false, selected.Name);
            valueSource = "Histogram";
        }
        else if (kind == "Terrain")
        {
            fill = TerrainHistogramFill((Terrain)source,
                new ColorPalette(selected.Colors.ToList(), selected.Name), selected.Name);
            valueSource = "Histogram";
        }
        else
        {
            fill = new InterpolatedSurfaceFill(selected.Colors, NValueInitialRamp,
                isStretched: true, regenForScreen: false, selected.Name);
            valueSource = "Manual";
        }

        var symbology = new RasterSymbology(fill);
        if (kind == "Terrain")
        {
            symbology.HillshadeProperties.IsEnabled = true;
            symbology.HillshadeProperties.ZFactor = 3f;
        }
        return symbology;
    }

    private static RasterSymbology ResetSymbology(string kind, object source, UnitSystem units)
    {
        if (kind == "Terrain")
        {
            var terrain = (Terrain)source;
            var reset = new RasterSymbology(TerrainHistogramFill(terrain, ColorPalettes.Terrain));
            reset.HillshadeProperties.IsEnabled = true;
            reset.HillshadeProperties.ZFactor = 3f;
            return reset;
        }
        InterpolatedSurfaceFill fill = ColorPalettes.GetValuesByUnit(units)
            .FirstOrDefault(item => string.Equals(item.Name, NValueDefaultPalette, StringComparison.Ordinal))
            ?? new InterpolatedSurfaceFill(ColorPalettes.ManningsN, NValueResetRamp,
                isStretched: true, regenForScreen: false, NValueDefaultPalette);
        return new RasterSymbology(fill.Clone());
    }

    private static InterpolatedSurfaceFill TerrainHistogramFill(Terrain terrain,
        IColorPalette palette, string? name = null)
    {
        Extent extent = terrain.ValidExtent;
        if (!FiniteExtent(extent))
            return new InterpolatedSurfaceFill(palette.Colors,
                LinearValues(0, 100, palette.Colors.Count), isStretched: true,
                regenForScreen: false, name ?? palette.Name);
        double cellSize = Math.Sqrt(Math.Sqrt(extent.Area));
        if (!double.IsFinite(cellSize) || cellSize <= 0)
            return new InterpolatedSurfaceFill(palette.Colors,
                LinearValues(0, 100, palette.Colors.Count), isStretched: true,
                regenForScreen: false, name ?? palette.Name);
        int sampleCount = new RasterDefinition(extent, cellSize).CellCount;
        if (sampleCount <= 0) return new InterpolatedSurfaceFill(palette.Colors,
            LinearValues(0, 100, palette.Colors.Count), isStretched: true,
            regenForScreen: false, name ?? palette.Name);
        Histogram histogram = terrain.ComputeHistogram(sampleCount, 1024);
        return histogram.IsNull()
            ? new InterpolatedSurfaceFill(palette.Colors,
                LinearValues(0, 100, palette.Colors.Count), isStretched: true,
                regenForScreen: false, name ?? palette.Name)
            : new InterpolatedSurfaceFill(palette.Colors, histogram,
                isStretched: true, regenForScreen: false, name ?? palette.Name);
    }

    private static double[]? MakeHistogramValues(object source, string kind, int colorCount)
    {
        if (colorCount < 2) return null;
        try
        {
            if (source is Terrain terrain)
            {
                if (!FiniteExtent(terrain.ValidExtent)) return LinearValues(0, 100, colorCount);
                return InterpolatedSurfaceFill.EstimateColorValuesFrom(terrain, colorCount).ToArray();
            }
            if (source is NValueLayer nvalue)
            {
                if (nvalue.Mode is not (NValueSource.Constant or NValueSource.ClassificationLayer)) return null;
                if (!FiniteExtent(nvalue.ValidExtent))
                    return kind == "NValue" ? FitValues(NValueHistogramFallback, colorCount) : null;
                return InterpolatedSurfaceFill.EstimateColorValuesFrom(nvalue, colorCount).ToArray();
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static bool CanComputeHistogram(object source) => source switch
    {
        Terrain terrain => FiniteExtent(terrain.ValidExtent),
        NValueLayer nvalue when nvalue.Mode is NValueSource.Constant or NValueSource.ClassificationLayer =>
            FiniteExtent(nvalue.ValidExtent),
        _ => false
    };

    private static AssociatedRasterStyleDraft EffectiveDraft(AssociatedRasterStyleDraft draft, object source)
    {
        double[] values = draft.ValueSource switch
        {
            "Linear" => LinearValues(draft.Stops[0].Value, draft.Stops[^1].Value, draft.Stops.Length),
            "Histogram" => MakeHistogramValues(source, source is Terrain ? "Terrain" : "NValue",
                draft.Stops.Length) ?? throw new NotSupportedException("The source does not provide histogram values for this raster style."),
            _ => draft.Stops.Select(stop => stop.Value).ToArray()
        };
        var stops = draft.Stops.Select((stop, index) => stop with { Index = index, Value = values[index] }).ToArray();
        return draft with { Stops = stops, IsEditing = true };
    }

    private static AssociatedRasterStyleDraft DraftFrom(RasterSymbology symbology,
        string valueSource, string? paletteID)
    {
        InterpolatedSurfaceFill fill = symbology.SurfaceFill;
        return new(paletteID, fill.Name ?? "", fill.Colors.Select((color, index) =>
                new AssociatedRasterStyleColorStop(index, ColorText(color), fill.ColorValues[index])).ToArray(),
            valueSource, false, symbology.IsSurfaceFillEnabled, symbology.Alpha,
            symbology.InterpolationMode.ToString(), symbology.SmoothingRadius,
            symbology.ResampleCellSizeRatio, symbology.HillshadeProperties.IsEnabled,
            symbology.HillshadeProperties.ZFactor, symbology.HillshadeProperties.Azimuth,
            symbology.HillshadeProperties.Zenith, symbology.ContourProperties.IsEnabled,
            symbology.ContourProperties.AutoInterval, symbology.ContourProperties.ContourInterval,
            symbology.ContourProperties.IndexEvery, symbology.ContourProperties.Color.ToArgb(),
            symbology.ContourProperties.Opacity, symbology.ContourProperties.ShowLabels,
            symbology.ContourProperties.Density);
    }

    private static void ValidateDraft(AssociatedRasterStyleDraft draft)
    {
        if (draft is null || draft.Name is null || draft.Stops is null ||
            !IsValueSource(draft.ValueSource) ||
            draft.InterpolationMode is not ("Nearest" or "Linear" or "Smooth" or "AreaAveraged" or "Hybrid") ||
            draft.Stops.Length is < 2 or > 256 || draft.Alpha is < 0 or > 255 ||
            !Finite(draft.SmoothingRadius, 0, 1_000_000) ||
            !Finite(draft.ResampleCellSizeRatio, 0, 1_000_000) ||
            !Finite(draft.HillshadeZFactor, 0, 1_000_000) ||
            !Finite(draft.HillshadeAzimuth, -1_000_000, 1_000_000) ||
            !Finite(draft.HillshadeZenith, -1_000_000, 1_000_000) ||
            !Finite(draft.ContourInterval, 0.000001, 1_000_000) ||
            draft.ContourIndexEvery is < 0 or > 1_000 ||
            draft.ContourOpacity is < 0 or > 100 ||
            !Finite(draft.ContourDensity, 0.025, 4) ||
            draft.Stops.Where((stop, index) => stop.Index != index || !double.IsFinite(stop.Value) ||
                !TryParseColor(stop.Color, out _)).Any())
            throw new ArgumentException("The raster style contains an invalid value.");
        for (int index = 1; index < draft.Stops.Length; index++)
            if (draft.Stops[index].Value <= draft.Stops[index - 1].Value)
                throw new ArgumentException("The raster color values must increase from first to last.");
    }

    private static bool PatchNode(XElement node, AssociatedRasterStyleDraft draft,
        IReadOnlyList<PaletteInfo> palettes)
    {
        XElement before = new(node);
        PaletteInfo? selectedPalette = draft.PaletteID is { Length: > 0 } paletteID
            ? FindPaletteByID(palettes, paletteID)
            : FindUniquePaletteByName(palettes, draft.Name);
        bool paletteMatches = selectedPalette is not null && PaletteMatchesDraft(selectedPalette, draft);
        bool namedReference = paletteMatches && selectedPalette is not null &&
            palettes.Count(palette => NameEquals(palette.Name, selectedPalette.Name)) == 1;
        if (namedReference && selectedPalette is { } nodePalette)
            node.SetAttributeValue("Symbology", nodePalette.Name);
        else
            node.Attribute("Symbology")?.Remove();

        XElement? raster = node.Element("RasterSymbology");
        if (raster is null)
        {
            raster = new XElement("RasterSymbology");
            node.Add(raster);
        }
        SetDefaulted(raster, "IsSurfaceFillEnabled", draft.IsSurfaceFillEnabled, true);
        SetDefaulted(raster, "Alpha", draft.Alpha, byte.MaxValue);
        SetDefaulted(raster, "InterpolationMode", draft.InterpolationMode, "Linear");
        SetDefaulted(raster, "SmoothingRadius", draft.SmoothingRadius, 15d);
        SetDefaulted(raster, "ResampleCellSizeRatio", draft.ResampleCellSizeRatio, 1d);
        SetDefaulted(raster, "ValueSource", draft.ValueSource, "Manual");

        XElement? surface = raster.Element("SurfaceFill");
        if (surface is null)
        {
            surface = new XElement("SurfaceFill");
            raster.Add(surface);
        }
        surface.Attribute("Name")?.Remove();
        surface.Elements("Color").Remove();
        if (namedReference && selectedPalette is { } surfacePalette)
        {
            surface.SetAttributeValue("Name", surfacePalette.Name);
        }
        else
        {
            string inlineName = paletteMatches && selectedPalette is { } inlinePalette
                ? inlinePalette.Name : draft.Name;
            if (!string.IsNullOrWhiteSpace(inlineName))
                surface.SetAttributeValue("Name", inlineName);
            foreach (AssociatedRasterStyleColorStop stop in draft.Stops)
            {
                if (!TryParseColor(stop.Color, out Color color)) throw new ArgumentException("The raster color is invalid.");
                surface.Add(new XElement("Color", new XAttribute("R", color.R),
                    new XAttribute("G", color.G), new XAttribute("B", color.B),
                    new XAttribute("Value", stop.Value.ToString("R", CultureInfo.InvariantCulture))));
            }
        }

        XElement? hillshade = raster.Element("Hillshade");
        PatchChild(raster, ref hillshade, "Hillshade", new Dictionary<string, object>
        {
            ["IsEnabled"] = draft.HillshadeEnabled,
            ["ZFactor"] = draft.HillshadeZFactor,
            ["Azimuth"] = draft.HillshadeAzimuth,
            ["Zenith"] = draft.HillshadeZenith
        }, new Dictionary<string, object>
        {
            ["IsEnabled"] = false, ["ZFactor"] = 3d, ["Azimuth"] = 315d, ["Zenith"] = 45d
        });

        XElement? contours = raster.Element("Contours");
        PatchChild(raster, ref contours, "Contours", new Dictionary<string, object>
        {
            ["IsEnabled"] = draft.ContoursEnabled,
            ["Auto"] = draft.ContoursAuto,
            ["Interval"] = draft.ContourInterval,
            ["IndexEvery"] = draft.ContourIndexEvery,
            ["Color"] = draft.ContourColorArgb,
            ["Opacity"] = draft.ContourOpacity,
            ["ShowLabels"] = draft.ContourShowLabels,
            ["Density"] = draft.ContourDensity
        }, new Dictionary<string, object>
        {
            ["IsEnabled"] = false, ["Auto"] = true, ["Interval"] = 10d,
            ["IndexEvery"] = 5, ["Color"] = -16777216, ["Opacity"] = 70,
            ["ShowLabels"] = true, ["Density"] = 1d
        });

        if (!raster.HasAttributes && !raster.HasElements && string.IsNullOrWhiteSpace(raster.Value))
            raster.Remove();
        return !XNode.DeepEquals(before, node);
    }

    private static bool ApplyPaletteMutation(XElement visualizationRoot,
        AssociatedRasterStylePaletteMutation? mutation, AssociatedRasterStyleDraft draft,
        UnitSystem units)
    {
        if (mutation is null) return false;
        XElement symbology = visualizationRoot.Element("Symbology") ??
            new XElement("Symbology");
        if (symbology.Parent is null) visualizationRoot.Add(symbology);
        switch (mutation.Action)
        {
            case "saveCustom":
            {
                string name = (mutation.Name ?? "").Trim();
                if (name.Length is 0 or > 128 ||
                    ColorPalettes.GetAllPalettes().Any(palette => NameEquals(palette.Name, name)) ||
                    ColorPalettes.GetValuesByUnit(units).Any(palette => NameEquals(palette.Name, name)) ||
                    symbology.Elements("CustomSymbology").Any(element => NameEquals((string?)element.Attribute("Name"), name)))
                    throw new ArgumentException("The custom palette name is empty, too long, or already in use.");
                symbology.Add(SerializeCustomPalette(name, draft));
                return true;
            }
            case "deleteCustom":
            {
                string name = mutation.PaletteID ?? "";
                XElement[] matches = symbology.Elements("CustomSymbology")
                    .Where(element => NameEquals((string?)element.Attribute("Name"), name)).ToArray();
                if (matches.Length != 1) throw new SourceFileChangedException();
                matches[0].Remove();
                return true;
            }
            default:
                throw new NotSupportedException("The requested palette operation is not supported.");
        }
    }

    private static XElement SerializeCustomPalette(string name, AssociatedRasterStyleDraft draft)
    {
        var element = new XElement("CustomSymbology",
            new XAttribute("Name", name),
            new XAttribute("ValueSource", draft.ValueSource),
            new XAttribute("IsChanged", draft.ValueSource != "Histogram"));
        foreach (AssociatedRasterStyleColorStop stop in draft.Stops)
        {
            if (!TryParseColor(stop.Color, out Color color)) throw new ArgumentException("The raster color is invalid.");
            var item = new XElement("Color", new XAttribute("R", color.R),
                new XAttribute("G", color.G), new XAttribute("B", color.B));
            bool includeValue = draft.ValueSource switch
            {
                "Manual" => true,
                "Linear" => stop.Index == 0 || stop.Index == draft.Stops.Length - 1,
                _ => false
            };
            if (includeValue)
                item.SetAttributeValue("Value", stop.Value.ToString("R", CultureInfo.InvariantCulture));
            element.Add(item);
        }
        return element;
    }

    private static void PatchChild(XElement parent, ref XElement? child, string name,
        IReadOnlyDictionary<string, object> values, IReadOnlyDictionary<string, object> defaults)
    {
        child ??= new XElement(name);
        foreach ((string key, object value) in values)
        {
            object fallback = defaults[key];
            bool same = NumericEquals(value, fallback);
            if (same) child.Attribute(key)?.Remove();
            else child.SetAttributeValue(key, XmlValue(value));
        }
        if (!child.HasAttributes && !child.HasElements && string.IsNullOrWhiteSpace(child.Value))
        {
            // Sparse source contexts omit children that are still at their
            // defaults. A freshly-created child has no parent yet; calling
            // Remove() on it throws instead of leaving the source XML sparse.
            if (child.Parent is not null) child.Remove();
            return;
        }
        if (child.Parent is null) parent.Add(child);
    }

    private static void SetDefaulted(XElement element, string name, object value, object defaultValue)
    {
        if (NumericEquals(value, defaultValue)) element.Attribute(name)?.Remove();
        else element.SetAttributeValue(name, XmlValue(value));
    }

    private static object XmlValue(object value) => value switch
    {
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => value
    };

    private static bool NumericEquals(object left, object right)
    {
        if (left is IConvertible && right is IConvertible &&
            left is not bool && right is not bool && left is not string && right is not string)
            return Convert.ToDouble(left, CultureInfo.InvariantCulture) == Convert.ToDouble(right, CultureInfo.InvariantCulture);
        return Equals(left, right);
    }

    private static bool PaletteMatchesDraft(PaletteInfo palette, AssociatedRasterStyleDraft draft)
    {
        if (palette.Colors.Length != draft.Stops.Length ||
            !string.Equals(palette.ValueSource, draft.ValueSource, StringComparison.Ordinal)) return false;
        for (int index = 0; index < draft.Stops.Length; index++)
            if (!string.Equals(ColorText(palette.Colors[index]), NormalizeColor(draft.Stops[index].Color), StringComparison.OrdinalIgnoreCase))
                return false;
        if (draft.ValueSource == "Histogram") return true;
        double[]? values = palette.Values;
        if (values is null || values.Length != draft.Stops.Length) return false;
        return values.Select((value, index) => Math.Abs(value - draft.Stops[index].Value) <=
                Math.Max(1e-12, Math.Abs(value) * 1e-10)).All(equal => equal);
    }

    private static AssociatedRasterStylePalette ToContract(PaletteInfo palette) => new(
        palette.Id, palette.Name, palette.Colors.Select(ColorText).ToArray(), palette.Values,
        palette.IsCustom, palette.ValueSource);

    private static string? PaletteID(AssociatedRasterStyleDraft draft,
        IReadOnlyList<PaletteInfo> palettes)
    {
        PaletteInfo[] matches = palettes.Where(palette => NameEquals(palette.Name, draft.Name) &&
            PaletteMatchesDraft(palette, draft)).ToArray();
        if (matches.Length == 1) return matches[0].Id;
        if (matches.Length > 1) return null;

        PaletteInfo[] contentMatches = palettes.Where(palette =>
            PaletteMatchesDraft(palette, draft)).ToArray();
        return contentMatches.Length == 1 ? contentMatches[0].Id : null;
    }

    private static PaletteInfo? FindPalette(IReadOnlyList<PaletteInfo> palettes, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;
        PaletteInfo? exact = FindPaletteByID(palettes, selector);
        if (exact is not null) return exact;

        // Existing .vis files store only the source palette name. Keep that
        // source resolver's original order for legacy name-only references.
        return palettes.FirstOrDefault(palette => NameEquals(palette.Name, selector));
    }

    private static PaletteInfo? FindPaletteByID(IReadOnlyList<PaletteInfo> palettes, string id)
    {
        PaletteInfo[] matches = palettes.Where(palette =>
            string.Equals(palette.Id, id, StringComparison.Ordinal)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static PaletteInfo? FindUniquePaletteByName(
        IReadOnlyList<PaletteInfo> palettes, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        PaletteInfo[] matches = palettes.Where(palette => NameEquals(palette.Name, name))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static List<PaletteInfo> ReadPalettes(XElement root, UnitSystem units, string kind,
        object source)
    {
        var defaults = new List<PaletteInfo>();
        var unitFills = ColorPalettes.GetValuesByUnit(units);
        var unitNames = unitFills.Select(fill => fill.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ColorPalette palette in ColorPalettes.GetAllPalettes())
        {
            if (palette.Name is not { Length: > 0 } name || unitNames.Contains(name)) continue;
            string valueSource = kind == "Terrain" ? "Histogram" : "Manual";
            double[]? values = kind == "Terrain"
                ? MakeHistogramValues(source, kind, palette.Colors.Count)
                : FitValues(NValueInitialRamp, palette.Colors.Count);
            defaults.Add(new(name, name, palette.Colors.ToArray(), values, valueSource, false));
        }
        defaults.AddRange(unitFills.Where(fill => !string.IsNullOrWhiteSpace(fill.Name))
            .Select(fill => new PaletteInfo(fill.Name, fill.Name, fill.Colors.ToArray(),
                fill.ColorValues.ToArray(), "Manual", false, PaletteSourceIdentity(fill))));

        XElement? customRoot = root.Element("Symbology");
        foreach (XElement custom in customRoot?.Elements("CustomSymbology") ?? [])
        {
            string name = (string?)custom.Attribute("Name") ?? "";
            if (name.Length == 0 || defaults.Any(item => NameEquals(item.Name, name))) continue;
            XElement[] colorElements = custom.Elements("Color").ToArray();
            var parsed = colorElements.Select(ReadColorStop).ToArray();
            if (parsed.Length < 2 || parsed.Any(stop => !stop.HasValue)) continue;
            (Color Color, double? Value)[] stops = parsed.Select(stop => stop!.Value).ToArray();
            Color[] colors = stops.Select(stop => stop.Color).ToArray();
            double[]? values = null;
            string valueSource = (string?)custom.Attribute("ValueSource") ?? "Histogram";
            if (valueSource == "Manual" && stops.All(stop => stop.Value.HasValue))
            {
                values = stops.Select(stop => stop.Value!.Value).ToArray();
            }
            else if (valueSource == "Linear" && stops[0].Value.HasValue && stops[^1].Value.HasValue)
                values = LinearValues(stops[0].Value!.Value, stops[^1].Value!.Value, stops.Length);
            else if (valueSource == "Histogram")
                values = MakeHistogramValues(source, kind, colors.Length);
            else continue;
            if (valueSource == "Manual" && values is null) continue;
            defaults.Add(new(name, name, colors, values, valueSource, true));
        }
        return AssignStablePaletteIDs(defaults);
    }

    private static List<PaletteInfo> AssignStablePaletteIDs(List<PaletteInfo> palettes)
    {
        var nameCounts = palettes.GroupBy(palette => palette.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        List<PaletteInfo> identified = palettes.Select(palette =>
        {
            if (nameCounts[palette.Name] == 1) return palette with { Id = palette.Name };
            string identity = palette.SourceIdentity ?? PaletteContentIdentity(palette);
            return palette with { Id = identity };
        }).ToList();

        if (identified.Select(palette => palette.Id)
                .Distinct(StringComparer.Ordinal).Count() != identified.Count)
            throw new InvalidDataException("The retained source palette identities are not unique.");
        return identified;
    }

    private static string PaletteSourceIdentity(InterpolatedSurfaceFill fill)
    {
        Type[] sourceTypes = [typeof(MetricSurfaceFills), typeof(USCustomarySurfaceFills),
            typeof(SurfaceFills)];
        foreach (Type sourceType in sourceTypes)
        {
            FieldInfo[] fields = sourceType.GetFields(BindingFlags.Public | BindingFlags.Static |
                BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                if (field.FieldType == typeof(InterpolatedSurfaceFill) &&
                    ReferenceEquals(field.GetValue(null), fill))
                    return $"{sourceType.FullName}.{field.Name}";
            }
        }
        return PaletteContentIdentity(new PaletteInfo(fill.Name, fill.Name,
            fill.Colors.ToArray(), fill.ColorValues.ToArray(), "Manual", false));
    }

    private static string PaletteContentIdentity(PaletteInfo palette)
    {
        string content = string.Join("|", new[]
        {
            palette.Name,
            string.Join(",", palette.Colors.Select(color =>
                $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}")),
            palette.Values is null ? "" : string.Join(",", palette.Values.Select(value =>
                value.ToString("R", CultureInfo.InvariantCulture)))
        });
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return $"{typeof(InterpolatedSurfaceFill).FullName}/{digest}";
    }

    private static (Color Color, double? Value)? ReadColorStop(XElement element)
    {
        if (!byte.TryParse((string?)element.Attribute("R"), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse((string?)element.Attribute("G"), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse((string?)element.Attribute("B"), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue)) return null;
        double? value = double.TryParse((string?)element.Attribute("Value"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double parsed) && double.IsFinite(parsed) ? parsed : null;
        return (Color.FromArgb(255, red, green, blue), value);
    }

    private static void ApplySavedSymbology(XElement saved, XElement node, object source,
        string kind, IReadOnlyList<PaletteInfo> palettes,
        ref RasterSymbology symbology, ref string valueSource)
    {
        string fillName = (string?)saved.Element("SurfaceFill")?.Attribute("Name") ??
            (string?)node.Attribute("Symbology") ?? symbology.SurfaceFill.Name ?? "";
        if (saved.Attribute("ValueSource") is { } sourceAttribute && IsValueSource(sourceAttribute.Value))
            valueSource = sourceAttribute.Value;

        PaletteInfo? named = FindPalette(palettes, fillName);
        if (named is not null)
        {
            RasterSymbology namedSymbology = DefaultSymbology(kind, source, named.Name,
                palettes, out string namedValueSource);
            symbology.SurfaceFill = namedSymbology.SurfaceFill;
            if (saved.Attribute("ValueSource") is null) valueSource = namedValueSource;
        }

        XElement? surface = saved.Element("SurfaceFill");
        if (surface is not null && TryReadRamp(surface, out Color[] colors, out double[] values))
            symbology.SurfaceFill = new InterpolatedSurfaceFill(colors, values,
                isStretched: true, regenForScreen: false, fillName);

        if (bool.TryParse((string?)saved.Attribute("IsSurfaceFillEnabled"), out bool enabled))
            symbology.IsSurfaceFillEnabled = enabled;
        if (byte.TryParse((string?)saved.Attribute("Alpha"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out byte alpha)) symbology.Alpha = alpha;
        if (Enum.TryParse((string?)saved.Attribute("InterpolationMode"), true,
                out InterpolationMode interpolation)) symbology.InterpolationMode = interpolation;
        if (float.TryParse((string?)saved.Attribute("SmoothingRadius"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float smoothing) && float.IsFinite(smoothing) && smoothing >= 0)
            symbology.SmoothingRadius = smoothing;
        if (float.TryParse((string?)saved.Attribute("ResampleCellSizeRatio"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float ratio) && float.IsFinite(ratio) && ratio >= 0)
            symbology.ResampleCellSizeRatio = ratio;

        XElement? hillshade = saved.Element("Hillshade");
        if (hillshade is not null)
        {
            if (bool.TryParse((string?)hillshade.Attribute("IsEnabled"), out bool hillshadeEnabled))
                symbology.HillshadeProperties.IsEnabled = hillshadeEnabled;
            if (Float(hillshade, "ZFactor") is { } z && z >= 0) symbology.HillshadeProperties.ZFactor = z;
            if (Float(hillshade, "Azimuth") is { } azimuth) symbology.HillshadeProperties.Azimuth = azimuth;
            if (Float(hillshade, "Zenith") is { } zenith) symbology.HillshadeProperties.Zenith = zenith;
        }

        XElement? contours = saved.Element("Contours");
        if (contours is not null)
        {
            ContourProperties props = symbology.ContourProperties;
            if (bool.TryParse((string?)contours.Attribute("IsEnabled"), out bool contourEnabled)) props.IsEnabled = contourEnabled;
            if (bool.TryParse((string?)contours.Attribute("Auto"), out bool auto)) props.AutoInterval = auto;
            if (Float(contours, "Interval") is { } interval && interval > 0) props.ContourInterval = interval;
            if (int.TryParse((string?)contours.Attribute("IndexEvery"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int every) && every >= 0) props.IndexEvery = every;
            if (int.TryParse((string?)contours.Attribute("Color"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int argb)) props.Color = Color.FromArgb(argb);
            if (int.TryParse((string?)contours.Attribute("Opacity"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int opacity)) props.Opacity = opacity;
            if (bool.TryParse((string?)contours.Attribute("ShowLabels"), out bool labels)) props.ShowLabels = labels;
            if (Float(contours, "Density") is { } density) props.Density = density;
        }
    }

    private static bool TryReadRamp(XElement surface, out Color[] colors, out double[] values)
    {
        var stops = surface.Elements("Color").Select(ReadColorStop)
            .Where(stop => stop.HasValue && stop.Value.Value.HasValue).Select(stop => stop!.Value).ToArray();
        colors = stops.Select(stop => stop.Color).ToArray();
        values = stops.Select(stop => stop.Value!.Value).ToArray();
        return colors.Length >= 2 && colors.Length == values.Length &&
            values.All(double.IsFinite) && values.Zip(values.Skip(1)).All(pair => pair.First < pair.Second);
    }

    private static double[] LinearValues(double minimum, double maximum, int count)
    {
        if (count < 2) return [minimum];
        return Enumerable.Range(0, count).Select(index => minimum +
            (maximum - minimum) * index / (count - 1)).ToArray();
    }

    private static double[] FitValues(double[] values, int count) =>
        values.Length == count ? values.ToArray() : LinearValues(values[0], values[^1], count);

    private static float? Float(XElement node, string name) =>
        float.TryParse((string?)node.Attribute(name), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : null;

    private static bool Finite(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static bool IsValueSource(string value) => value is "Manual" or "Linear" or "Histogram";

    private static bool FiniteExtent(Extent extent) => IsFiniteExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);

    private static bool IsFiniteExtent(double minX, double minY, double maxX, double maxY)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY) ||
            maxX <= minX || maxY <= minY) return false;
        double width = maxX - minX, height = maxY - minY;
        return double.IsFinite(width) && double.IsFinite(height) && double.IsFinite(width * height) && width * height > 0;
    }

    private static bool TryParseColor(string raw, out Color color)
    {
        color = Color.Empty;
        string hex = raw.StartsWith('#') ? raw[1..] : raw;
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = Color.FromArgb(255, (int)((rgb >> 16) & 0xff), (int)((rgb >> 8) & 0xff), (int)(rgb & 0xff));
            return true;
        }
        if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            color = Color.FromArgb(unchecked((int)argb));
            return true;
        }
        return false;
    }

    private static string NormalizeColor(string raw)
    {
        return TryParseColor(raw, out Color color) ? ColorText(color) : raw;
    }

    private static string ColorText(Color color) => $"#{color.ToArgb():X8}";

    private static AssociatedRasterStyleDependency[] CaptureDependencies(Project project, object source)
    {
        string sourcePath = SourcePath(source);
        string canonicalSource = CanonicalExisting(ModelPath(project, sourcePath));
        var dependencies = new List<AssociatedRasterStyleDependency>
        {
            new(canonicalSource, Revision(canonicalSource), "source-hdf")
        };
        switch (source)
        {
            case Terrain terrain:
                dependencies.AddRange(terrain.AllRasterInputs().Select(input =>
                    RasterDependency(project, input.Filename, "terrain-input-raster")));
                break;
            case NValueLayer nvalue when nvalue.Mode == NValueSource.ClassificationLayer:
            {
                ClassificationLayer? classification = nvalue.AssociatedClassificationLayer;
                if (classification is null)
                {
                    string expectedPath = string.IsNullOrWhiteSpace(nvalue.ClassificationLayerName)
                        ? "" : Path.Combine(project.ProjectDirectory, nvalue.ClassificationLayerName + ".h5");
                    dependencies.Add(RasterDependency(project, expectedPath, "classification-source-hdf"));
                    break;
                }
                ClassificationLayer[] owned = project.ClassificationLayers.Where(candidate =>
                    SameModelPath(project, candidate.Filename, classification.Filename)).ToArray();
                if (owned.Length != 1 || !ReferenceEquals(owned[0], classification))
                    throw new SourceFileChangedException();
                string classificationPath = CanonicalExisting(ModelPath(project, classification.Filename));
                dependencies.Add(new(classificationPath, Revision(classificationPath), "classification-source-hdf"));
                dependencies.AddRange(classification.AllRasterInputs().Select(input =>
                    RasterDependency(project, input.Filename, "classification-input-raster")));
                break;
            }
        }
        return dependencies
            .GroupBy(item => (item.Path, item.Role), new DependencyKeyComparer())
            .Select(group => group.First())
            .OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Path, PathComparer)
            .ToArray();
    }

    private static AssociatedRasterStyleDependency RasterDependency(Project project,
        string inputPath, string role)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) return new("", "missing", role);
        string path = Path.IsPathRooted(inputPath)
            ? inputPath : Path.Combine(project.ProjectDirectory, inputPath);
        string canonical = SourceFilePaths.Canonical(path);
        return File.Exists(path)
            ? new(canonical, Revision(path), role)
            : new(canonical, "missing", role);
    }

    private static void VerifyDependencies(IEnumerable<AssociatedRasterStyleDependency> dependencies)
    {
        foreach (AssociatedRasterStyleDependency dependency in dependencies)
        {
            if (dependency.Revision == "missing")
            {
                if (File.Exists(dependency.Path)) throw new SourceFileChangedException();
                continue;
            }
            if (!File.Exists(dependency.Path) ||
                !string.Equals(SourceFilePaths.Canonical(dependency.Path), dependency.Path, StringComparison.Ordinal) ||
                !string.Equals(Revision(dependency.Path), dependency.Revision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        }
    }

    private static string ContextToken(MapTreeRequest map, string projectCanonicalPath,
        string projectRevision, string projectXmlSHA, CapturedNode captured, string sourcePath, string sourceRevision,
        IEnumerable<AssociatedRasterStyleDependency> dependencies,
        IEnumerable<AssociatedRasterStyleDependency> ownerDependencies,
        IEnumerable<CatalogTreeNodeLocator> geometryNodes,
        IEnumerable<CatalogTreeNodeLocator> planNodes,
        MapResultGeometryOwner? resultOwner)
    {
        StringBuilder material = new();
        void Add(string value) => material.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':').Append(value).Append(';');

        Add(map.MapID);
        Add(map.MapIndex.ToString(CultureInfo.InvariantCulture));
        Add(map.MapName);
        Add(map.MapType);
        Add(projectCanonicalPath);
        Add(projectRevision);
        Add(projectXmlSHA);
        Add(map.ExpectedVisualizationRevision ?? "");
        Add(map.CoreLayerContext?.IdentityToken ?? "");
        Add(sourcePath);
        Add(sourceRevision);
        Add(captured.Node.ToString(SaveOptions.DisableFormatting));
        Add(captured.Root.Element("Symbology")?.ToString(SaveOptions.DisableFormatting) ?? "");
        foreach (CatalogTreeNodeLocator geometry in geometryNodes) Add(LocatorIdentity(geometry));
        foreach (CatalogTreeNodeLocator plan in planNodes) Add(LocatorIdentity(plan));
        if (resultOwner is not null)
        {
            Add(LocatorIdentity(resultOwner.ResultNode));
            Add(resultOwner.ContextToken);
            Add(resultOwner.SelectionIndex.ToString(CultureInfo.InvariantCulture));
            Add(resultOwner.SelectedResultPath);
            Add(resultOwner.SelectedResultRevision);
            foreach (MapResultSelectionOverride selection in resultOwner.SelectionOverrides ?? [])
            {
                Add(LocatorIdentity(selection.Node));
                Add(selection.SelectionIndex.ToString(CultureInfo.InvariantCulture));
                Add(selection.Mode);
                Add(selection.ResultPath ?? "");
                Add(selection.ResultRevision ?? "");
            }
        }
        foreach (AssociatedRasterStyleDependency dependency in dependencies)
        {
            Add(dependency.Role);
            Add(dependency.Path);
            Add(dependency.Revision);
        }
        foreach (AssociatedRasterStyleDependency dependency in ownerDependencies)
        {
            Add(dependency.Role);
            Add(dependency.Path);
            Add(dependency.Revision);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private static void VerifyResultOwnerAfterMutation(MapTreeRequest map,
        string visualizationRevision, MapResultGeometryOwner previousOwner)
    {
        MapTreeRequest refreshedMap = map with { ExpectedVisualizationRevision = visualizationRevision };
        using ResolvedMapResultContext refreshed = MapResultChartService.ResolveContext(
            refreshedMap, previousOwner.SelectionOverrides);
        MapResultGeometryOwner refreshedOwner = previousOwner with
        {
            ContextToken = refreshed.Sources.ContextToken
        };
        if (!TryResolveResultOwner(refreshed, refreshed.Tree.Roots, refreshedOwner,
                out ResolvedMapResultNode? selected) || selected is null ||
            !SamePath(selected.ResultPath, previousOwner.SelectedResultPath) ||
            !string.Equals(selected.Revision, previousOwner.SelectedResultRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
        refreshed.EnsureUnchanged();
    }

    private static AssociatedRasterStyleDependency[] MergeDependencies(
        IEnumerable<AssociatedRasterStyleDependency> source,
        IEnumerable<AssociatedRasterStyleDependency> owners) => source.Concat(owners)
        .GroupBy(item => (item.Path, item.Role), new DependencyKeyComparer())
        .Select(group => group.First())
        .OrderBy(item => item.Role, StringComparer.Ordinal)
        .ThenBy(item => item.Path, PathComparer).ToArray();

    private static string CanonicalExisting(string path)
    {
        string canonical = SourceFilePaths.Canonical(path);
        if (!File.Exists(canonical)) throw new FileNotFoundException("The selected raster source file was not found.", canonical);
        return canonical;
    }

    private static void VerifyRevision(string path, string expected)
    {
        if (!File.Exists(path) || !string.Equals(Revision(path), expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static void VerifyProjectRevision(string canonicalPath, string expected)
    {
        if (!File.Exists(canonicalPath) ||
            !string.Equals(SourceFilePaths.Canonical(canonicalPath), canonicalPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !string.Equals(Revision(canonicalPath), expected, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static string Revision(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string SourcePath(object source) => source switch
    {
        Terrain terrain => terrain.Filename,
        NValueLayer nvalue => nvalue.Filename,
        _ => throw new SourceFileChangedException()
    };

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsSHA256(string value) => value.Length == 64 && value == value.ToUpperInvariant()
        && value.All(Uri.IsHexDigit);

    private static bool NameEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record CapturedNode(XElement Node, XElement Root, string VisualizationRevision,
        CatalogTreeNode[] Roots);
    private sealed record StyleSourceRequest(MapTreeRequest Map, CatalogTreeNodeLocator Node,
        string SourcePath, string SourceRevision, string? ContextToken,
        CatalogTreeNodeLocator[]? GeometryNodes, CatalogTreeNodeLocator[]? PlanNodes,
        MapResultGeometryOwner? ResultOwner);
    private sealed record ResolvedStyleSource(Project Project, object Source,
        ResolvedMapResultContext? ResultContext,
        AssociatedRasterStyleDependency[] OwnerDependencies);
    private sealed record ResolvedPlanOwner(CatalogTreeNodeLocator Locator, string AssetPath,
        string Revision, Plan Source);
    private sealed record ResolvedGeometryOwner(CatalogTreeNodeLocator Locator, string AssetPath,
        string Revision, Geometry Source);
    private sealed class DependencyKeyComparer : IEqualityComparer<(string Path, string Role)>
    {
        public bool Equals((string Path, string Role) left, (string Path, string Role) right) =>
            PathComparer.Equals(left.Path, right.Path) &&
            string.Equals(left.Role, right.Role, StringComparison.Ordinal);
        public int GetHashCode((string Path, string Role) value) =>
            HashCode.Combine(PathComparer.GetHashCode(value.Path), StringComparer.Ordinal.GetHashCode(value.Role));
    }
    private sealed record PaletteInfo(string Id, string Name, Color[] Colors, double[]? Values,
        string ValueSource, bool IsCustom, string? SourceIdentity = null);
    private sealed record RasterStyleRead(RasterSymbology Symbology,
        AssociatedRasterStyleDraft Draft, AssociatedRasterStyleDraft ResetDraft,
        AssociatedRasterStylePalette[] Palettes, bool CanComputeHistogram,
        double[]? HistogramValues);
}

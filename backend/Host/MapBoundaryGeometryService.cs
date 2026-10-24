using Ras.Layers;
using Ras.Observables;
using Ras.Rendering;
using Utility.Files;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    /// <summary>
    /// Read-only map projection of source BoundaryCondition lines. Project and
    /// Plan rows load their exact source HDF; Result/MultiResult rows resolve
    /// the exact slot-zero Result and its serialized BoundaryCondition child.
    /// </summary>
    public static async Task<MapBoundaryGeometrySnapshot> LoadMapBoundaryGeometryAsync(
        MapBoundaryGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMapBoundaryGeometryRequest(request);
        string projectAliasPath = Path.GetFullPath(request.Map.ProjectPath);
        string canonicalProjectPath = SourceFilePaths.Canonical(projectAliasPath);
        string projectRevision = Revision(projectAliasPath);
        string? parentPlanPath = request.BoundaryNode.ParentPlanAssetID is { Length: > 0 } planAsset
            ? Path.GetFullPath(planAsset)
            : null;
        string? parentPlanRevision = parentPlanPath is null ? null : Revision(parentPlanPath);
        MapTreeSnapshot tree = MapTreeService.Load(request.Map);
        string visualizationRevision = tree.VisualizationRevision;
        if (parentPlanPath is not null && Revision(parentPlanPath) != parentPlanRevision)
            throw new SourceFileChangedException();
        ResolvedGeometryPlanOwner[] matchingPlanOwners = request.ResultOwner is null &&
                request.Map.MapType == "Plan" && request.BoundaryNode.RootName == "Matching Layers"
            ? CaptureBoundaryMatchingPlanOwners(tree.Roots, request.Map)
            : [];
        VerifyPlanOwnerRevisions(matchingPlanOwners);

        MapBoundaryLineData[] lines;
        string boundaryPath;
        string boundaryRevision;
        string boundarySourceState;
        bool boundaryChecked;
        bool boundaryEffectiveVisible;
        MapResultOwnerIdentity? resultOwnerIdentity = null;
        MapSourceRasterFrame? attachedFaces = null;
        string contextToken;

        if (request.ResultOwner is { } owner)
        {
            using ResolvedMapResultContext resultContext = MapResultChartService.ResolveContext(
                request.Map, owner.SelectionOverrides, owner.ContextToken);
            ResolvedMapResultNode selected = ResolveMapResultOwner(resultContext, owner.ResultNode,
                owner.SelectionIndex);
            RequireExactSelectedResult(selected, owner.SelectedResultPath, owner.SelectedResultRevision);
            CatalogTreeNode resultRow = ResolveExactMapNode(resultContext.Tree.Roots, owner.ResultNode);
            CatalogTreeNode boundaryRow = ResolveResultOwnedChild(resultRow, request.BoundaryNode,
                selected.ResultPath, selected.Revision, "BoundaryCondition");
            BoundaryCondition? condition = selected.Result.BoundaryConditions;
            if (condition is null || !string.Equals(condition.Name, boundaryRow.Name, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            boundaryPath = SourceFilePaths.Canonical(selected.ResultPath);
            boundaryRevision = selected.Revision;
            if (!string.Equals(request.ExpectedBoundaryRevision, boundaryRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
            var observableProject = new ObservableProject(resultContext.Project, Utility.SynchronizationContext.None);
            var observableResult = new ObservableResult(observableProject, selected.Result);
            ObservableBoundaryCondition observable = observableResult.BoundaryCondition;
            var editor = new BoundaryEditorService(condition, observable, boundaryPath,
                boundaryRevision, request.BoundarySourceState);
            BoundaryEditorData snapshot = editor.Snapshot();
            lines = ProjectBoundaryLines(snapshot.Lines);
            if (request.AttachedFacesViewport is { } attachedFacesViewport)
            {
                var baseline = new BoundaryConditionLineRenderLayer(condition.BoundaryConditionLines);
                var withAttachedFaces = new BoundaryConditionLineRenderLayer(condition.BoundaryConditionLines)
                {
                    ShowAttachedFaces = true
                };
                attachedFaces = MapSourceRasterRendering.Difference(
                    baseline, withAttachedFaces, attachedFacesViewport,
                    renderScale: request.RenderScale);
            }
            boundarySourceState = editor.State();
            boundaryChecked = boundaryRow.Checked == "checked";
            boundaryEffectiveVisible = MapTreeService.IsEffectivelyVisible(resultContext.Tree.Roots, boundaryRow);
            resultOwnerIdentity = new(owner.ResultNode, owner.ContextToken,
                owner.SelectionOverrides, owner.SelectionIndex, selected.ResultPath, selected.Revision);
            contextToken = BoundaryMapContextToken(request.Map, owner.ContextToken,
                owner.ResultNode, owner.SelectionIndex, selected.ResultPath, selected.Revision,
                request.BoundaryNode, boundaryPath, boundaryRevision, boundarySourceState,
                parentPlanRevision, matchingPlanOwners);
            resultContext.EnsureUnchanged();
            _ = ResolveResultOwnedChild(
                ResolveExactMapNode(resultContext.Tree.Roots, owner.ResultNode),
                request.BoundaryNode, selected.ResultPath, selected.Revision, "BoundaryCondition");
        }
        else
        {
            CatalogTreeNode boundaryRow = ResolveDirectBoundaryRow(tree.Roots, request.BoundaryNode,
                request.Map);
            string? rowAssetPath = boundaryRow.Locator?.AssociationAssetID ?? boundaryRow.AssetID ??
                boundaryRow.Locator?.AssetID;
            if (string.IsNullOrWhiteSpace(rowAssetPath)) throw new SourceFileChangedException();
            boundaryPath = Path.GetFullPath(rowAssetPath);
            string canonicalBoundaryPath = SourceFilePaths.Canonical(boundaryPath);
            boundaryRevision = Revision(canonicalBoundaryPath);
            if (!string.Equals(boundaryRevision, request.ExpectedBoundaryRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            ModelSnapshot snapshot = await LoadAsync(new ModelRequest(boundaryPath,
                "Boundary Conditions", request.Map.ProjectPath, request.Map.ProjectXml,
                request.BoundarySourceState, request.Map.CoreLayerContext));
            if (snapshot.Boundary is null || snapshot.SourceState is null)
                throw new InvalidDataException("The selected source has no boundary-line data.");
            lines = ProjectBoundaryLines(snapshot.Boundary.Lines);
            boundarySourceState = snapshot.SourceState;
            boundaryPath = canonicalBoundaryPath;
            boundaryRevision = snapshot.Revision;
            boundaryChecked = boundaryRow.Checked == "checked";
            boundaryEffectiveVisible = MapTreeService.IsEffectivelyVisible(tree.Roots, boundaryRow);
            contextToken = BoundaryMapContextToken(request.Map, null, null, 0, null, null,
                request.BoundaryNode, boundaryPath, boundaryRevision, boundarySourceState,
                parentPlanRevision, matchingPlanOwners);
        }

        if (request.ExpectedContextToken is { Length: > 0 } expectedContextToken &&
            !string.Equals(expectedContextToken, contextToken, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (Revision(boundaryPath) != boundaryRevision || Revision(projectAliasPath) != projectRevision ||
            SourceFilePaths.Canonical(projectAliasPath) != canonicalProjectPath ||
            parentPlanPath is not null && Revision(parentPlanPath) != parentPlanRevision ||
            !PlanOwnerRevisionsMatch(matchingPlanOwners))
            throw new SourceFileChangedException();

        MapTreeSnapshot finalTree = MapTreeService.Load(request.Map with
        { ExpectedVisualizationRevision = visualizationRevision });
        if (request.ResultOwner is { } finalOwner)
        {
            using ResolvedMapResultContext finalContext = MapResultChartService.ResolveContext(
                request.Map with { ExpectedVisualizationRevision = visualizationRevision },
                finalOwner.SelectionOverrides, finalOwner.ContextToken);
            ResolvedMapResultNode finalResult = ResolveMapResultOwner(finalContext,
                finalOwner.ResultNode, finalOwner.SelectionIndex);
            RequireExactSelectedResult(finalResult, finalOwner.SelectedResultPath,
                finalOwner.SelectedResultRevision);
            _ = ResolveResultOwnedChild(
                ResolveExactMapNode(finalContext.Tree.Roots, finalOwner.ResultNode),
                request.BoundaryNode, finalResult.ResultPath, finalResult.Revision, "BoundaryCondition");
            finalContext.EnsureUnchanged();
        }
        else
        {
            _ = ResolveDirectBoundaryRow(finalTree.Roots, request.BoundaryNode, request.Map);
        }
        ResolvedGeometryPlanOwner[] finalMatchingPlanOwners = request.ResultOwner is null &&
                request.Map.MapType == "Plan" && request.BoundaryNode.RootName == "Matching Layers"
            ? CaptureBoundaryMatchingPlanOwners(finalTree.Roots, request.Map)
            : [];
        if (finalTree.VisualizationRevision != visualizationRevision ||
            Revision(boundaryPath) != boundaryRevision || Revision(projectAliasPath) != projectRevision ||
            parentPlanPath is not null && Revision(parentPlanPath) != parentPlanRevision ||
            !PlanOwnerRevisionsMatch(finalMatchingPlanOwners) ||
            !SamePlanOwnerCaptures(matchingPlanOwners, finalMatchingPlanOwners))
            throw new SourceFileChangedException();

        return new MapBoundaryGeometrySnapshot(canonicalProjectPath, projectRevision,
            request.Map.ProjectXmlSHA, visualizationRevision, request.Map.MapID,
            request.Map.MapIndex, request.Map.MapName, request.Map.MapType,
            contextToken, request.BoundaryNode, boundaryPath, boundaryRevision,
            boundaryChecked, boundaryEffectiveVisible, boundarySourceState, lines,
            resultOwnerIdentity, attachedFaces);
    }

    private static void ValidateMapBoundaryGeometryRequest(MapBoundaryGeometryRequest request)
    {
        if (request.Map is null || request.BoundaryNode is null ||
            string.IsNullOrWhiteSpace(request.ExpectedBoundaryRevision))
            throw new ArgumentException("A saved map, exact BoundaryCondition row and source revision are required.");
        if (!double.IsFinite(request.RenderScale) || request.RenderScale <= 0 || request.RenderScale > 64)
            throw new ArgumentOutOfRangeException(nameof(request.RenderScale));
        if (request.BoundaryNode.NodeType != "BoundaryCondition")
            throw new ArgumentException("The selected map row is not a BoundaryCondition source.");
        if (request.ResultOwner is { } owner &&
            (owner.SelectionIndex != 0 || owner.ResultNode.NodeType is not ("Result" or "MultiResult")))
            throw new ArgumentException("A Result-owned boundary child must use its current slot-zero keyframe.");
        if (request.ResultOwner is null &&
            request.BoundaryNode.ParentResultAssetID is not null)
            throw new ArgumentException("A Result-owned BoundaryCondition requires its exact Result selection context.");
        if (request.AttachedFacesViewport is not null && request.ResultOwner is null)
            throw new ArgumentException("Attached boundary faces are available only from the exact selected Result.");
    }

    private static CatalogTreeNode ResolveDirectBoundaryRow(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator requested, MapTreeRequest map)
    {
        string mapType = map.MapType;
        if (mapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
            throw new NotSupportedException("Boundary source lines are supported only on saved Project, Buildable, Geometry and Plan maps.");
        if (requested.ParentResultAssetID is not null || requested.ParentResultNodePath is not null ||
            requested.ParentResultNodeName is not null || requested.ParentResultRevision is not null ||
            requested.ParentResultNodeType is not null || requested.ParentGeometryAssetID is not null ||
            requested.ParentGeometryNodePath is not null || requested.ParentGeometryNodeName is not null)
            throw new SourceFileChangedException();
        if (mapType == "Project")
        {
            if (requested.RootName != "Boundary Conditions" ||
                HasPlanOwner(requested) || requested.AssociationAssetID is not null ||
                requested.AssociationKind is not null)
                throw new SourceFileChangedException();
        }
        else if (mapType == "Plan" && requested.RootName == "Focused Plans")
        {
            ResolvePlanOwnedBoundaryRow(roots, requested);
        }
        else if (mapType == "Plan" && requested.RootName == "Matching Layers")
        {
            if (requested.AssociationKind != "BoundaryCondition" ||
                string.IsNullOrWhiteSpace(requested.AssociationAssetID) || HasPlanOwner(requested))
                throw new SourceFileChangedException();
            _ = CaptureBoundaryMatchingPlanOwners(roots, map);
            EnsurePlanBoundaryAssociation(roots, map, requested);
        }
        else if (mapType is "Buildable" or "Geometry")
        {
            // BuildableRasProjectTreeBase places direct BoundaryCondition rows
            // in Layers. GeometryTreeModeVM inherits that owner. These are
            // project-owned sources, not children of the focused Geometry.
            if (requested.RootName != "Layers" || HasPlanOwner(requested) ||
                requested.AssociationAssetID is not null || requested.AssociationKind is not null)
                throw new SourceFileChangedException();
        }
        else
        {
            throw new SourceFileChangedException();
        }

        CatalogTreeNode row = ResolveExactMapNode(roots, requested);
        if (row.AssetID is not { Length: > 0 } assetID ||
            requested.AssetID is not { Length: > 0 } requestedAssetID ||
            !string.Equals(SourceFilePaths.Canonical(assetID),
                SourceFilePaths.Canonical(requestedAssetID), PathComparison()))
            throw new SourceFileChangedException();
        if (requested.AssociationKind == "BoundaryCondition" &&
            (requested.AssociationAssetID is not { Length: > 0 } associationAssetID ||
             !string.Equals(SourceFilePaths.Canonical(assetID),
                 SourceFilePaths.Canonical(associationAssetID), PathComparison())))
            throw new SourceFileChangedException();
        return row;
    }

    private static CatalogTreeNode ResolvePlanOwnedBoundaryRow(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator requested)
    {
        if (requested.AssociationKind is not ("BoundaryCondition" or null))
            throw new SourceFileChangedException();
        CatalogTreeNode plan = ResolveExactPlanOwner(roots, requested);
        if (!(plan.Children ?? []).Any(child =>
                child.Locator is { } childLocator && SameLocator(childLocator, requested)))
            throw new SourceFileChangedException();
        return plan;
    }

    private static CatalogTreeNode ResolveExactPlanOwner(CatalogTreeNode[] roots,
        CatalogTreeNodeLocator requested)
    {
        if (string.IsNullOrWhiteSpace(requested.ParentPlanAssetID) ||
            requested.ParentPlanNodePath is not { Length: >= 2 } planPath ||
            string.IsNullOrWhiteSpace(requested.ParentPlanNodeName))
            throw new SourceFileChangedException();
        CatalogTreeNode[] plans = WalkMapNodes(roots).Where(node =>
        {
            CatalogTreeNodeLocator? locator = node.Locator;
            if (locator?.NodeType != "Plan" || locator.RootName != "Focused Plans" ||
                locator.NodeName != requested.ParentPlanNodeName || locator.NodePath is null ||
                !locator.NodePath.SequenceEqual(planPath) || node.AssetID is not { Length: > 0 } asset ||
                !string.Equals(SourceFilePaths.Canonical(asset),
                    SourceFilePaths.Canonical(requested.ParentPlanAssetID), PathComparison()))
                return false;
            return true;
        }).ToArray();
        if (plans.Length != 1) throw new SourceFileChangedException();
        return plans[0];
    }

    private static ResolvedGeometryPlanOwner[] CaptureBoundaryMatchingPlanOwners(
        CatalogTreeNode[] roots, MapTreeRequest map)
    {
        CatalogTreeNode[] focusedRoots = roots.Where(root => root.Name == "Focused Plans").ToArray();
        if (focusedRoots.Length != 1) throw new SourceFileChangedException();
        CatalogTreeNode[] plans = (focusedRoots[0].Children ?? [])
            .Where(node => node.Locator?.NodeType == "Plan").ToArray();
        if (plans.Length < 2) throw new SourceFileChangedException();

        ResolvedGeometryPlanOwner[] owners = plans.Select(plan =>
        {
            CatalogTreeNodeLocator locator = plan.Locator
                ?? throw new SourceFileChangedException();
            string path = Path.GetFullPath(plan.AssetID ?? locator.AssetID
                ?? throw new SourceFileChangedException());
            if (locator.RootName != "Focused Plans" || locator.NodePath is not { Length: >= 2 } ||
                !File.Exists(path))
                throw new SourceFileChangedException();
            return new ResolvedGeometryPlanOwner(locator, path,
                SourceFilePaths.Canonical(path), Revision(path));
        }).ToArray();
        VerifyPlanOwnerRevisions(owners);
        return owners;
    }

    private static void EnsurePlanBoundaryAssociation(CatalogTreeNode[] roots,
        MapTreeRequest map, CatalogTreeNodeLocator requested)
    {
        ResolvedGeometryPlanOwner[] planOwners = CaptureBoundaryMatchingPlanOwners(roots, map);
        CatalogTreeNode[] defaults = CatalogTreeService.LoadMapTreeDefaults(new CatalogTreeRequest(
            map.ProjectPath, map.ProjectXml, Mode: "Plan",
            FocusedPlanIds: planOwners.Select(owner => owner.Locator.AssetID!).ToArray()), "Plan");
        CatalogTreeNode matching = defaults.SingleOrDefault(root => root.Name == "Matching Layers")
            ?? throw new SourceFileChangedException();
        CatalogTreeNode[] matches = (matching.Children ?? []).Where(node =>
            node.Locator?.NodeType == "BoundaryCondition" && node.AssetID is { Length: > 0 } asset &&
            string.Equals(node.Name, requested.NodeName, StringComparison.Ordinal) &&
            requested.AssetID is { Length: > 0 } requestedAsset && SamePath(asset, requestedAsset)).ToArray();
        if (matches.Length != 1 || matches[0].AssetID is not { Length: > 0 } matchedAsset ||
            requested.AssociationAssetID is not { Length: > 0 } associationAsset ||
            !SamePath(matchedAsset, associationAsset))
            throw new SourceFileChangedException();
    }

    private static string BoundaryMapContextToken(MapTreeRequest map, string? resultContextToken,
        CatalogTreeNodeLocator? resultNode, int selectionIndex, string? resultPath,
        string? resultRevision, CatalogTreeNodeLocator boundaryNode, string boundaryPath,
        string boundaryRevision, string boundarySourceState, string? parentPlanRevision,
        ResolvedGeometryPlanOwner[] matchingPlanOwners)
    {
        string payload = string.Join("\n", "boundary-map-v1", Path.GetFullPath(map.ProjectPath),
            map.ProjectXmlSHA ?? "", map.MapID, map.MapIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            map.MapName, map.MapType, map.ExpectedVisualizationRevision ?? "",
            resultContextToken ?? "", resultNode is null ? "" : LocatorIdentity(resultNode),
            selectionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            resultPath is null ? "" : SourceFilePaths.Canonical(resultPath), resultRevision ?? "",
            LocatorIdentity(boundaryNode), SourceFilePaths.Canonical(boundaryPath), boundaryRevision,
            parentPlanRevision ?? "",
            string.Join("\n", matchingPlanOwners.Select(owner => string.Join("\u001f",
                LocatorIdentity(owner.Locator), owner.CanonicalPath, owner.Revision))),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(boundarySourceState))));
        return HashGeometryMapIdentity(payload);
    }

    private static MapBoundaryLineData[] ProjectBoundaryLines(BoundaryLineData[] lines) =>
        lines.Select((line, sourceOrdinal) => new MapBoundaryLineData(
            line.Id, line.Name, line.Geometry, sourceOrdinal)).ToArray();
}

using System.Xml.Linq;
using Ras.Layers;

namespace Ras.Backend;

public sealed record MapResultSelectionOverride(CatalogTreeNodeLocator Node, int SelectionIndex,
    string Mode, string? ResultPath = null, string? ResultRevision = null);

public sealed record MapResultFeatureIdentity(string VectorPath, string VectorRevision,
    string FeatureID, string CoordinateSHA);

public sealed record MapResultSourcesRequest(MapTreeRequest Map,
    MapResultSelectionOverride[]? SelectionOverrides = null);

public sealed record MapResultSelectionRequest(MapTreeRequest Map, string ExpectedContextToken,
    MapResultSelectionOverride[]? CurrentSelectionOverrides,
    MapResultSelectionOverride[]? SelectionOverrides);

public sealed record MapResultSampleLineRequest(MapTreeRequest Map, string ContextToken,
    MapResultSelectionOverride[]? SelectionOverrides, MapResultFeatureIdentity Feature,
    GeometryTopologyCoordinate[] SampleLine, string Mode);

public sealed record MapResultSourceChoice(string ResultPath, string Revision, string ResultName,
    string? PlanName, string? EndTime, bool IsComputing,
    string? ComputeState = null, string? ComputeFailureReason = null,
    string? DiagnosticStatus = null, CatalogTreeLayerError[]? LayerErrors = null,
    string? DiagnosticReadError = null);

public sealed record MapResultSelectionState(int SelectionIndex, string SelectionMode,
    MapResultSourceChoice? SelectedResult, MapResultSourceChoice[] Choices);

public sealed record MapResultActiveNode(CatalogTreeNodeLocator Node, string NodeKind,
    string Name, MapResultSelectionState[] Selections, string? SelectedMapType, int? ProfileIndex);

public sealed record MapResultSourcesResult(string ProjectPath, string ProjectRevision,
    string ProjectXmlSHA, string VisualizationRevision, string MapID, int MapIndex,
    string MapName, string MapType, string ContextToken, MapResultActiveNode[] ActiveNodes,
    CatalogTreeNode[]? TreeRoots = null,
    MapTreeResultSelectionOverride[]? TreeSelectionOverrides = null);

public sealed record MapResultSelectionSource(CatalogTreeNodeLocator Node, int SelectionIndex);

public sealed record MapResultSampleLineSeries(MapResultSelectionSource[] Selections,
    string? SelectedMapType, int? ProfileIndex, ResultProfileSampleLineSeries Series);

public sealed record MapResultSideEffect(string Code, string? Path);

public sealed record MapResultSampleLineResult(string ProjectPath, string ProjectRevision,
    string ProjectXmlSHA, string VisualizationRevision, string MapID, int MapIndex,
    string MapName, string MapType, string ContextToken, string FeatureContextToken,
    MapResultFeatureIdentity Feature, string Mode, GeometryTopologyCoordinate[] SampleLine,
    MapResultSampleLineSeries[] Series, MapResultSideEffect[] SideEffects);

internal sealed record ResolvedMapResultNode(MapResultActiveNode ActiveNode, int SelectionIndex,
    int[] SelectionIndices, Result Result,
    string ResultPath, string Revision, string? SelectedMapType, int? ProfileIndex,
    XElement SourceVisualNode, MapResultSelectionSource[] Selections);

internal sealed class ResolvedMapResultContext : IDisposable
{
    private Project? _project;

    internal MapTreeRequest Map { get; }
    internal string ProjectPath { get; }
    internal string ProjectRevision { get; }
    internal string ProjectXmlSHA { get; }
    internal string VisualizationRevision { get; }
    internal string VisualizationPath { get; }
    internal MapTreeSnapshot Tree { get; }
    internal MapResultSourcesResult Sources { get; }
    internal Project Project => _project ?? throw new ObjectDisposedException(nameof(ResolvedMapResultContext));
    internal IReadOnlyList<ResolvedMapResultNode> ResultContexts { get; }

    internal ResolvedMapResultContext(MapTreeRequest map, string projectPath,
        string projectRevision, string projectXmlSHA, string visualizationRevision,
        string visualizationPath, MapTreeSnapshot tree, Project project,
        MapResultSourcesResult sources, IReadOnlyList<ResolvedMapResultNode> resultContexts)
    {
        Map = map;
        ProjectPath = projectPath;
        ProjectRevision = projectRevision;
        ProjectXmlSHA = projectXmlSHA;
        VisualizationRevision = visualizationRevision;
        VisualizationPath = visualizationPath;
        Tree = tree;
        _project = project;
        Sources = sources;
        ResultContexts = resultContexts;
    }

    internal void EnsureUnchanged()
    {
        MapResultChartService.EnsureUnchanged(ProjectPath, ProjectRevision);
        MapResultChartService.EnsureUnchanged(VisualizationPath, VisualizationRevision);
        foreach (MapResultSourceChoice choice in Sources.ActiveNodes
                     .SelectMany(node => node.Selections)
                     .SelectMany(selection => selection.Choices)
                     .DistinctBy(choice => choice.ResultPath, StringComparer.Ordinal))
            MapResultChartService.EnsureUnchanged(choice.ResultPath, choice.Revision);
    }

    public void Dispose()
    {
        Project? project = Interlocked.Exchange(ref _project, null);
        if (project is null) return;
        MapResultChartService.DisposeProject(project);
    }
}

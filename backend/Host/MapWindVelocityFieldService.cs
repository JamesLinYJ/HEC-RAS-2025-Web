using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Files;

namespace Ras.Backend;

/// <summary>
/// Samples the actual WindLayer resampler belonging to one exact checked and
/// effectively-visible map Wind child. The parent BoundaryCondition and its
/// WindLayer child are both resolved from the live map tree; a parent row is
/// never treated as the selected Wind context.
/// </summary>
public static class MapWindVelocityFieldService
{
    private const int ValuesPerCell = 3;
    private const int MaximumVelocityRasterSide = 384;

    public static MapWindVelocityFieldResult Sample(MapWindVelocityFieldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        ArgumentNullException.ThrowIfNull(request.BoundaryNode);
        ArgumentNullException.ThrowIfNull(request.WindNode);
        if (request.BoundaryNode.NodeType != "BoundaryCondition" ||
            request.BoundaryNode.ParentResultAssetID is not null ||
            request.BoundaryNode.ParentResultNodeType is not null ||
            request.BoundaryNode.ParentResultNodePath is not null ||
            request.BoundaryNode.ParentResultNodeName is not null ||
            request.BoundaryNode.ParentResultRevision is not null ||
            request.WindNode.NodeType != "WindLayer" ||
            request.WindNode.ParentResultAssetID is not null ||
            request.WindNode.ParentResultNodeType is not null ||
            request.WindNode.ParentResultNodePath is not null ||
            request.WindNode.ParentResultNodeName is not null ||
            request.WindNode.ParentResultRevision is not null ||
            request.WindNode.RootName != request.BoundaryNode.RootName ||
            string.IsNullOrWhiteSpace(request.ContextToken) ||
            string.IsNullOrWhiteSpace(request.RequestID) ||
            string.IsNullOrWhiteSpace(request.ExpectedBoundaryPath) ||
            string.IsNullOrWhiteSpace(request.ExpectedBoundaryRevision))
            throw new ArgumentException("A Wind field request must identify the exact map Boundary Condition, Wind child, and profile.", nameof(request));

        string windAssetID = request.WindNode.AssetID
            ?? throw new ArgumentException("The Wind child has no source file identity.", nameof(request));
        string boundaryAssetID = request.BoundaryNode.AssetID
            ?? throw new ArgumentException("The Boundary Condition has no source file identity.", nameof(request));
        int[] windPath = request.WindNode.NodePath
            ?? throw new ArgumentException("The Wind child has no source-tree path.", nameof(request));
        int[] boundaryPathParts = request.BoundaryNode.NodePath
            ?? throw new ArgumentException("The Boundary Condition has no source-tree path.", nameof(request));
        if (request.BoundaryNode.ParentBoundaryConditionAssetID is not null ||
            request.BoundaryNode.ParentBoundaryConditionNodePath is not null ||
            request.BoundaryNode.ParentBoundaryConditionNodeName is not null)
            throw new ArgumentException("The selected Boundary Condition has an unexpected parent identity.", nameof(request));
        string windParentAssetID = request.WindNode.ParentBoundaryConditionAssetID
            ?? throw new ArgumentException("The Wind child has no parent Boundary Condition identity.", nameof(request));
        int[] windParentPath = request.WindNode.ParentBoundaryConditionNodePath
            ?? throw new ArgumentException("The Wind child has no parent Boundary Condition path.", nameof(request));
        string windParentName = request.WindNode.ParentBoundaryConditionNodeName
            ?? throw new ArgumentException("The Wind child has no parent Boundary Condition name.", nameof(request));
        string boundaryNodeName = request.BoundaryNode.NodeName;
        if (windAssetID.Length == 0 || boundaryAssetID.Length == 0 ||
            !PathEquals(windAssetID, boundaryAssetID) ||
            windPath.Length != boundaryPathParts.Length + 1 ||
            !windPath.Take(boundaryPathParts.Length).SequenceEqual(boundaryPathParts) ||
            !PathEquals(windParentAssetID, boundaryAssetID) ||
            !windParentPath.SequenceEqual(boundaryPathParts) ||
            !string.Equals(windParentName, boundaryNodeName, StringComparison.Ordinal))
            throw new ArgumentException("The Wind child does not belong to the selected Boundary Condition.", nameof(request));

        MapResultRenderService.ValidateViewport(request.Viewport);
        if (request.Viewport.PixelWidth > MaximumVelocityRasterSide ||
            request.Viewport.PixelHeight > MaximumVelocityRasterSide)
            throw new ArgumentException("The Wind velocity viewport exceeds the bounded raster dimensions.", nameof(request));
        string projectPath = SourceFilePaths.Canonical(URLPath(request.Map.ProjectPath));
        string visualizationPath = Path.ChangeExtension(projectPath, ".vis");
        string projectRevision = ComputeRevision(projectPath);
        string visualizationRevision = ComputeRevision(visualizationPath);
        MapTreeSnapshot tree = MapTreeService.Load(request.Map);
        if (!string.Equals(tree.VisualizationRevision, request.Map.ExpectedVisualizationRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        CatalogTreeNode[] treeNodes = Flatten(tree.Roots).ToArray();
        CatalogTreeNode row = treeNodes.SingleOrDefault(candidate =>
            candidate.Locator is { } locator && MapResultRenderService.SameLocator(locator, request.BoundaryNode))
            ?? throw new SourceFileChangedException();
        CatalogTreeNode windRow = treeNodes.SingleOrDefault(candidate =>
            candidate.Locator is { } locator && MapResultRenderService.SameLocator(locator, request.WindNode))
            ?? throw new SourceFileChangedException();
        CatalogTreeNodeLocator actualBoundaryLocator = row.Locator
            ?? throw new SourceFileChangedException();
        CatalogTreeNodeLocator actualWindLocator = windRow.Locator
            ?? throw new SourceFileChangedException();
        if (actualBoundaryLocator.ParentResultNodeType is not null ||
            actualBoundaryLocator.ParentResultAssetID is not null ||
            actualBoundaryLocator.ParentResultNodePath is not null ||
            actualBoundaryLocator.ParentResultNodeName is not null ||
            actualBoundaryLocator.ParentResultRevision is not null ||
            actualBoundaryLocator.ParentBoundaryConditionAssetID is not null ||
            actualBoundaryLocator.ParentBoundaryConditionNodePath is not null ||
            actualBoundaryLocator.ParentBoundaryConditionNodeName is not null ||
            actualWindLocator.ParentResultNodeType is not null ||
            actualWindLocator.ParentResultAssetID is not null ||
            actualWindLocator.ParentResultNodePath is not null ||
            actualWindLocator.ParentResultNodeName is not null ||
            actualWindLocator.ParentResultRevision is not null ||
            string.IsNullOrWhiteSpace(actualBoundaryLocator.AssetID) ||
            string.IsNullOrWhiteSpace(actualWindLocator.ParentBoundaryConditionAssetID) ||
            actualWindLocator.ParentBoundaryConditionNodePath is null ||
            string.IsNullOrWhiteSpace(actualWindLocator.ParentBoundaryConditionNodeName) ||
            !PathEquals(actualWindLocator.ParentBoundaryConditionAssetID,
                actualBoundaryLocator.AssetID) ||
            !(actualWindLocator.ParentBoundaryConditionNodePath ?? []).SequenceEqual(actualBoundaryLocator.NodePath ?? []) ||
            !string.Equals(actualWindLocator.ParentBoundaryConditionNodeName,
                actualBoundaryLocator.NodeName, StringComparison.Ordinal))
            throw new SourceFileChangedException();
        if (row.Children?.Any(child => child.Id == windRow.Id) != true ||
            windRow.Checked != "checked" || !MapTreeService.IsEffectivelyVisible(tree.Roots, windRow))
            throw new InvalidOperationException("The selected Wind child is not active and visible on this map.");

        string? rowPath = row.Locator?.AssociationAssetID ?? row.AssetID ?? row.Locator?.AssetID;
        if (string.IsNullOrWhiteSpace(rowPath)) throw new SourceFileChangedException();
        string boundaryPath = SourceFilePaths.Canonical(URLPath(rowPath));
        string expectedBoundaryPath = SourceFilePaths.Canonical(URLPath(request.ExpectedBoundaryPath));
        if (!string.Equals(boundaryPath, expectedBoundaryPath, PathComparison) ||
            !string.Equals(ComputeRevision(boundaryPath), request.ExpectedBoundaryRevision,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        Project project = CoreLayerImportService.OpenProjectWithMetadata(projectPath,
            ParseProjectXml(request.Map.ProjectXml
                ?? throw new ArgumentException("The current project XML context is unavailable.", nameof(request))),
            request.Map.CoreLayerContext);
        try
        {
            BoundaryCondition boundary = project.BoundaryConditions.SingleOrDefault(condition =>
                PathEquals(condition.Filename, boundaryPath) &&
                string.Equals(condition.Name, request.BoundaryNode.NodeName, StringComparison.Ordinal))
                ?? throw new SourceFileChangedException();
            WindLayer wind = boundary.Wind
                ?? throw new InvalidDataException("The selected Boundary Condition has no Wind layer.");
            if (!wind.IsEnabled)
                throw new InvalidOperationException("The selected Wind layer is disabled in the source Boundary Condition.");
            if (wind.SpatialDataType == SpatialDataType.Point)
                throw new NotSupportedException("The retained Wind Point resampler is not implemented.");

            ITimeSeriesVelocityResampler<float> resampler = wind.Resampler
                ?? throw new InvalidDataException("The selected Wind layer has no source velocity resampler.");
            IReadOnlyList<DateTime> times = resampler.Times
                ?? throw new InvalidDataException("The selected Wind layer has no source profile times.");
            if (request.ProfileIndex < 0 || request.ProfileIndex >= times.Count ||
                resampler.ProfileCount != times.Count)
                throw new ArgumentOutOfRangeException(nameof(request.ProfileIndex),
                    "The selected Wind profile is outside the source resampler timeline.");

            string sourceTime = times[request.ProfileIndex].ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (request.ExpectedProfileTime is { Length: > 0 } expectedTime &&
                !string.Equals(sourceTime, expectedTime, StringComparison.Ordinal))
                throw new SourceFileChangedException();

            var definition = new RasterDefinition(new Extent(request.Viewport.MinX,
                request.Viewport.MaxX, request.Viewport.MinY, request.Viewport.MaxY),
                request.Viewport.PixelHeight, request.Viewport.PixelWidth);
            if (!definition.IsValid() || definition.CellCount <= 0 ||
                !double.IsFinite(definition.CellSize) || definition.CellSize <= 0)
                throw new ArgumentException("The current map viewport cannot be sampled as a Wind velocity field.", nameof(request));

            float[] velocityX = new float[definition.CellCount];
            float[] velocityY = new float[definition.CellCount];
            float[] magnitude = new float[definition.CellCount];
            wind.ResampleVxVy(new ResampleArgs(definition), request.ProfileIndex,
                velocityX.AsMemory(), velocityY.AsMemory(), magnitude.AsMemory());
            byte[] interleaved = PackVelocity(velocityX, velocityY, magnitude);

            if (!string.Equals(ComputeRevision(boundaryPath), request.ExpectedBoundaryRevision,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ComputeRevision(projectPath), projectRevision, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ComputeRevision(visualizationPath), visualizationRevision, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(MapTreeService.Load(request.Map with
                    { ExpectedVisualizationRevision = visualizationRevision }).VisualizationRevision,
                    visualizationRevision, StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();

            return new MapWindVelocityFieldResult(request.RequestID, request.ContextToken,
                MapResultRenderService.NodeIdentity(request.WindNode), request.Map.MapID,
                request.BoundaryNode, request.WindNode, boundaryPath,
                request.ExpectedBoundaryRevision, wind.SpatialDataType.ToString(),
                request.ProfileIndex, sourceTime, sourceTime,
                times.Select((time, index) => new MapWindVelocityProfile(index,
                    time.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).ToArray(),
                definition.Cols, definition.Rows, definition.CellSize,
                definition.MinX, definition.MinY, definition.MaxX, definition.MaxY,
                resampler.NoDataValue, interleaved);
        }
        finally
        {
            MapResultChartService.DisposeProject(project);
        }
    }

    private static XElement ParseProjectXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("The current project XML context is unavailable.");
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8L * 1024 * 1024
        });
        return XElement.Load(reader, LoadOptions.None);
    }

    private static IEnumerable<CatalogTreeNode> Flatten(IEnumerable<CatalogTreeNode> nodes)
    {
        foreach (CatalogTreeNode node in nodes)
        {
            yield return node;
            if (node.Children is { Length: > 0 } children)
                foreach (CatalogTreeNode child in Flatten(children)) yield return child;
        }
    }

    private static byte[] PackVelocity(float[] velocityX, float[] velocityY, float[] magnitude)
    {
        if (velocityX.Length != velocityY.Length || velocityX.Length != magnitude.Length)
            throw new InvalidDataException("The source Wind resampler returned arrays with different dimensions.");
        byte[] bytes = new byte[checked(velocityX.Length * ValuesPerCell * sizeof(float))];
        for (int index = 0; index < velocityX.Length; index++)
        {
            int offset = checked(index * ValuesPerCell * sizeof(float));
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, sizeof(float)), velocityX[index]);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + sizeof(float), sizeof(float)), velocityY[index]);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 2 * sizeof(float), sizeof(float)), magnitude[index]);
        }
        return bytes;
    }

    private static string ComputeRevision(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string URLPath(string path) => Path.GetFullPath(path);
    private static bool PathEquals(string first, string second) =>
        string.Equals(SourceFilePaths.Canonical(URLPath(first)), SourceFilePaths.Canonical(URLPath(second)), PathComparison);
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

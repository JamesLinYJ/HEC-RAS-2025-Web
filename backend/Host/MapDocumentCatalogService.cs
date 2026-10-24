using System.Xml.Linq;

namespace Ras.Backend;

public sealed record MapDocumentListRequest(
    string ProjectPath,
    string? ProjectXml,
    string? ProjectXmlSHA,
    string ExpectedProjectRevision);

public sealed record MapDocumentDescriptor(int MapIndex, string MapName, string MapType);

public sealed record MapDocumentListSnapshot(
    string ProjectRevision,
    string? VisualizationRevision,
    MapDocumentDescriptor[] Maps,
    int? ActiveMapIndex);

public static partial class MapTreeService
{
    /// <summary>Discovers saved map identities without creating or updating the visualization file.</summary>
    public static MapDocumentListSnapshot ListMaps(MapDocumentListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExpectedProjectRevision))
            throw new ArgumentException("The saved project revision is required to list its map documents.");

        using var context = Open(request.ProjectPath, request.ProjectXml, request.ProjectXmlSHA,
            expectedVisualizationRevision: null, allowMissing: true, discoverVisualizationRevision: true);
        if (!SameRevision(context.ProjectRevision, request.ExpectedProjectRevision))
            throw new SourceFileChangedException();

        XElement? mapsElement = context.Visualization.Root?.Element("Maps");
        MapDocumentDescriptor[] maps = mapsElement?.Elements("Map")
            .Select((map, index) => new MapDocumentDescriptor(index,
                (string?)map.Attribute("Name") ?? "",
                (string?)map.Attribute("Type") ?? ""))
            .ToArray() ?? [];
        string? activeName = (string?)mapsElement?.Attribute("Maps");
        int activeIndex = activeName is null ? -1 : Array.FindIndex(maps, map => map.MapName == activeName);

        // The same identity guards used by commits also reject a project or .vis
        // replaced while this read-only discovery was in flight.
        VerifyBeforeCommit(context);
        return new(context.ProjectRevision, context.PreviousVisualizationRevision,
            maps, activeIndex < 0 ? null : activeIndex);
    }
}

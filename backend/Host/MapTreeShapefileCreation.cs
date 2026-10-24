using System.Xml.Linq;

namespace Ras.Backend;

public static partial class MapTreeService
{
    /// <summary>
    /// Validates the same saved map owner used by ImportShapefileVector while
    /// the empty file family is being prepared. This is deliberately read-only:
    /// the subsequent existing import operation owns the .vis transaction.
    /// </summary>
    internal static string ValidateNewShapefileMap(NewShapefileCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Map);
        MapDocumentRequest owner = request.Map;
        if (!Guid.TryParse(request.ProjectSessionID, out _) ||
            !Guid.TryParse(request.MapID, out _) ||
            !string.Equals(owner.MapID, request.MapID, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Shapefile project and map session identity is incomplete.");

        using OperationContext context = Open(owner.ProjectPath, owner.ProjectXml, owner.ProjectXmlSHA,
            owner.ExpectedVisualizationRevision, allowMissing: owner.MapIndex is null,
            projectSessionID: owner.CoreLayerContext?.ProjectSessionID,
            externalCoreLayerReferences: owner.CoreLayerContext?.References,
            coreLayerContext: owner.CoreLayerContext);
        if (!IsSHA256(request.ExpectedProjectRevision) ||
            !context.ProjectRevision.Equals(request.ExpectedProjectRevision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        if (owner.MapIndex is int index)
        {
            if (owner.MapName is null || owner.MapType is null ||
                owner.Kind != owner.MapType ||
                owner.MapType is not ("Project" or "Buildable" or "Geometry" or "Plan"))
                throw new ArgumentException("The selected saved map identity is incomplete.");
            _ = ResolveMap(context.Visualization, index, owner.MapName, owner.MapType);
        }
        else
        {
            XElement[] maps = context.Visualization.Root!.Element("Maps")?.Elements("Map").ToArray() ?? [];
            if (owner.Kind != "Project" || owner.MapType is not (null or "Project") || maps.Length != 0)
                throw new SourceFileChangedException();
        }

        return context.ProjectRevision;
    }

    private static bool IsSHA256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');
}

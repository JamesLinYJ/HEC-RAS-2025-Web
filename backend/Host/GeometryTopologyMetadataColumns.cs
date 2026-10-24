using Visual.Mapping.Observables;
using Geospatial.Vectors.Topology;
using Utility.Memory;
using Visual.Observables;

namespace Ras.Backend;

/// <summary>
/// Applies source-supported Arc and ControlPoint metadata-column edits to the
/// topology draft's observable tables. GeometryTopologyService owns the draft
/// replay and parent-save transaction; this helper never writes HDF directly.
/// </summary>
public static partial class ModelEditorAdapter
{
    private static bool ApplyGeometryTopologyMetadataColumnMutation(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        ValidateMetadataColumnMutationShape(mutation);
        (ObservableTable table, Func<string, bool> isUserDefined) = MetadataColumnTarget(topology, mutation.FeatureKind);

        return mutation.Operation switch
        {
            "addMetadataColumn" => AddMetadataColumn(table, isUserDefined, mutation.FieldID, mutation.Value),
            "renameMetadataColumn" => RenameMetadataColumn(table, isUserDefined, mutation.FieldID, mutation.Value),
            "deleteMetadataColumn" => DeleteMetadataColumn(table, isUserDefined, mutation.FieldID),
            _ => throw new ArgumentException("Unsupported topology metadata-column operation.")
        };
    }

    private static bool IsNoOpGeometryTopologyMetadataColumnMutation(
        ObservableConceptualMeshTopology topology, GeometryTopologyMutation mutation)
    {
        ValidateMetadataColumnMutationShape(mutation);
        (ObservableTable table, Func<string, bool> isUserDefined) = MetadataColumnTarget(topology, mutation.FeatureKind);
        int index = FindUserColumnIndex(table, isUserDefined, mutation.FieldID);
        string newName = mutation.Value ?? throw new ArgumentException("A replacement topology metadata column name is required.");
        ValidateUserColumnName(newName, isUserDefined);
        if (table.Columns.Where((_, columnIndex) => columnIndex != index)
            .Any(column => string.Equals(column.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A topology metadata column with that name already exists.");
        return string.Equals(table.Columns[index].Name, newName, StringComparison.Ordinal);
    }

    private static (ObservableTable Table, Func<string, bool> IsUserDefined) MetadataColumnTarget(
        ObservableConceptualMeshTopology topology, string featureKind) => featureKind switch
    {
        "arc" => (topology.ArcMetadata, MeshExtensions.IsUserDefinedArcColumn),
        // The source Regions editor is backed by ControlPointMetadata.
        "region" => (topology.ControlPointMetadata, MeshExtensions.IsUserDefinedControlPointColumn),
        _ => throw new ArgumentException("Metadata columns are supported only for topology arcs and regions.")
    };

    private static bool AddMetadataColumn(ObservableTable table, Func<string, bool> isUserDefined,
        string? name, string? sourceType)
    {
        ValidateUserColumnName(name, isUserDefined);
        if (table.Columns.Any(column => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A topology metadata column with that name already exists.");
        if (!Enum.TryParse(sourceType, ignoreCase: false, out TableType type)
            || !Enum.IsDefined(typeof(TableType), type))
            throw new ArgumentException("The topology metadata column type is unsupported.");

        // ObservableTable owns row defaults, notifications, and save tracking.
        if (!table.TryEnsureColumn(name!, type.AsType(), out _))
            throw new InvalidOperationException("The topology metadata column could not be added.");
        return true;
    }

    private static bool RenameMetadataColumn(ObservableTable table, Func<string, bool> isUserDefined,
        string? oldName, string? newName)
    {
        int index = FindUserColumnIndex(table, isUserDefined, oldName);
        ValidateUserColumnName(newName, isUserDefined);
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return false;
        if (table.Columns.Where((_, columnIndex) => columnIndex != index)
            .Any(column => string.Equals(column.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A topology metadata column with that name already exists.");

        if (!table.TryRenameColumn(index, newName!))
            throw new ArgumentException("The topology metadata column could not be renamed.");
        return true;
    }

    private static bool DeleteMetadataColumn(ObservableTable table, Func<string, bool> isUserDefined,
        string? name)
    {
        int index = FindUserColumnIndex(table, isUserDefined, name);
        table.RemoveColumn(index);
        return true;
    }

    private static int FindUserColumnIndex(ObservableTable table, Func<string, bool> isUserDefined,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A topology metadata column name is required.");
        for (int index = 0; index < table.Columns.Count; index++)
        {
            if (!string.Equals(table.Columns[index].Name, name, StringComparison.Ordinal)) continue;
            if (!isUserDefined(name))
                throw new ArgumentException("Source-defined topology columns cannot be renamed or deleted.");
            return index;
        }
        throw new ArgumentException("The topology metadata column no longer exists.");
    }

    private static void ValidateUserColumnName(string? name, Func<string, bool> isUserDefined)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A topology metadata column name is required.");
        if (!isUserDefined(name))
            throw new ArgumentException("The requested name is reserved by the source topology schema.");
    }

    private static void ValidateMetadataColumnMutationShape(GeometryTopologyMutation mutation)
    {
        if (mutation.FeatureIDs is null || mutation.FeatureIDs.Length != 0
            || mutation.TagName is not null || mutation.TagType is not null || mutation.TagValue is not null
            || mutation.ApplyToSelectedOnly || mutation.FactorText is not null
            || mutation.ArcFeatureIDs is null || mutation.ArcFeatureIDs.Length != 0
            || mutation.ControlPointFeatureIDs is null || mutation.ControlPointFeatureIDs.Length != 0
            || mutation.NodeFeatureIDs is null || mutation.NodeFeatureIDs.Length != 0
            || mutation.RecipeText is not null || mutation.ForceSet
            || mutation.PointKind is not null || mutation.InternalPointIndex is not null
            || mutation.EndpointNodeIndex is not null || mutation.Axis is not null || mutation.RawValue is not null)
            throw new ArgumentException("A topology metadata-column command has an invalid shape.");

        bool validFields = mutation.Operation switch
        {
            "addMetadataColumn" => mutation.FieldID is not null && mutation.Value is not null,
            "renameMetadataColumn" => mutation.FieldID is not null && mutation.Value is not null,
            "deleteMetadataColumn" => mutation.FieldID is not null && mutation.Value is null,
            _ => false
        };
        if (!validFields)
            throw new ArgumentException("A topology metadata-column command requires the fields for its operation.");
    }
}

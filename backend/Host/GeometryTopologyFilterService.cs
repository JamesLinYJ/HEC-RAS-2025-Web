using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;
using Utility.Filtering;
using Utility.Helpers;
using Utility.Memory;
using Visual.Mapping.Observables;
using Visual.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    /// <summary>
    /// Applies the retained TagFilter predicate to the exact replayed topology
    /// draft. The source geometry is read-only; feature ids and metadata values
    /// come from its Observable topology tables rather than the display rows.
    /// </summary>
    public static async Task<GeometryTopologyFilterSnapshot> FilterGeometryTopologyAsync(
        GeometryTopologyFilterRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Model is null) throw new ArgumentException("A topology filter model is required.");
        if (request.Clauses is null) throw new ArgumentException("Topology filter clauses are required.");

        VerifyGeometryTopologyModel(request.Model);
        string revision = Revision(request.Model.AssetPath);
        if (!string.Equals(revision, request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        GeometryTopologyDraftState state = ReadTopologyDraft(request.DraftState,
            request.Model.AssetPath, request.Revision);
        if (!string.Equals(Revision(request.Model.AssetPath), request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        string featureKind = request.FeatureKind?.ToLowerInvariant() ?? "";
        if (featureKind is not ("arc" or "region"))
            throw new ArgumentException("The topology filter feature type is invalid.");

        using var context = new ModelContext(ToModelRequest(request.Model));
        var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, state);
        var (geometry, topology) = GeometryTopologyFor(context, requireEditable: false);
        _ = geometry;

        ObservableTable table;
        IReadOnlyAdvancedCollection<ObservableTagDefinition> definitions;
        Type featureType;
        IEnumerable<ObservableTopologyFeature> features;
        if (featureKind == "arc")
        {
            table = topology.ArcMetadata;
            definitions = topology.ArcTagDefinitions;
            featureType = typeof(ObservableConceptualMeshArc);
            features = topology.Arcs.Cast<ObservableTopologyFeature>();
        }
        else
        {
            // The source editor calls these rows regions; their tagged feature
            // objects are ObservableConceptualMeshControlPoint instances.
            table = topology.ControlPointMetadata;
            definitions = topology.ControlPointTagDefinitions;
            featureType = typeof(ObservableConceptualMeshControlPoint);
            features = topology.ControlPoints.Cast<ObservableTopologyFeature>();
        }

        GeometryTopologyPropertyDescriptor[] propertyDescriptors = FilterPropertyDescriptors(featureType);
        GeometryTopologyTagDefinition[] tagDefinitions = definitions
            .Select(definition => new GeometryTopologyTagDefinition(definition.Key, definition.Type.ToString()))
            .ToArray();
        ResolvedTopologyFilterClause[] clauses = ResolveFilterClauses(
            request.Clauses, definitions, propertyDescriptors, table);

        ResolvedTopologyFilterClause[] individual = clauses
            .Where(clause => !clause.IsNoOp && clause.TagDefinition is null)
            .ToArray();
        IGrouping<ObservableTagDefinition, ResolvedTopologyFilterClause>[] tagGroups = clauses
            .Where(clause => !clause.IsNoOp && clause.TagDefinition is not null)
            .GroupBy(clause => clause.TagDefinition
                ?? throw new InvalidOperationException("A tag filter group has no source definition."))
            .ToArray();

        int[] matchedFeatureIDs = features
            .Where(feature => MatchesTopologyFilter(feature, individual, tagGroups))
            .Select(feature => feature.Fid)
            .ToArray();

        string encodedDraft = JsonSerializer.Serialize(replay.State, BackendJson.Options);
        string draftIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encodedDraft)))
            .ToLowerInvariant();
        if (!string.Equals(Revision(request.Model.AssetPath), request.Revision, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        var result = new GeometryTopologyFilterSnapshot(request.Revision, draftIdentity, featureKind,
            matchedFeatureIDs, propertyDescriptors, tagDefinitions);
        return await Task.FromResult(result);
    }

    private static GeometryTopologyPropertyDescriptor[] FilterPropertyDescriptors(Type featureType)
    {
        // Mirrors PropertyDescriptorFactory.BuildFromType without taking a
        // dependency on the Windows UI assembly: the retained attribute is the
        // source of both the visible property name and its table column.
        return featureType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (Property: property,
                Attribute: property.GetCustomAttribute<NotifyPropertyChangedForTableColumnAttribute>()))
            .Where(item => item.Attribute is not null)
            .Select(item =>
            {
                NotifyPropertyChangedForTableColumnAttribute attribute = item.Attribute
                    ?? throw new InvalidOperationException("A topology property descriptor has no source column.");
                return new GeometryTopologyPropertyDescriptor(item.Property.Name,
                    attribute.ColumnName, SourceTypeName(item.Property.PropertyType),
                    IsEnum: item.Property.PropertyType.IsEnum);
            })
            .OrderBy(descriptor => descriptor.Name)
            .ToArray();
    }

    private static ResolvedTopologyFilterClause[] ResolveFilterClauses(
        GeometryTopologyFilterClause[] clauses,
        IReadOnlyAdvancedCollection<ObservableTagDefinition> definitions,
        GeometryTopologyPropertyDescriptor[] descriptors,
        ObservableTable table)
    {
        var resolved = new List<ResolvedTopologyFilterClause>(clauses.Length);
        foreach (GeometryTopologyFilterClause? clause in clauses)
        {
            if (clause is null) throw new ArgumentException("The topology filter clause is invalid.");
            switch (clause.Type)
            {
                case "Tag":
                {
                    // A TagFilterItem with no selected definition is a source
                    // no-op. The other selector is ignored by TagFilterItem.
                    if (clause.TagDefinitionID is null)
                    {
                        resolved.Add(ResolvedTopologyFilterClause.NoOp);
                        break;
                    }
                    if (!TryReadFilterOperator(clause.Operator, out FilterCondition op))
                        throw new ArgumentException("The topology filter clause is invalid.");
                    ObservableTagDefinition[] matches = definitions
                        .Where(definition => string.Equals(definition.Key, clause.TagDefinitionID,
                            StringComparison.Ordinal))
                        .ToArray();
                    if (matches.Length != 1)
                        throw new ArgumentException("The selected topology filter source is no longer available.");
                    resolved.Add(new ResolvedTopologyFilterClause(op, clause.Value, matches[0], null, false));
                    break;
                }
                case "Property":
                {
                    // A Property TagFilterItem without a descriptor is also a
                    // source no-op, even if its unused Tag selector is set.
                    if (clause.PropertyColumnID is null)
                    {
                        resolved.Add(ResolvedTopologyFilterClause.NoOp);
                        break;
                    }
                    if (!TryReadFilterOperator(clause.Operator, out FilterCondition op))
                        throw new ArgumentException("The topology filter clause is invalid.");
                    GeometryTopologyPropertyDescriptor[] descriptorMatches = descriptors
                        .Where(descriptor => string.Equals(descriptor.ColumnName, clause.PropertyColumnID,
                            StringComparison.Ordinal))
                        .ToArray();
                    if (descriptorMatches.Length != 1
                        || !table.TryGetColumnIndex(clause.PropertyColumnID, out _))
                        throw new ArgumentException("The selected topology filter source is no longer available.");
                    resolved.Add(new ResolvedTopologyFilterClause(op, clause.Value, null,
                        descriptorMatches[0].ColumnName, true));
                    break;
                }
                default:
                    throw new ArgumentException("The topology filter clause is invalid.");
            }
        }
        return resolved.ToArray();
    }

    private static bool TryReadFilterOperator(string? text, out FilterCondition op)
    {
        if (Enum.TryParse(text, ignoreCase: false, out op) && Enum.IsDefined(op)) return true;
        op = FilterCondition.IsNotBlank;
        return false;
    }

    private static bool MatchesTopologyFilter(ObservableTopologyFeature feature,
        ResolvedTopologyFilterClause[] individual,
        IGrouping<ObservableTagDefinition, ResolvedTopologyFilterClause>[] tagGroups)
    {
        foreach (ResolvedTopologyFilterClause clause in individual)
            if (!MatchesTopologyFilterClause(feature, clause)) return false;

        foreach (IGrouping<ObservableTagDefinition, ResolvedTopologyFilterClause> group in tagGroups)
        {
            bool matched = false;
            foreach (ResolvedTopologyFilterClause clause in group)
            {
                if (!MatchesTopologyFilterClause(feature, clause)) continue;
                matched = true;
                break;
            }
            if (!matched) return false;
        }
        return true;
    }

    private static bool MatchesTopologyFilterClause(ObservableTopologyFeature feature,
        ResolvedTopologyFilterClause clause)
    {
        if (clause.IsNoOp) return true;
        if (clause.TagDefinition is not null)
            return MatchesTopologyTag(feature, clause.TagDefinition, clause.Operator, clause.Value);
        if (!clause.IsProperty || clause.PropertyColumnName is null) return true;

        object? cell = feature.TableRow[clause.PropertyColumnName];
        if (clause.Operator == FilterCondition.IsNotBlank) return cell is not null;
        if (clause.Operator == FilterCondition.IsBlank) return cell is null;
        if (cell is null) return false;
        if (clause.Value is null) return true;
        return clause.Operator switch
        {
            FilterCondition.Equals => CompareTopologyValues(cell, clause.Value, FilterCondition.Equals),
            FilterCondition.NotEquals => CompareTopologyValues(cell, clause.Value, FilterCondition.NotEquals),
            FilterCondition.LessThan => CompareTopologyValues(cell, clause.Value, FilterCondition.LessThan),
            FilterCondition.AtMost => CompareTopologyValues(cell, clause.Value, FilterCondition.AtMost),
            FilterCondition.GreaterThan => CompareTopologyValues(cell, clause.Value, FilterCondition.GreaterThan),
            FilterCondition.AtLeast => CompareTopologyValues(cell, clause.Value, FilterCondition.AtLeast),
            FilterCondition.Contains => cell.ToString()?.Contains(clause.Value, StringComparison.OrdinalIgnoreCase) == true,
            FilterCondition.DoesNotContain => cell.ToString()?.Contains(clause.Value, StringComparison.OrdinalIgnoreCase) != true,
            _ => true
        };
    }

    private static bool MatchesTopologyTag(ObservableTopologyFeature feature,
        ObservableTagDefinition definition, FilterCondition op, string? value)
    {
        if (value is null && op is not (FilterCondition.IsNotBlank or FilterCondition.IsBlank))
            return true;

        if (feature.Tags is null) return op == FilterCondition.IsBlank;
        ObservableTag? tag = feature.Tags.FirstOrDefault(item => item.Definition.Equals(definition));
        if (op == FilterCondition.IsNotBlank) return tag is not null;
        if (op == FilterCondition.IsBlank) return tag is null;
        if (tag is null)
            return op is FilterCondition.NotEquals or FilterCondition.DoesNotContain;
        if (value is null) return true;

        return op switch
        {
            FilterCondition.Equals => CompareTopologyValues(tag.Value, value, FilterCondition.Equals),
            FilterCondition.NotEquals => CompareTopologyValues(tag.Value, value, FilterCondition.NotEquals),
            FilterCondition.LessThan => CompareTopologyValues(tag.Value, value, FilterCondition.LessThan),
            FilterCondition.AtMost => CompareTopologyValues(tag.Value, value, FilterCondition.AtMost),
            FilterCondition.GreaterThan => CompareTopologyValues(tag.Value, value, FilterCondition.GreaterThan),
            FilterCondition.AtLeast => CompareTopologyValues(tag.Value, value, FilterCondition.AtLeast),
            FilterCondition.Contains => tag.Value?.ToString()?.Contains(value ?? "", StringComparison.OrdinalIgnoreCase) == true,
            FilterCondition.DoesNotContain => tag.Value?.ToString()?.Contains(value ?? "", StringComparison.OrdinalIgnoreCase) != true,
            _ => true
        };
    }

    private static bool CompareTopologyValues(object? actual, string filterValue, FilterCondition op)
    {
        if (actual is not IComparable comparable || string.IsNullOrEmpty(filterValue)) return false;
        IComparable? parsed = ParseHelpers.TryParseStringAs(filterValue, actual.GetType()) as IComparable;
        IComparable right;
        if (parsed is not null) right = parsed;
        else
        {
            string? fallbackText = actual.ToString();
            if (fallbackText is null) return false;
            comparable = fallbackText;
            right = filterValue;
        }
        int comparison = comparable.CompareTo(right);
        return op switch
        {
            FilterCondition.Equals => comparison == 0,
            FilterCondition.NotEquals => comparison != 0,
            FilterCondition.LessThan => comparison < 0,
            FilterCondition.AtMost => comparison <= 0,
            FilterCondition.GreaterThan => comparison > 0,
            FilterCondition.AtLeast => comparison >= 0,
            _ => true
        };
    }

    private sealed record ResolvedTopologyFilterClause(FilterCondition Operator, string? Value,
        ObservableTagDefinition? TagDefinition, string? PropertyColumnName, bool IsProperty, bool IsNoOp = false)
    {
        public static ResolvedTopologyFilterClause NoOp { get; } = new(
            FilterCondition.IsNotBlank, null, null, null, false, IsNoOp: true);
    }
}

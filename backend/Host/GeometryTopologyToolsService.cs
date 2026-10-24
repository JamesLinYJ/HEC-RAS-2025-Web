using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Visual.Mapping.Observables;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    public static async Task<GeometryTopologyArcPointsSnapshot> LoadGeometryTopologyArcPointsAsync(
        GeometryTopologyArcPointsRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        GeometryTopologyDraftState state = ReadTopologyDraft(request.DraftState, request.Model.AssetPath, request.Revision);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();

        using var context = new ModelContext(ToModelRequest(request.Model));
        var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, state);
        ObservableConceptualMeshArc feature = FindArc(replay.Session.ObservableTopology, request.ArcFID);
        Arc arc = replay.Session.Topology.Arcs[feature.Fid];
        var rows = new List<GeometryTopologyArcPoint>(arc.InternalPointCount + 2)
        {
            new(0, "start", null, arc.StartNode, replay.Session.Topology.Nodes[arc.StartNode].Point.X,
                replay.Session.Topology.Nodes[arc.StartNode].Point.Y)
        };
        for (int index = 0; index < arc.InternalPointCount; index++)
        {
            Point point = arc.InternalPoints[index];
            rows.Add(new GeometryTopologyArcPoint(rows.Count, "internal", index, null, point.X, point.Y));
        }
        Point end = replay.Session.Topology.Nodes[arc.EndNode].Point;
        rows.Add(new GeometryTopologyArcPoint(rows.Count, "end", null, arc.EndNode, end.X, end.Y));

        string draftIdentity = TopologyDraftIdentity(replay.State);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        return await Task.FromResult(new GeometryTopologyArcPointsSnapshot(request.Revision, draftIdentity,
            request.ArcFID, rows.ToArray()));
    }

    public static async Task<GeometryTopologyRecipeSnapshot> LoadGeometryTopologyRecipeAsync(
        GeometryTopologyRecipeRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        GeometryTopologyDraftState state = ReadTopologyDraft(request.DraftState, request.Model.AssetPath, request.Revision);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        if (request.ControlPointFIDs is null || request.ControlPointFIDs.Any(fid => fid < 0)
            || request.ControlPointFIDs.Distinct().Count() != request.ControlPointFIDs.Length)
            throw new ArgumentException("The selected topology control point is no longer available.");

        using var context = new ModelContext(ToModelRequest(request.Model));
        var replay = ReplayGeometryTopologyDraft(context, request.Model.AssetPath, request.Revision, state);
        ObservableConceptualMeshTopology topology = replay.Session.ObservableTopology;
        ObservableConceptualMeshControlPoint[] selected = request.ControlPointFIDs
            .Select(fid => FindRegion(topology, fid)).ToArray();
        GeometryTopologyControlPointRecipe[] selectedRecipes = selected
            .Select(point => new GeometryTopologyControlPointRecipe(point.Fid, point.Recipe ?? string.Empty)).ToArray();
        string[] selectedPatchTypes = selected.Select(point => point.PatchType.ToString()).ToArray();
        bool hasSelection = selected.Length > 0;
        bool hasMismatch = hasSelection && selected.Skip(1)
            .Any(point => !string.Equals(point.Recipe, selected[0].Recipe, StringComparison.Ordinal));
        string? commonRecipe = hasSelection && !hasMismatch ? selected[0].Recipe ?? string.Empty : null;
        string rawText = request.RecipeText ?? commonRecipe ?? string.Empty;
        bool valid = TopologyRecipe.TryParse(rawText, out TopologyRecipe parsed, global: false);
        string? parseIssue = null;
        if (!valid)
        {
            _ = TopologyRecipe.TryParse(rawText, out string sourceIssue, global: false);
            parseIssue = sourceIssue;
        }
        GeometryTopologyRecipeStep[] steps = valid
            ? parsed.Steps.Select(SnapshotRecipeStep).ToArray()
            : [];

        string draftIdentity = TopologyDraftIdentity(replay.State);
        if (Revision(request.Model.AssetPath) != state.Revision) throw new SourceFileChangedException();
        return await Task.FromResult(new GeometryTopologyRecipeSnapshot(request.Revision, draftIdentity,
            request.ControlPointFIDs, hasSelection, hasMismatch, commonRecipe, rawText, valid, parseIssue,
            steps, RecipeStepSchema(), selectedRecipes, selectedPatchTypes));
    }

    private static string TopologyDraftIdentity(GeometryTopologyDraftState state)
    {
        string encoded = JsonSerializer.Serialize(state, BackendJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded))).ToLowerInvariant();
    }

    private static readonly (string Name, string[] Aliases, Type Type)[] SourceRecipeStepTypes =
    [
        ("SwapEdges", [], typeof(SwapEdgesStep)),
        ("Smooth", [], typeof(SmoothStep)),
        ("ForceDisplacement", [], typeof(ForceDisplacementStep)),
        ("Orthogonalize", [], typeof(OrthogonalizeStep)),
        ("SmoothAngles", [], typeof(SmoothAnglesStep)),
        ("Distort", [], typeof(DistortStep)),
        ("RemoveFaces", ["Merge"], typeof(RemoveFacesStep)),
        ("RemoveNodes", ["MergeFaces"], typeof(RemoveNodesStep)),
        ("OptTopology", [], typeof(OptTopologyStep)),
        ("OptSkew", [], typeof(OptSkewStep)),
        ("Split", [], typeof(SplitStep)),
        ("Default", [], typeof(DefaultStep))
    ];

    private static GeometryTopologyRecipeStepSchema[] RecipeStepSchema() => SourceRecipeStepTypes
        .Select(definition => new GeometryTopologyRecipeStepSchema(definition.Name, definition.Aliases,
            StepParameters(Activator.CreateInstance(definition.Type) as TopologyStep
                ?? throw new InvalidOperationException("A retained topology recipe step has no default instance."))))
        .ToArray();

    private static GeometryTopologyRecipeParameter[] StepParameters(TopologyStep step) =>
        step.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(double) || field.FieldType == typeof(int) || field.FieldType == typeof(bool))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .Select(field => new GeometryTopologyRecipeParameter(field.Name, SourceTypeName(field.FieldType),
                Format(field.GetValue(step))))
            .ToArray();

    private static GeometryTopologyRecipeStep SnapshotRecipeStep(TopologyStep step)
    {
        string typeName = step.GetType().Name;
        string name = typeName.EndsWith("Step", StringComparison.Ordinal) ? typeName[..^4] : typeName;
        var parameters = step.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(field => field.FieldType == typeof(double) || field.FieldType == typeof(int) || field.FieldType == typeof(bool))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(field => field.Name, field => Format(field.GetValue(step)), StringComparer.Ordinal);
        return new GeometryTopologyRecipeStep(name, parameters);
    }
}

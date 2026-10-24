using System.Security.Cryptography;
using System.Text;
using Geospatial.Vectors.Meshes;
using Ras.Hydraulics;
using Ras.Observables;
using Utility.Logging;

namespace Ras.Backend;

public static partial class ModelEditorAdapter
{
    internal static async Task<GeometryMeshElementPropertyChartResult> LoadGeometryMeshElementPropertyChartDuplexAsync(
        GeometryMeshElementPropertyChartRequest request,
        Action<BackendOperationProgress>? report,
        BackendDuplexControlReader duplexControls)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(duplexControls);
        ValidateGeometryMeshElementChartRequest(request);
        if (!Guid.TryParse(request.OperationID, out _))
            throw new ArgumentException("The Geometry property chart operation identity is invalid.", nameof(request.OperationID));
        VerifyGeometryMeshModel(request.Model);

        string assetPath = SourceFilePaths.Canonical(request.Model.AssetPath);
        string revision = Revision(assetPath);
        if (!string.Equals(revision, request.Revision, StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();

        using var context = new ModelContext(ToModelRequest(request.Model));
        ApplyGeometryTopologyDraft(context, assetPath, revision, request.TopologyDraftState);
        GeometryMeshDraftState state = ReadGeometryMeshDraft(request.DraftState, assetPath, revision);
        EnsureTopologyFingerprint(state, GeometryMeshTopologyFingerprint(context));
        int flowAreaIndex = -1;
        Observable2DFlowArea? observableFlowArea = null;
        var (geometry, _) = GeometryMeshTopologyFor(context);

        // The chart's Flow Area identity is captured from the retained mesh
        // draft. In particular, Generate replaces the source Flow Area layer
        // with a draft-owned row, so resolve its row ID only after replaying
        // that exact draft into this isolated ModelContext.
        bool hasComputedChildIntent = state.Mutations.Any(mutation => mutation.Operation == "computePropertyTables");
        try
        {
            _ = await ApplyGeometryMeshMutationsAsync(context, assetPath, revision, state,
                replayPropertyTableComputations: hasComputedChildIntent,
                report: report,
                duplexControls: duplexControls).ConfigureAwait(false);
        }
        catch (PropertyTableReplayCancelledException)
        {
            EnsureCurrentRequest();
            return Result("cancelled", "Property-table calculation was cancelled. The Geometry draft and source HDF were not changed.", []);
        }

        EnsureCurrentRequest();
        for (int index = 0; index < geometry.FlowAreaLayer.Features.Count; index++)
        {
            if (!string.Equals(FlowAreaID(index), request.FlowAreaID, StringComparison.Ordinal)) continue;
            flowAreaIndex = index;
            observableFlowArea = geometry.FlowAreaLayer.Features[index];
            break;
        }
        if (flowAreaIndex < 0 || observableFlowArea is null ||
            !string.Equals(observableFlowArea.Name, request.ExpectedFlowAreaName, StringComparison.Ordinal))
            throw new SourceFileChangedException();

        EnsureCurrentRequest();
        FlowArea flowArea = observableFlowArea.FlowArea;
        int meshHash = flowArea.GetMeshDataHash(isResult: false);
        MeshPropertyTables? tables = flowArea.PropertyTables;
        OperationResult currentTables = geometry.FlowAreaLayer.PropertyTablesAreCurrent();
        if (tables is null || !currentTables.Result)
        {
            return Result("requiresPropertyTables",
                hasComputedChildIntent
                    ? "The retained Geometry child computation did not produce current property tables. Run Compute Property Tables again from this Geometry draft, then find this element again."
                    : "Compute property tables in this Geometry draft to plot the source curve. The calculation stays in the draft and does not require saving the parent Geometry.",
                []);
        }

        if (request.ElementIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(request.ElementIndex));

        float[] xValues;
        float[] yValues;
        string xQuantity;
        string xUnits;
        string yQuantity;
        string yUnits;
        int elementCount;
        if (request.ElementKind == "Cell")
        {
            elementCount = flowArea.Mesh.CellCount;
            if (request.ElementIndex >= elementCount)
                throw new ArgumentOutOfRangeException(nameof(request.ElementIndex),
                    "The selected cell index is outside the exact Flow Area mesh.");
            (xValues, yValues) = Slice(tables.CellTableStart, tables.CellTableCount,
                tables.CellVolumeTable, tables.CellElevationTable, request.ElementIndex,
                "cell volume/elevation");
            xQuantity = "Volume";
            yQuantity = "Elevation";
            (xUnits, yUnits) = GeometryUnits(context.Project.Units, isCell: true);
        }
        else
        {
            elementCount = flowArea.Mesh.FaceCount;
            if (request.ElementIndex >= elementCount)
                throw new ArgumentOutOfRangeException(nameof(request.ElementIndex),
                    "The selected face index is outside the exact Flow Area mesh.");
            (xValues, yValues) = Slice(tables.FaceTableStart, tables.FaceTableCount,
                tables.FaceAreaTable, tables.FaceElevationTable, request.ElementIndex,
                "face area/elevation");
            xQuantity = "Area";
            yQuantity = "Elevation";
            (xUnits, yUnits) = GeometryUnits(context.Project.Units, isCell: false);
        }

        EnsureCurrentRequest();

        GeometryMeshElementPropertyChartPoint[] points = xValues.Select((x, index) =>
            new GeometryMeshElementPropertyChartPoint(x, yValues[index])).ToArray();
        return Result("ready", null, points) with
        {
            MeshHash = meshHash,
            XQuantity = xQuantity,
            XUnits = xUnits,
            YQuantity = yQuantity,
            YUnits = yUnits
        };

        GeometryMeshElementPropertyChartResult Result(string stateName, string? message,
            GeometryMeshElementPropertyChartPoint[] points) => new(
                request.OperationID,
                stateName,
                message,
                request.ProjectSessionID,
                request.AssetID,
                assetPath,
                revision,
                request.MeshDraftSHA256.ToUpperInvariant(),
                request.TopologyDraftSHA256.ToUpperInvariant(),
                request.FlowAreaID,
                request.ExpectedFlowAreaName,
                flowAreaIndex,
                observableFlowArea?.FlowArea.GetMeshDataHash(isResult: false) ?? 0,
                request.ElementKind,
                request.ElementIndex,
                request.ElementKind == "Cell" ? "Volume" : "Area",
                request.ElementKind == "Cell"
                    ? GeometryUnits(context.Project.Units, isCell: true).X
                    : GeometryUnits(context.Project.Units, isCell: false).X,
                "Elevation",
                GeometryUnits(context.Project.Units, isCell: request.ElementKind == "Cell").Y,
                points);

        void EnsureCurrentRequest()
        {
            if (Revision(assetPath) != revision ||
                !string.Equals(MeshElementChartDraftHash(request.DraftState), request.MeshDraftSHA256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(MeshElementChartDraftHash(request.TopologyDraftState ?? string.Empty), request.TopologyDraftSHA256,
                    StringComparison.OrdinalIgnoreCase))
                throw new SourceFileChangedException();
        }
    }

    private static void ValidateGeometryMeshElementChartRequest(
        GeometryMeshElementPropertyChartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectSessionID) ||
            string.IsNullOrWhiteSpace(request.OperationID) ||
            string.IsNullOrWhiteSpace(request.AssetID) || request.Model is null ||
            string.IsNullOrWhiteSpace(request.Revision) ||
            string.IsNullOrWhiteSpace(request.DraftState) ||
            string.IsNullOrWhiteSpace(request.MeshDraftSHA256) ||
            string.IsNullOrWhiteSpace(request.TopologyDraftSHA256) ||
            string.IsNullOrWhiteSpace(request.FlowAreaID) ||
            string.IsNullOrWhiteSpace(request.ExpectedFlowAreaName) ||
            request.ElementKind is not ("Cell" or "Face") || request.ElementIndex < 0)
            throw new ArgumentException("A complete exact Geometry mesh element chart owner is required.");
        if (!string.Equals(MeshElementChartDraftHash(request.DraftState), request.MeshDraftSHA256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(MeshElementChartDraftHash(request.TopologyDraftState ?? string.Empty), request.TopologyDraftSHA256,
                StringComparison.OrdinalIgnoreCase))
            throw new SourceFileChangedException();
    }

    private static (float[] X, float[] Y) Slice(int[]? starts, short[]? counts,
        float[]? xValues, float[]? yValues, int elementIndex, string tableName)
    {
        if (starts is null || counts is null || xValues is null || yValues is null ||
            elementIndex >= starts.Length || elementIndex >= counts.Length)
            throw new InvalidDataException("The current " + tableName + " property table is incomplete.");
        int start = starts[elementIndex];
        int count = counts[elementIndex];
        if (start < 0 || count < 2 || start > xValues.Length - count || start > yValues.Length - count)
            throw new InvalidDataException("The current " + tableName + " property table has an invalid range.");
        float[] x = xValues.AsSpan(start, count).ToArray();
        float[] y = yValues.AsSpan(start, count).ToArray();
        if (x.Any(value => !float.IsFinite(value)) || y.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("The current " + tableName + " property table contains non-finite values.");
        return (x, y);
    }

    private static (string X, string Y) GeometryUnits(Utility.Units.UnitSystem unitSystem,
        bool isCell) => unitSystem switch
    {
        Utility.Units.UnitSystem.USCustomary => (isCell ? "ft³" : "ft²", "ft"),
        Utility.Units.UnitSystem.SI => (isCell ? "m³" : "m²", "m"),
        _ => (string.Empty, string.Empty)
    };

    private static string MeshElementChartDraftHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

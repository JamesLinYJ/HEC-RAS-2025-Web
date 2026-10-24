using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geospatial.Features;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Vectors;
using Ras.Layers;
using Utility.Logging;
using Utility.Memory;
using Utility.Progress;

namespace Ras.Backend;

/// <summary>
/// Reads original polyline/polygon Shapefiles into the owning geometry's
/// unsaved topology draft. The normal parent model save remains the only write.
/// </summary>
public static partial class ModelEditorAdapter
{
    public static Task<GeometryVectorImportInspection> InspectGeometryVectorImportAsync(
        GeometryVectorImportInspectRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        GeometryTopologyDraftState state = ValidateVectorImportCapture(request.Model, request.Revision,
            request.DraftState, request.DraftIdentity);
        if (request.Paths is null || request.Paths.Length == 0)
            throw new ArgumentException("Choose one or more polyline or polygon Shapefiles.");
        if (request.Paths.Any(string.IsNullOrWhiteSpace) ||
            request.Paths.Select(path => Path.GetFullPath(path)).Distinct(StringComparer.Ordinal).Count() != request.Paths.Length)
            throw new ArgumentException("The selected Shapefile list is incomplete or contains duplicate files.");

        using var context = new ModelContext(ToModelRequest(request.Model));
        _ = GeometryTopologyFor(context, requireEditable: true);
        string revision = request.Revision;
        var sources = new List<GeometryVectorImportSource>();
        var issues = new List<GeometryVectorImportIssue>();
        foreach (string selectedPath in request.Paths)
        {
            string path;
            try
            {
                path = MapShapefileImportService.ValidateSourcePath(selectedPath);
                MapSourceFamilyCapture family = MapShapefileImportService.CaptureFamily(path);
                GeometryVectorImportSource source = ReadVectorImportSource(path, context.Project.Projection, family);
                MapSourceIdentity.VerifyFamily(family);
                sources.Add(source);
            }
            catch (SourceFileChangedException) { throw; }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
            {
                issues.Add(new(selectedPath, error.Message));
            }
        }

        VerifyVectorImportCapture(request.Model, revision, request.DraftState, request.DraftIdentity, state);
        return Task.FromResult(new GeometryVectorImportInspection(revision, request.DraftIdentity,
            sources.ToArray(), issues.ToArray()));
    }

    public static async Task<GeometryTopologySnapshot> ApplyGeometryVectorImportAsync(
        GeometryVectorImportApplyRequest request)
    {
        VerifyGeometryTopologyModel(request.Model);
        GeometryTopologyDraftState state = ValidateVectorImportCapture(request.Model, request.Revision,
            request.DraftState, request.DraftIdentity);
        if (request.Mode is not ("Merge" or "NoMerge"))
            throw new ArgumentException("Choose one of the supported source import modes.");
        if (request.Sources is null || request.Sources.Length == 0)
            throw new ArgumentException("There are no selected Shapefiles to import.");
        if (request.Sources.Any(source => source is null || source.Family is null || string.IsNullOrWhiteSpace(source.Path)) ||
            request.Sources.Select(source => Path.GetFullPath(source.Path)).Distinct(StringComparer.Ordinal).Count() != request.Sources.Length)
            throw new ArgumentException("The selected Shapefile list is incomplete or contains duplicate files.");

        using var context = new ModelContext(ToModelRequest(request.Model));
        _ = GeometryTopologyFor(context, requireEditable: true);
        var features = new List<GeometryTopologyImportFeature>();
        var sources = new List<GeometryTopologyImportSource>(request.Sources.Length);
        foreach (GeometryVectorImportSelectedSource selected in request.Sources)
        {
            string path = MapShapefileImportService.ValidateSourcePath(selected.Path);
            MapShapefileImportService.VerifyExpectedFamily(selected.Family, path);
            GeometryVectorImportSource reread = ReadVectorImportSource(path, context.Project.Projection, selected.Family);
            MapSourceIdentity.VerifyFamily(selected.Family);
            if (reread.Features.Length == 0)
                throw new InvalidDataException($"'{Path.GetFileName(path)}' contains no line or polygon features to import.");
            sources.Add(new(path, selected.Family));
            features.AddRange(reread.Features.Select(feature =>
                new GeometryTopologyImportFeature(feature.GeometryType, feature.Points)));
        }
        if (features.Count == 0)
            throw new InvalidDataException("The selected Shapefiles contain no line or polygon features to import.");

        VerifyVectorImportCapture(request.Model, request.Revision, request.DraftState,
            request.DraftIdentity, state);
        var mutation = new GeometryTopologyMutation("importLines", "geometry", [])
        {
            ImportPayload = new GeometryTopologyImportPayload(request.Mode, request.MergeContiguousArcs,
                request.RemoveExternalArcs, sources.ToArray(), features.ToArray())
        };
        var edit = new GeometryTopologyEditRequest(request.Model, request.Revision,
            request.DraftState, mutation);
        GeometryTopologySnapshot result = await EditGeometryTopologyAsync(edit).ConfigureAwait(false);
        return result;
    }

    private static GeometryTopologyDraftState ValidateVectorImportCapture(GeometryTopologyModelRequest model,
        string revision, string draftState, string draftIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftState);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftIdentity);
        GeometryTopologyDraftState state = ReadTopologyDraft(draftState, model.AssetPath, revision);
        VerifyVectorImportCapture(model, revision, draftState, draftIdentity, state);
        return state;
    }

    private static void VerifyVectorImportCapture(GeometryTopologyModelRequest model, string revision,
        string draftState, string draftIdentity, GeometryTopologyDraftState state)
    {
        if (!string.Equals(state.AssetPath, SourceFilePaths.Canonical(model.AssetPath), StringComparison.Ordinal) ||
            !string.Equals(state.Revision, revision, StringComparison.Ordinal) ||
            !string.Equals(DraftIdentity(draftState), draftIdentity, StringComparison.Ordinal) ||
            Revision(model.AssetPath) != revision)
            throw new SourceFileChangedException();
    }

    private static string DraftIdentity(string draftState) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(draftState))).ToLowerInvariant();

    private static GeometryVectorImportSource ReadVectorImportSource(string path, Projection? targetProjection,
        MapSourceFamilyCapture family)
    {
        OperationResult headerResult = ShapefileIO.TryParseShapefileHeader(path, out ShapefileHeader header);
        RequireVectorReadSuccess(headerResult, "Shapefile header read");
        if (header is null) throw new InvalidDataException("The retained reader returned no Shapefile header.");
        GeometryVectorImportFeature[] features;
        string shapeType;
        if (ShapefileIO.IsPolylineShapefile(path))
        {
            OperationResult read = ShapefileIO.TryRead(path, out PolylineCollectionFeatureCollection collection,
                targetProjection, ProgressReporter.None());
            RequireVectorReadSuccess(read, "Polyline Shapefile read");
            shapeType = "Polyline";
            features = collection.ToExplodedPolylineCollection().Select((line, ordinal) =>
                ToImportFeature(line, "polyline", ordinal)).ToArray();
        }
        else if (ShapefileIO.IsPolygonShapefile(path))
        {
            OperationResult read = ShapefileIO.TryRead(path, out PolygonCollectionFeatureCollection collection,
                targetProjection, ProgressReporter.None());
            RequireVectorReadSuccess(read, "Polygon Shapefile read");
            shapeType = "Polygon";
            features = collection.ToExplodedPolygonCollection().Select((polygon, ordinal) =>
                ToImportFeature(polygon, "polygon", ordinal)).ToArray();
        }
        else
        {
            throw new NotSupportedException("Only polyline and polygon Shapefiles can be imported into a geometry topology.");
        }

        MapSourceIdentity.VerifyFamily(family);
        return new(family.SourcePath, Path.GetFileNameWithoutExtension(path), shapeType, header.ShpTypeCode,
            features.Length, family, features);
    }

    private static GeometryVectorImportFeature ToImportFeature(Polyline line, string geometryType, int ordinal)
    {
        GeometryTopologyCoordinate[] points = line.Points.Select(point =>
            new GeometryTopologyCoordinate(point.X, point.Y)).ToArray();
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidDataException($"Shapefile feature {ordinal} contains a non-finite coordinate.");
        return new(ordinal, geometryType, points);
    }

    private static void RequireVectorReadSuccess(OperationResult result, string operation)
    {
        if (result.Result) return;
        string detail = result.GetConcatenatedMessages();
        throw new InvalidDataException(string.IsNullOrWhiteSpace(detail)
            ? $"The retained reader could not finish the {operation}." : detail);
    }
}

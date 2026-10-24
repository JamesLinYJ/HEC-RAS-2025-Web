using System.Security.Cryptography;
using Geospatial.Rendering;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.Classifiers;

namespace Ras.Backend;

public sealed record NValuePreviewRequest(string ProjectPath, string AssetPath,
    string ProjectRevision, string Revision);

public sealed record NValuePreviewData(string Revision, string ProjectRevision,
    string Mode, SpatialGridPreview? Raster, NValuePreviewLegendStop[] LegendStops,
    NValuePreviewHistogram? Histogram);
public sealed record NValuePreviewLegendStop(double Value, byte Red, byte Green, byte Blue, byte Alpha);
public sealed record NValuePreviewHistogram(double Minimum, double Maximum, double BinWidth, double[] BinArea);

/// <summary>
/// Read-only catalog preview of one project-owned N-value layer. Raster samples
/// come from the retained NValueLayer resampler and use the source visual
/// context's Manning's-N palette and value ramp. A constant layer has no finite
/// source extent, so it intentionally returns no raster.
/// </summary>
public static class NValuePreviewService
{
    private const int PreviewWidth = 256;
    private const int PreviewHeight = 256;

    public static NValuePreviewData Load(NValuePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.ProjectPath) || !File.Exists(request.ProjectPath))
            throw new ArgumentException("Open a saved project before previewing an N-value layer.");
        if (!Path.IsPathFullyQualified(request.AssetPath) || !File.Exists(request.AssetPath))
            throw new FileNotFoundException("The selected N-value source file was not found.");
        if (string.IsNullOrWhiteSpace(request.ProjectRevision) || string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("Refresh the N-value source summary before rendering its preview.");

        string projectPath = Path.GetFullPath(request.ProjectPath);
        string assetPath = Path.GetFullPath(request.AssetPath);
        VerifyRevision(projectPath, request.ProjectRevision);
        VerifyRevision(assetPath, request.Revision);
        if (!NValueLayer.IsValid(assetPath))
            throw new ArgumentException("The selected HDF file is not an N-value layer.");

        Project project = new(projectPath);
        try
        {
            NValueLayer[] matches = project.NValues.Where(layer =>
                !string.IsNullOrWhiteSpace(layer.Filename)
                && SourceFilePaths.SameSourceFile(layer.Filename, assetPath)).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException("The selected N-value layer is not exactly one source in the current project.");

            NValueLayer layer = matches[0];
            string? classificationPath = null;
            string? classificationRevision = null;
            Dictionary<string, string> classificationRasterRevisions = new(StringComparer.Ordinal);
            if (layer.Mode == NValueSource.ClassificationLayer)
            {
                ClassificationLayer? associated = layer.AssociatedClassificationLayer;
                if (associated is null || string.IsNullOrWhiteSpace(associated.Filename))
                    throw new IOException("The selected N-value layer's classification source is unavailable.");
                ClassificationLayer[] classificationMatches = project.ClassificationLayers.Where(candidate =>
                    SourceFilePaths.SameSourceFile(candidate.Filename, associated.Filename)).ToArray();
                if (classificationMatches.Length != 1 || !ReferenceEquals(classificationMatches[0], associated))
                    throw new IOException("The selected N-value layer's classification source is not uniquely project-owned.");
                classificationPath = SourceFilePaths.Canonical(associated.Filename);
                classificationRevision = Revision(classificationPath);
                if (!ClassificationLayer.IsValid(classificationPath))
                    throw new IOException("The selected N-value layer's classification source is unavailable.");
                VerifyRevision(classificationPath, classificationRevision);
                if (associated.HasLoadErrors)
                    throw new IOException(string.Join("\n", associated.LayerErrors.Select(error => error.EvaluatedMessage)));
                foreach (RasterClassificationInput input in associated.AllRasterInputs())
                {
                    if (!input.IsValid(out _) || !File.Exists(input.Filename))
                        throw new IOException("The selected N-value layer's classification raster source is unavailable.");
                    string rasterPath = SourceFilePaths.Canonical(input.Filename);
                    classificationRasterRevisions.TryAdd(rasterPath, Revision(rasterPath));
                }
            }
            if (layer.HasLoadErrors)
                throw new IOException(string.Join("\n", layer.LayerErrors.Select(error => error.EvaluatedMessage)));

            if (layer.Mode is not (NValueSource.Constant or NValueSource.ClassificationLayer))
                throw new NotSupportedException("The selected N-value source mode has no retained raster resampler.");

            SpatialGridPreview? raster = null;
            NValuePreviewHistogram? histogram = null;
            NValuePreviewLegendStop[] legendStops = [];
            if (layer.Mode == NValueSource.ClassificationLayer)
            {
                InterpolatedSurfaceFill sourceFill = SurfaceFills.ManningsNValues;
                histogram = Histogram(layer);
                raster = Render(layer, sourceFill);
                legendStops = Legend(sourceFill);
            }

            VerifyRevision(assetPath, request.Revision);
            if (classificationPath is not null && classificationRevision is not null)
                VerifyRevision(classificationPath, classificationRevision);
            foreach ((string rasterPath, string rasterRevision) in classificationRasterRevisions)
                VerifyRevision(rasterPath, rasterRevision);
            VerifyRevision(projectPath, request.ProjectRevision);

            return new(request.Revision, request.ProjectRevision, layer.Mode.ToString(), raster,
                legendStops, histogram);
        }
        finally
        {
            DisposeProject(project);
        }
    }

    private static NValuePreviewLegendStop[] Legend(InterpolatedSurfaceFill sourceFill)
    {
        int count = Math.Min(sourceFill.ColorValues.Length, sourceFill.Colors.Count);
        return Enumerable.Range(0, count).Select(index =>
        {
            var color = sourceFill.Colors[index];
            return new NValuePreviewLegendStop(sourceFill.ColorValues[index], color.R, color.G, color.B, color.A);
        }).ToArray();
    }

    private static NValuePreviewHistogram? Histogram(NValueLayer layer)
    {
        var extent = layer.ValidExtent;
        if (!IsFiniteExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY)) return null;
        var histogram = InterpolatedSurfaceFill.EstimateHistogramFrom(layer);
        if (histogram.IsNull()) return null;
        return new(histogram.Minimum, histogram.Maximum, histogram.BinWidth, histogram.BinArea.ToArray());
    }

    private static SpatialGridPreview? Render(NValueLayer layer, InterpolatedSurfaceFill sourceFill)
    {
        var extent = layer.ValidExtent;
        if (!IsFiniteExtent(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY))
            return null;

        Point[] points = new Point[PreviewWidth * PreviewHeight];
        float[] values = new float[points.Length];
        for (int y = 0; y < PreviewHeight; y++)
        for (int x = 0; x < PreviewWidth; x++)
        {
            points[y * PreviewWidth + x] = new Point(
                extent.MinX + (x + 0.5) * (extent.MaxX - extent.MinX) / PreviewWidth,
                extent.MaxY - (y + 0.5) * (extent.MaxY - extent.MinY) / PreviewHeight);
        }
        layer.SamplePoints(points, values);

        float noData = layer.NoDataValue ?? -9999f;
        float[] valid = values.Where(value => float.IsFinite(value) && value != noData).ToArray();
        double? minimum = valid.Length == 0 ? null : valid.Min();
        double? maximum = valid.Length == 0 ? null : valid.Max();
        byte[] rgba = new byte[values.Length * 4];
        for (int index = 0; index < values.Length; index++)
        {
            float value = values[index];
            if (!float.IsFinite(value) || value == noData) continue;
            uint color = unchecked((uint)sourceFill.ColorARGB(value));
            int offset = index * 4;
            rgba[offset] = (byte)(color >> 16);
            rgba[offset + 1] = (byte)(color >> 8);
            rgba[offset + 2] = (byte)color;
            rgba[offset + 3] = (byte)(color >> 24);
        }
        return new(PreviewWidth, PreviewHeight, rgba, extent.MinX, extent.MinY,
            extent.MaxX, extent.MaxY, minimum, maximum);
    }

    private static bool IsFiniteExtent(double minX, double minY, double maxX, double maxY)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY)
            || maxX <= minX || maxY <= minY) return false;
        double spanX = maxX - minX;
        double spanY = maxY - minY;
        return double.IsFinite(spanX) && double.IsFinite(spanY)
            && double.IsFinite(spanX * spanY) && spanX * spanY > 0;
    }

    private static string Revision(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void VerifyRevision(string path, string expected)
    {
        if (!string.Equals(Revision(path), expected, StringComparison.Ordinal))
            throw new SourceFileChangedException();
    }

    private static void DisposeProject(Project project)
    {
        foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>()
            .Concat(project.Geometries).Concat(project.Terrains).Concat(project.BoundaryConditions)
            .Concat(project.NValues).Concat(project.ClassificationLayers))
            layer.Dispose();
        foreach (var result in project.Results) result.Dispose();
    }
}

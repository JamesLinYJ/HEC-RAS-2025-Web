using System;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.IO;

public static class RasterSkeletonizerExtensions
{
	public static PolylineCollection Skeletonize(this string filename, float threshold = 0.1f, int minLengthPixels = 8, double simplifyTolerance = 0.5, double cullLengthWorld = 0.0, ProgressReporter progress = null)
	{
		if (progress == null)
		{
			progress = ProgressReporter.None();
		}
		IRaster<float> raster = RasterReader.TryGetBestRasterizer<float>(filename, out var _, out var cleanup);
		if (raster == null)
		{
			throw new InvalidOperationException("Failed to open raster: " + filename);
		}
		progress.ReportMessage($"Opened {filename} — {raster.RasterDefinition.Rows}x{raster.RasterDefinition.Cols} @ {raster.RasterDefinition.CellSize}");
		try
		{
			return RasterSkeletonizer.Skeletonize(raster, threshold, minLengthPixels, simplifyTolerance, cullLengthWorld, progress);
		}
		finally
		{
			cleanup?.Dispose();
		}
	}
}

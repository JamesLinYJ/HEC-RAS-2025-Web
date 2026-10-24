using System;
using Geospatial.Rasters;

namespace Geospatial.IO;

public static class IResampleExportExtensions
{
	public static void SaveAs<T>(this IResample<T> res, string filename, RasterDefinition rdef) where T : unmanaged, IEquatable<T>
	{
		TiffExportEngine.ExportWithOverviews(filename, res, rdef);
	}
}

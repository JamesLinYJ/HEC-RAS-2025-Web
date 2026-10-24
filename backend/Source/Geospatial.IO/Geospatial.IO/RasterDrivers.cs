using System;
using System.Collections.Generic;

namespace Geospatial.IO;

public static class RasterDrivers
{
	public const string GdalTiff = "GDAL/GTIFF";

	public const string GdalWMS = "GDAL/WMS";

	public const string NativeTiff = "Native/GTIFF";

	public static RasterDriver GDALDriver = new RasterDriver();

	public static bool TryFindBestDriver(string filename, List<RasterDriver> capableDrivers)
	{
		throw new NotImplementedException();
	}
}

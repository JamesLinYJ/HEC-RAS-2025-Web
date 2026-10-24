using System;
using System.Collections.Generic;
using System.Linq;
using OSGeo.GDAL;

namespace Geospatial.GDALAssist;

public static class Drivers
{
	public const string VRT = "VRT";

	public const string TIFF = "GTiff";

	public const string GRIB = "GRIB";

	public const string NetCDF = "NetCDF";

	public static readonly IReadOnlySet<string> ParallelReadSafe = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GTiff" };

	public static bool IsParallelReadSafe(string driverShortName)
	{
		return ParallelReadSafe.Contains(driverShortName);
	}

	public static List<Driver> AllDrivers()
	{
		return (from i in Enumerable.Range(0, Gdal.GetDriverCount())
			select Gdal.GetDriver(i)).ToList();
	}
}

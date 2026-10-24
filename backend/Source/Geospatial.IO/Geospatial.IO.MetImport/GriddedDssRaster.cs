using System;
using System.Collections.Generic;

namespace Geospatial.IO.MetImport;

public class GriddedDssRaster : IGriddedDataSource
{
	public DssTimeSeriesRasterDataSource DssRaster { get; private set; }

	public IReadOnlyList<DateTime> BandTimesteps => DssRaster.Times;

	public IReadOnlyList<DateTime> BandIssueTimes => DssRaster.Times;

	public string Name => DssRaster.RootPathname;

	public string Filepath => DssRaster.Filename;

	public GriddedDssRaster(DssTimeSeriesRasterDataSource dssRaster)
	{
		DssRaster = dssRaster;
	}
}

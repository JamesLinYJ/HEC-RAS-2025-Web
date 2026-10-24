using System;
using System.Collections.Generic;
using System.IO;
using Geospatial.GDALAssist;
using Utility.Units;

namespace Geospatial.IO.MetImport;

public class GriddedGdalRaster : IGriddedDataSource
{
	public GdalRasterSummary Summary { get; private set; }

	public IReadOnlyList<DateTime> BandTimesteps => Summary.BandTimestamps;

	public IReadOnlyList<DateTime> BandIssueTimes => Summary.ForecastIssueTimes;

	public string Name => Path.GetFileName(Summary.Path);

	public string Filepath => Summary.Path;

	public GriddedSourceUnits Units
	{
		get
		{
			Precipitation precipitation = Precipitation.TryParse(Summary.Units);
			return new GriddedSourceUnits
			{
				RawUnits = Summary.Units,
				RawDataType = Summary.DataType,
				DefaultPrecipDataType = precipitation?.DataTypeName,
				DefaultLength = precipitation?.VerticalUnits?.Name
			};
		}
	}

	public GriddedGdalRaster(GdalRasterSummary summary)
	{
		Summary = summary;
	}
}

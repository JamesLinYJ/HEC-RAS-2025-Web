using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.GDALAssist;
using Utility.Logging;

namespace Geospatial.IO.MetImport;

public class GriddedGdalRasterImportController : IGriddedDataImportController
{
	public Type ItemType => typeof(GriddedGdalRaster);

	public bool CanCombineSources => true;

	public IReadOnlyList<string> GetSubdatasetsOfFiles(IList<string> paths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (string path in paths)
		{
			using GDALRaster gDALRaster = GDALRaster.TryOpen(path);
			if (gDALRaster == null)
			{
				continue;
			}
			foreach (string key in gDALRaster.GetSubDatasets().Keys)
			{
				int lastIndexOf = key.LastIndexOf(':');
				stringSet.Add(key.Substring(lastIndexOf + 1));
			}
		}
		return stringSet.ToArray();
	}

	public IReadOnlyList<IGriddedDataSource> CreateDataSourcesFromFiles(IList<string> paths, string subset)
	{
		GDALRaster gDALRaster = GDALRaster.TryOpen(paths[0]);
		string driverShortName = gDALRaster.DriverShortName;
		gDALRaster.Dispose();
		IList<string> startingFiles;
		if (string.IsNullOrEmpty(subset))
		{
			startingFiles = paths;
		}
		else
		{
			List<string> startingFilesList = new List<string>();
			foreach (string path in paths)
			{
				startingFilesList.Add($"{driverShortName}:\"{path}\":{subset}");
			}
			startingFiles = startingFilesList;
		}
		List<IGriddedDataSource> iGriddedDataSources = new List<IGriddedDataSource>();
		using GdalRasterCollection gdrc = GdalRasterCollection.Open(startingFiles);
		foreach (GdalRasterSummary gdalRasterSummary in (from sum in GdalRasterSummary.ParseCollection(gdrc)
			orderby sum.ForecastIssueTimes[0]
			select sum).ToList())
		{
			iGriddedDataSources.Add(new GriddedGdalRaster(gdalRasterSummary));
		}
		return iGriddedDataSources;
	}

	public OperationResult CreateSelection(IReadOnlyList<(IGriddedDataSource, int)> selectedBands, out IGriddedDataSelection selection)
	{
		OperationResult result = GriddedGdalRasterSelection.CreateFromSelections(selectedBands.Select(((IGriddedDataSource, int) tuple) => (Summary: (tuple.Item1 as GriddedGdalRaster).Summary, tuple.Item2)).ToArray(), out var selection2);
		selection = selection2;
		return result;
	}
}

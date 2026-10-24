using System;
using System.Collections.Generic;
using System.Linq;
using Hec.Dss;
using Utility.Logging;

namespace Geospatial.IO.MetImport;

public class GriddedDssRasterImportController : IGriddedDataImportController
{
	public Type ItemType => typeof(GriddedDssRaster);

	public bool CanCombineSources => false;

	public IReadOnlyList<string> GetSubdatasetsOfFiles(IList<string> paths)
	{
		HashSet<string> stringSet = new HashSet<string>();
		foreach (string path in paths)
		{
			using DssReader dssReader = new DssReader(path);
			foreach (DssPath catalog in dssReader.GetCatalog())
			{
				if (dssReader.GetRecordType(catalog) == RecordType.Grid)
				{
					string toLowerInvariant = $"{catalog.Apart}/{catalog.Bpart}/{catalog.Cpart}///{catalog.Fpart}/".ToLowerInvariant();
					if (!stringSet.Contains(toLowerInvariant))
					{
						stringSet.Add(toLowerInvariant);
					}
				}
			}
		}
		return stringSet.ToArray();
	}

	public IReadOnlyList<IGriddedDataSource> CreateDataSourcesFromFiles(IList<string> paths, string subset)
	{
		List<IGriddedDataSource> iGriddedDataSources = new List<IGriddedDataSource>();
		foreach (string path in paths)
		{
			DssTimeSeriesRasterDataSource dssRaster = new DssTimeSeriesRasterDataSource(new DssReader(path), subset);
			iGriddedDataSources.Add(new GriddedDssRaster(dssRaster));
		}
		return iGriddedDataSources;
	}

	public OperationResult CreateSelection(IReadOnlyList<(IGriddedDataSource, int)> selectedBands, out IGriddedDataSelection selection)
	{
		if (selectedBands.Count == 0)
		{
			selection = null;
			return OperationResult.Fail("No bands selected.");
		}
		if (((selectedBands[0].Item1 as GriddedDssRaster) ?? throw new ArgumentException("Keyframe must be a GriddedDssRaster")).DssRaster.RasterDefinition.IsNull())
		{
			selection = null;
			return OperationResult.Fail("Could not read given DSS raster succesfully.");
		}
		selection = GriddedDssRasterSelection.Create((selectedBands[0].Item1 as GriddedDssRaster).DssRaster);
		return true;
	}
}

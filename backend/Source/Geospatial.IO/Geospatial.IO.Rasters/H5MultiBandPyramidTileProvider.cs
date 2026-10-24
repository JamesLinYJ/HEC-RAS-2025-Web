using System;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using Utility;
using Utility.Logging;

namespace Geospatial.IO.Rasters;

public class H5MultiBandPyramidTileProvider<T> : IMultiBandPyramidTileProvider<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private IMultiBandTileProvider<T>[] _pyramidLevels;

	public IMultiBandTileProvider<T>[] PyramidLevel => _pyramidLevels;

	public int ProfileCount => _pyramidLevels[0].ProfileCount;

	public Extent ValidExtent => _pyramidLevels[0].ValidExtent;

	public T? NoDataValue => _pyramidLevels[0].NoDataValue;

	public static OperationResult TryOpen(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5MultiBandPyramidTileProvider<T> result)
	{
		if (hwrap == null || hwrap.IsDisposed)
		{
			throw new ArgumentException("hwrap is null or disposed.");
		}
		OperationResult operationResult = H5MultiBandTileProvider<T>.TryOpen(hwrap, datasetName, out var baseLvl);
		if (!operationResult.Result)
		{
			result = null;
			return operationResult;
		}
		H5MultiBandPyramidTileProvider<T> retn = new H5MultiBandPyramidTileProvider<T>();
		string expectedGroup = H5RasterWriter.GetDefaultOverviewGroupName(datasetName);
		hwrap.UseResource((H5Reader hr) =>
		{
			if (hr.ExistsAsGroup(expectedGroup))
			{
				int datasetCount = hr.GetDatasetCount(expectedGroup);
				retn._pyramidLevels = new IMultiBandTileProvider<T>[datasetCount + 1];
				retn._pyramidLevels[0] = baseLvl;
				for (int i = 1; i <= datasetCount; i++)
				{
					string defaultOverviewLevelName = H5RasterWriter.GetDefaultOverviewLevelName(expectedGroup, i);
					if (!H5MultiBandTileProvider<T>.TryOpen(hwrap, defaultOverviewLevelName, out var result2).Result)
					{
						throw new Exception("Pyramid lvl failed to load");
					}
					retn._pyramidLevels[i] = result2;
				}
			}
			else
			{
				retn._pyramidLevels = new IMultiBandTileProvider<T>[1];
				retn._pyramidLevels[0] = baseLvl;
			}
		});
		result = retn;
		return OperationResult.Success();
	}
}

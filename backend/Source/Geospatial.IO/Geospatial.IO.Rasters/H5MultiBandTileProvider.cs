using System;
using Geospatial.Rasters;
using H5Assist;
using Utility;
using Utility.Logging;

namespace Geospatial.IO.Rasters;

public class H5MultiBandTileProvider<T> : H5MultiBandRasterProvider<T>, IMultiBandTileProvider<T>, IMultiBandRasterProvider<T>, IBlockedRasterDefinition, IRasterDefinition, ISurface<T>, ITiledRasterDefinition where T : unmanaged, IEquatable<T>
{
	public int TileSize => BlockSize.Width;

	public RasterDefinition TiledRasterDefinition => RasterDefinition.TiledRaster(TileSize);

	public static OperationResult TryOpen(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5MultiBandTileProvider<T> result)
	{
		if (hwrap == null || hwrap.IsDisposed)
		{
			throw new ArgumentException("hwrap is null or disposed.");
		}
		return TryInitBandedInternal(hwrap, datasetName, out result);
	}

	protected static OperationResult TryInitBandedInternal(NativeResourceWrapper<H5Reader> hwrap, string datasetName, out H5MultiBandTileProvider<T> result)
	{
		result = new H5MultiBandTileProvider<T>();
		OperationResult operationResult = H5MultiBandRasterProvider<T>.TryInitBandedInternal(result, hwrap, datasetName);
		if (!operationResult.Result)
		{
			result = null;
			return operationResult;
		}
		if (!result._isTiled)
		{
			result = null;
			return OperationResult.Fail("Not a tiled data source.");
		}
		if (!result.BlockSize.IsSquare())
		{
			result = null;
			return OperationResult.Fail("Tiling scheme isn't square.");
		}
		return OperationResult.Success();
	}
}

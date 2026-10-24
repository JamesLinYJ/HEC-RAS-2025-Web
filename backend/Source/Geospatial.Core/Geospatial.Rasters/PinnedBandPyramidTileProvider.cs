using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

internal class PinnedBandPyramidTileProvider<T> : IPyramidTileProvider<T>, ISurface<T> where T : unmanaged, IEquatable<T>
{
	private readonly IMultiBandPyramidTileProvider<T> _src;

	public ITileProvider<T>[] PyramidLevel { get; init; }

	public Extent ValidExtent => _src.ValidExtent;

	public T? NoDataValue => _src.NoDataValue;

	public PinnedBandPyramidTileProvider(IMultiBandPyramidTileProvider<T> src, int bandIndex)
	{
		_src = src;
		PyramidLevel = new ITileProvider<T>[src.PyramidLevel.Length];
		for (int i = 0; i < src.PyramidLevel.Length; i++)
		{
			PyramidLevel[i] = src.PyramidLevel[i].AsSingleProfileProvider(bandIndex);
		}
	}
}

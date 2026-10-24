using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class RasterPyramid<T> : IRasterPyramid<T>, IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition, IPyramidLevel where T : unmanaged, IEquatable<T>
{
	private readonly IPyramidTileProvider<T> _datasource;

	private readonly List<IRaster<T>> _rasterizers;

	public RasterDefinition RasterDefinition { get; }

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue { get; set; }

	public int PyramidCount => _datasource.PyramidLevel.Length;

	public float PyramidSelectionRatio { get; set; } = 1f;

	public IPyramidTileProvider<T> TileProvider => _datasource;

	public RasterPyramid(IPyramidTileProvider<T> source)
	{
		if (source == null)
		{
			throw new ArgumentNullException("source");
		}
		if (source.PyramidLevel.Length == 0)
		{
			throw new ArgumentException("source has no pyramid levels.");
		}
		_datasource = source;
		_rasterizers = _datasource.PyramidLevel.Select((ITileProvider<T> pl) => pl.AsRasterizer()).ToList();
		ITileProvider<T> tileProvider = _datasource.PyramidLevel.First();
		RasterDefinition = tileProvider.RasterDefinition;
		NoDataValue = tileProvider.NoDataValue;
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		int bestPyramidLevel = GetBestPyramidLevel(args);
		_rasterizers[bestPyramidLevel].Resample(args, outData);
	}

	public int GetBestPyramidLevel(ResampleArgs args)
	{
		return GetBestPyramidLevel(args.Raster.CellSize, args.SampleParams.ResampleCellSizeRatio * PyramidSelectionRatio);
	}

	private int GetBestPyramidLevel(double targetCellSize, double targetCellSizeHeuristic = 1.0)
	{
		return GetBestPyramidLevel(_rasterizers, targetCellSize, targetCellSizeHeuristic);
	}

	public static int GetBestPyramidLevel<K>(IList<K> levels, double targetCellSize, double targetCellSizeHeuristic = 1.0) where K : IRasterDefinition
	{
		int count = levels.Count;
		for (int i = 0; i < count; i++)
		{
			if (levels[i].RasterDefinition.CellSize >= targetCellSize * targetCellSizeHeuristic)
			{
				if (i == 0)
				{
					return 0;
				}
				return i - 1;
			}
		}
		return count - 1;
	}

	public IRaster<T> GetPyramidLevel(int pLevel)
	{
		return _rasterizers[pLevel];
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		GetPyramidLevel(0).SamplePoints(points, outData, sampleParams);
	}
}

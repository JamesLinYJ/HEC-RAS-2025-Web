using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility;

namespace Geospatial.Rasters;

internal class BandedRasterPyramid<T> : IMultiBandRaster<T>, IMultiBandResampler<T>, ISurface<T>, IRasterDefinition, IPyramidLevel where T : unmanaged, IEquatable<T>
{
	private struct FallbackLowresProcessTilesData
	{
		public ResampleArgs Args;

		public ResampleArgs ArgsClone;

		public Memory<T> OutData;

		public T? NoDataValue;
	}

	private readonly IMultiBandPyramidTileProvider<T> _datasource;

	private readonly List<IMultiBandRaster<T>> _rasterizers;

	public RasterDefinition RasterDefinition => _datasource.BasePyramidLevel.RasterDefinition;

	public Extent ValidExtent => RasterDefinition.Extent;

	public T? NoDataValue => _datasource.BasePyramidLevel.NoDataValue;

	public int PyramidCount => _datasource.PyramidLevel.Length;

	public float PyramidSelectionRatio { get; set; } = 1f;

	public bool FallbackToLowerResOnReadFailure { get; set; }

	public IMultiBandPyramidTileProvider<T> TileProvider => _datasource;

	public int ProfileCount => _datasource.ProfileCount;

	public BandedRasterPyramid(IMultiBandPyramidTileProvider<T> source)
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
		_rasterizers = _datasource.PyramidLevel.Select((IMultiBandTileProvider<T> pl) => pl.AsBandedRasterizer()).ToList();
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		IPyramidTileProvider<T> pyramidTileProvider = _datasource.AsSingleProfile(bandIndex);
		args.EnsureBuffer(outData, NoDataValue);
		ResampleArgs resampleArgs = args.CloneWithoutBufferClear();
		if (args.Reporter.CancelRequested)
		{
			return;
		}
		if (FallbackToLowerResOnReadFailure)
		{
			int bestPyramidLevel = RasterPyramid<T>.GetBestPyramidLevel(pyramidTileProvider.PyramidLevel, args.Raster.CellSize, PyramidSelectionRatio);
			FallbackLowresProcessTilesData model = new FallbackLowresProcessTilesData
			{
				Args = args,
				ArgsClone = resampleArgs,
				OutData = outData,
				NoDataValue = NoDataValue
			};
			RasterOperations.ProcessTiles_FallbackToLowerRes(pyramidTileProvider, bestPyramidLevel, resampleArgs.Raster.Extent, clipTilesToDesiredExtent: true, model, (FallbackLowresProcessTilesData data, long tIdx, RasterDefinition rd, Memory<T> buf) =>
			{
				if (!data.Args.Reporter.CancelRequested)
				{
					RasterOperations.Resample(rd, buf, data.ArgsClone, data.OutData, data.NoDataValue.GetValueOrDefault());
				}
			}, ParallelizationIntent.Parallel);
			return;
		}
		IRasterPyramid<T> rasterPyramid = pyramidTileProvider.AsRasterizer();
		if (PyramidSelectionRatio != 1f)
		{
			IPyramidLevel pyramidLevel = rasterPyramid;
			if (pyramidLevel != null)
			{
				pyramidLevel.PyramidSelectionRatio = PyramidSelectionRatio;
			}
		}
		rasterPyramid.Resample(resampleArgs, outData);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.Resample_NaiiveLoop(args, startBandIndex, bandCount, outData);
	}

	public IMultiBandRaster<T> GetPyramidLevel(int pLevel)
	{
		return _rasterizers[pLevel];
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		this.SamplePoints_NaiiveLoop(points, startBandIndex, bandCount, outData);
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		_datasource.AsSingleProfile(bandIndex).AsRasterizer().SamplePoints(points, outData);
	}

	public void GetRange(PixelRange range, int bandIndex, Memory<T> result)
	{
		GetPyramidLevel(0).GetRange(range, bandIndex, result);
	}
}

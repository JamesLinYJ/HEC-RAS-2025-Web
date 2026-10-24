using System;
using System.Collections.Generic;
using Geospatial.DataStructures;
using Geospatial.Vectors;
using Utility;
using Utility.Extensions;

namespace Geospatial.Rasters;

internal class TiledRaster<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition, ITiledRasterDefinition, IBlockedRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly ITileProvider<T> _tileProvider;

	public Extent ValidExtent => _tileProvider.RasterDefinition.Extent;

	public T? NoDataValue => _tileProvider.NoDataValue;

	public RasterDefinition RasterDefinition => _tileProvider.RasterDefinition;

	public RasterDefinition TiledRasterDefinition => _tileProvider.TiledRasterDefinition;

	public RasterDefinition WorldRasterDefinition => _tileProvider.RasterDefinition;

	public int TileSize => _tileProvider.TileSize;

	public ITileProvider<T> TileProvider => _tileProvider;

	public BlockSize BlockSize => _tileProvider.BlockSize;

	public TiledRaster(ITileProvider<T> tileProvider)
	{
		_tileProvider = tileProvider ?? throw new ArgumentNullException("tileProvider");
	}

	public RasterDefinition GetTileRasterDefinition(Pixel tileIdx)
	{
		return new RasterDefinition(TiledRasterDefinition.CellExtent(tileIdx), WorldRasterDefinition.CellSize);
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		ResampleArgs argsClone = args.CloneWithoutBufferClear();
		if (args.Reporter.CancelRequested)
		{
			return;
		}
		if (args.SampleParams.InterpolationModeRequiresNeighboringCells)
		{
			RasterOperations.ProcessTilesWithNeighbors(_tileProvider, args.Raster.Extent, _tileProvider.RasterDefinition.CellSize, 999, (int tIdx, TileWithNeighbors<T> data) =>
			{
				if (!args.Reporter.CancelRequested)
				{
					RasterOperations.Resample(data, argsClone, outData, NoDataValue.GetValueOrDefault());
				}
			}, preferPinnedReads: true);
			return;
		}
		RasterOperations.ProcessTiles(_tileProvider, argsClone.Raster.Extent, clipTilesToDesiredExtent: true, (long tIdx, RasterDefinition rd, Memory<T> buf) =>
		{
			if (!args.Reporter.CancelRequested)
			{
				RasterOperations.Resample(rd, buf, argsClone, outData, NoDataValue.GetValueOrDefault());
			}
		}, new ParallelizationPlan(ParallelizationIntent.Parallel, 17));
	}

	public void SamplePoints(Memory<Point> points, Memory<T> retn, SamplingParams sampleParams = default(SamplingParams))
	{
		if (sampleParams.IsNull())
		{
			sampleParams = SamplingParams.Default;
		}
		if (NoDataValue.HasValue && sampleParams.InitializeBufferToNoData)
		{
			retn.Span.Fill(NoDataValue.Value);
		}
		Span<Point> pointsSpan = points.Span;
		Span<T> retnSpan = retn.Span;
		Extent ext = Extent.Union(pointsSpan).Extend(WorldRasterDefinition.CellSize);
		PixelRange pixelRange = TiledRasterDefinition.GetPixelRange(ext);
		RasterDefinition subRaster = TiledRasterDefinition.GetSubRaster(pixelRange);
		if (subRaster.IsNull())
		{
			return;
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(subRaster);
		for (int i = 0; i < pointsSpan.Length; i++)
		{
			spatialIndex.AddInBounds(pointsSpan[i], i);
		}
		T[] values = new T[_tileProvider.BlockSize.PixelCount];
		for (int j = pixelRange.Top; j <= pixelRange.Bottom; j++)
		{
			for (int k = pixelRange.Left; k <= pixelRange.Right; k++)
			{
				Pixel pixel = new Pixel(j, k);
				Pixel px = TiledRasterDefinition.TransformIndexingToSubSpace(pixelRange, pixel);
				List<int> bin = spatialIndex.Bin(px);
				if (bin.IsNullOrEmpty())
				{
					continue;
				}
				_tileProvider.ReadBlock(pixel, values);
				Point[] pointsArray = new Point[bin.Count];
				for (int l = 0; l < bin.Count; l++)
				{
					pointsArray[l] = pointsSpan[bin[l]];
				}
				T[] valuesArray = new T[pointsArray.Length];
				RasterOperations.SamplePoints(GetTileRasterDefinition(pixel), values, pointsArray, NoDataValue, valuesArray);
				if (NoDataValue.HasValue)
				{
					T noDataValueTerm = NoDataValue.Value;
					for (int m = 0; m < bin.Count; m++)
					{
						T retnSpan2 = valuesArray[m];
						if (!retnSpan2.Equals(noDataValueTerm))
						{
							retnSpan[bin[m]] = retnSpan2;
						}
					}
				}
				else
				{
					for (int n = 0; n < bin.Count; n++)
					{
						retnSpan[bin[n]] = valuesArray[n];
					}
				}
			}
		}
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		throw new NotImplementedException();
	}
}

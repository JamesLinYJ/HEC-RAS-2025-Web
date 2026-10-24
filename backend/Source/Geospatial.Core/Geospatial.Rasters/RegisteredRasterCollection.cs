using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Utility.Extensions;

namespace Geospatial.Rasters;

public class RegisteredRasterCollection<T> : IRaster<T>, IResample<T>, ISurface<T>, IRasterDefinition where T : unmanaged, IEquatable<T>
{
	private readonly List<IRaster<T>> _rasters;

	private readonly RasterDefinition _rasterDefinition;

	private readonly T? _noDataValue;

	public RasterDefinition RasterDefinition => _rasterDefinition;

	public Extent ValidExtent => _rasterDefinition.Extent;

	public T? NoDataValue => _noDataValue;

	public RegisteredRasterCollection(IEnumerable<IRaster<T>> rasters, T noDataValue, RasterDefinition? rasterDefinition = null)
	{
		int count = rasters.Count();
		if (count == 0)
		{
			throw new ArgumentException("Must provide at least one raster", "rasters");
		}
		_noDataValue = noDataValue;
		_rasters = rasters.ToList();
		for (int i = 0; i < count; i++)
		{
			IRaster<T> resampler = _rasters[i];
			_rasters[i] = resampler.ReplaceNodataRaster(noDataValue);
		}
		double cellSize = _rasters[0].RasterDefinition.CellSize;
		if (rasterDefinition.HasValue)
		{
			_rasterDefinition = rasterDefinition.Value;
			return;
		}
		Extent extent = Extent.Union(_rasters.Select((IRaster<T> r) => r.RasterDefinition.Extent));
		Point upperLeft = _rasters[0].RasterDefinition.Extent.UpperLeft;
		_rasterDefinition = RasterDefinition.ComputeRegisteredRaster(extent, upperLeft, cellSize);
	}

	public void Resample(ResampleArgs args, Memory<T> outData)
	{
		args.EnsureBuffer(outData, NoDataValue);
		if (_rasters.Count == 0 || !ValidExtent.Overlaps(args.Raster.Extent))
		{
			return;
		}
		ResampleArgs args2 = args.CloneWithoutBufferClear();
		for (int i = 0; i < _rasters.Count; i++)
		{
			if (_rasters[i].RasterDefinition.Overlaps(args.Raster.Extent))
			{
				_rasters[i].Resample(args2, outData);
			}
		}
	}

	public void GetRange(PixelRange range, Memory<T> outData)
	{
		if (_noDataValue.HasValue)
		{
			outData.Span.Fill(_noDataValue.Value);
		}
		RasterDefinition subRaster = _rasterDefinition.GetSubRaster(range);
		for (int i = 0; i < _rasters.Count; i++)
		{
			IRaster<T> raster = _rasters[i];
			RasterDefinition rasterDefinition = raster.RasterDefinition;
			if (rasterDefinition.Extent.Overlaps(subRaster.Extent))
			{
				Extent ext = rasterDefinition.Extent.Intersection(subRaster.Extent);
				PixelRange pixelRange = rasterDefinition.GetPixelRange(ext);
				PixelRange pixelRange2 = subRaster.GetPixelRange(ext);
				if (!pixelRange.IsNull() && !pixelRange2.IsNull())
				{
					T[] values = new T[pixelRange.CellCount];
					raster.GetRange(pixelRange, values);
					CopyRangeWithNoDataSkip(values, pixelRange, outData.Span, pixelRange2, subRaster.Cols);
				}
			}
		}
	}

	private void CopyRangeWithNoDataSkip(T[] srcData, PixelRange srcRange, Span<T> dstData, PixelRange dstRange, int dstCols)
	{
		T? noDataValue = NoDataValue;
		int colCount = srcRange.ColCount;
		int count = Math.Min(srcRange.RowCount, dstRange.RowCount);
		int count2 = Math.Min(srcRange.ColCount, dstRange.ColCount);
		for (int i = 0; i < count; i++)
		{
			int colCountTerm = i * colCount;
			int minRowDstRangeTerm = (dstRange.MinRow + i) * dstCols + dstRange.MinCol;
			for (int j = 0; j < count2; j++)
			{
				T srcData2 = srcData[colCountTerm + j];
				if (!noDataValue.HasValue || !srcData2.Equals(noDataValue.Value))
				{
					dstData[minRowDstRangeTerm + j] = srcData2;
				}
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<T> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_ = points.Span;
		Span<T> outDataSpan = outData.Span;
		if (sampleParams.InitializeBufferToNoData && _noDataValue.HasValue)
		{
			outDataSpan.Fill(_noDataValue.Value);
		}
		Extent other = PointCollection.ComputeExtent(points);
		if (!ValidExtent.Overlaps(in other))
		{
			return;
		}
		sampleParams.CloneWithoutBufferClear();
		T[] values = new T[outData.Length];
		for (int i = 0; i < _rasters.Count; i++)
		{
			IRaster<T> raster = _rasters[i];
			if (!raster.ValidExtent.Overlaps(in other))
			{
				continue;
			}
			T? noDataValue = raster.NoDataValue;
			values.Fill(noDataValue.GetValueOrDefault());
			raster.SamplePoints(points, values, sampleParams);
			for (int j = 0; j < values.Length; j++)
			{
				T outDataSpan2 = values[j];
				if (!noDataValue.HasValue || !outDataSpan2.Equals(noDataValue.Value))
				{
					outDataSpan[j] = outDataSpan2;
				}
			}
		}
	}
}

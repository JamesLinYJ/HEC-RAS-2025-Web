using System;
using System.Collections.Generic;
using Geospatial.DataStructures;
using Geospatial.Vectors;
using Geospatial.Vectors.Polylines;
using Utility;
using Utility.Memory;

namespace Geospatial.Rasters;

public class DistanceToPolylinesRaster : IRaster<float>, IResample<float>, ISurface<float>, IRasterDefinition
{
	public const int MaxHaloCells = 23000;

	private readonly RasterDefinition _rasterDefinition;

	private readonly IReadOnlyList<Polyline> _polylines;

	private readonly SpatialIndex<FeatureSegmentSubset> _segmentSpatialIndex;

	private readonly SpatialIndex<int> _polylineSpatialIndex;

	private readonly int _maxDistanceCells;

	private readonly float _maxDistanceUnits;

	private readonly float _noDataValue;

	private readonly DistanceAlgorithm _algorithm;

	private readonly double _extentDiagonalSquared;

	public RasterDefinition RasterDefinition => _rasterDefinition;

	public Extent ValidExtent => _rasterDefinition.Extent;

	public float? NoDataValue => _noDataValue;

	public DistanceToPolylinesRaster(RasterDefinition rasterDefinition, IEnumerable<Polyline> polylines, int maxDistanceCells, DistanceAlgorithm algorithm = DistanceAlgorithm.EDT, float noDataValue = float.NaN)
	{
		if (maxDistanceCells <= 0)
		{
			throw new ArgumentException("maxDistanceCells must be greater than 0.", "maxDistanceCells");
		}
		if (maxDistanceCells > 23000)
		{
			throw new ArgumentException($"maxDistanceCells must be ≤ {23000} to prevent buffer overflow.", "maxDistanceCells");
		}
		_rasterDefinition = rasterDefinition;
		_maxDistanceCells = maxDistanceCells;
		_maxDistanceUnits = (float)((double)maxDistanceCells * rasterDefinition.CellSize);
		_noDataValue = noDataValue;
		_algorithm = algorithm;
		double deltaXY = rasterDefinition.Extent.DeltaXY;
		_extentDiagonalSquared = deltaXY * deltaXY;
		if (polylines is IPolylineCollection polylineCollection)
		{
			_polylines = polylineCollection;
			_segmentSpatialIndex = ((ISpatiallyIndexedSegmentCollection)polylineCollection).GetSegmentSpatialIndex();
			_polylineSpatialIndex = polylineCollection.GetSpatialIndex();
		}
		else
		{
			PolylineCollection polylineCollection2 = (PolylineCollection)(_polylines = new PolylineCollection(polylines));
			_segmentSpatialIndex = polylineCollection2.GetSegmentSpatialIndex();
			_polylineSpatialIndex = polylineCollection2.GetSpatialIndex();
		}
	}

	public void GetRange(PixelRange range, Memory<float> outData)
	{
		RasterDefinition subRaster = _rasterDefinition.GetSubRaster(range);
		Resample(new ResampleArgs(subRaster), outData);
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		RasterDefinition raster = args.Raster;
		Span<float> outDataSpan = outData.Span;
		outDataSpan.Fill(_maxDistanceUnits);
		if (_polylines != null && _polylines.Count != 0 && TileHasNearbyPolylines(raster.Extent))
		{
			switch (_algorithm)
			{
			case DistanceAlgorithm.EDT:
				ResampleEDT(raster, outDataSpan);
				break;
			case DistanceAlgorithm.SpatialIndex:
				ResampleSpatialIndex(raster, outData);
				break;
			}
		}
	}

	private bool TileHasNearbyPolylines(Extent tileExtent)
	{
		if (_polylineSpatialIndex == null)
		{
			return true;
		}
		Extent extent = new Extent(tileExtent.Left - (double)_maxDistanceUnits, tileExtent.Right + (double)_maxDistanceUnits, tileExtent.Bottom - (double)_maxDistanceUnits, tileExtent.Top + (double)_maxDistanceUnits);
		return _polylineSpatialIndex.GetElements(extent).Count > 0;
	}

	private void ResampleSpatialIndex(RasterDefinition raster, Memory<float> outData)
	{
		double searchRadius = _maxDistanceUnits;
		double searchRadiusSquared = searchRadius * searchRadius;
		float maxDist = _maxDistanceUnits;
		Parallel.SmartFor(raster.CellCount, (int start, int end) =>
		{
			Span<float> outDataSpan = outData.Span;
			for (int i = start; i <= end; i++)
			{
				Point pt = raster.CellCenter(i);
				float minDistanceSpatialIndex = ComputeMinDistanceSpatialIndex(pt, searchRadius, searchRadiusSquared);
				outDataSpan[i] = Math.Min(minDistanceSpatialIndex, maxDist);
			}
		});
	}

	private float ComputeMinDistanceSpatialIndex(Point pt, double searchRadius, double searchRadiusSquared)
	{
		double searchRadiusSquared2 = searchRadiusSquared;
		List<FeatureSegmentSubset> elements = _segmentSpatialIndex.GetElements(pt, searchRadius, dedupe: false);
		if (elements.Count == 0)
		{
			return _maxDistanceUnits;
		}
		for (int i = 0; i < elements.Count; i++)
		{
			FeatureSegmentSubset featureSegmentSubset = elements[i];
			Polyline polyline = _polylines[featureSegmentSubset.Fid];
			if (polyline == null || polyline.Count < 2)
			{
				continue;
			}
			FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(polyline, isClosed: false);
			int count = featureSegmentSubset.Ranges.Count;
			for (int j = 0; j < count; j++)
			{
				Range1D range1D = featureSegmentSubset.Ranges[j];
				for (int k = range1D.Start; k <= range1D.End; k++)
				{
					double distanceToPointSquared = fastSegmentIterator[k].DistanceToPointSquared(pt);
					if (distanceToPointSquared < 0.0001)
					{
						return 0f;
					}
					if (distanceToPointSquared < searchRadiusSquared2)
					{
						searchRadiusSquared2 = distanceToPointSquared;
					}
				}
			}
		}
		return (float)Math.Sqrt(searchRadiusSquared2);
	}

	private void ResampleEDT(RasterDefinition raster, Span<float> outSpan)
	{
		float cellSize = (float)raster.CellSize;
		int maxDistanceCells = _maxDistanceCells;
		int colsRasterTerm = raster.Cols + 2 * maxDistanceCells;
		int rowsRasterTerm = raster.Rows + 2 * maxDistanceCells;
		int sz = colsRasterTerm * rowsRasterTerm;
		RasterDefinition rdef = new RasterDefinition(raster.Extent, cellSize).ExtendRaster(maxDistanceCells);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(sz, float.MaxValue, out mem))
		{
			Span<float> memSpan = mem.Span;
			RasterizePolylinesIntoBuffer(rdef, mem, colsRasterTerm);
			EuclideanDistanceTransform.Compute(memSpan, colsRasterTerm, rowsRasterTerm, cellSize);
			CropAndClamp(memSpan, colsRasterTerm, maxDistanceCells, raster.Cols, raster.Rows, outSpan);
		}
	}

	private void RasterizePolylinesIntoBuffer(RasterDefinition rdef, Memory<float> buffer, int cols)
	{
		foreach (Polyline polyline in _polylines)
		{
			if (polyline == null || polyline.Count < 2)
			{
				continue;
			}
			FastSegmentIterator fastSegmentIterator = new FastSegmentIterator(polyline, isClosed: false);
			for (int i = 0; i < polyline.SegmentCount; i++)
			{
				Segment segment = fastSegmentIterator[i];
				segment.Rasterize(rdef, (buffer, cols), (int idx, (Memory<float> buffer, int cols) payload) =>
				{
					if (idx >= 0 && idx < payload.buffer.Length)
					{
						payload.buffer.Span[idx] = 0f;
					}
				});
			}
		}
	}

	private void CropAndClamp(Span<float> expanded, int expandedCols, int halo, int outCols, int outRows, Span<float> outSpan)
	{
		float maxDistanceUnits = _maxDistanceUnits;
		for (int i = 0; i < outRows; i++)
		{
			for (int j = 0; j < outCols; j++)
			{
				int index = (i + halo) * expandedCols + (j + halo);
				int index2 = i * outCols + j;
				float expanded2 = expanded[index];
				outSpan[index2] = Math.Min(expanded2, maxDistanceUnits);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		if (_polylines == null || _polylines.Count == 0 || _segmentSpatialIndex == null)
		{
			outData.Span.Fill(_maxDistanceUnits);
			return;
		}
		double searchRadius = _maxDistanceUnits;
		double searchRadiusSquared = searchRadius * searchRadius;
		float maxDist = _maxDistanceUnits;
		Parallel.SmartFor(points.Length, (int start, int end) =>
		{
			Span<Point> pointsSpan = points.Span;
			Span<float> outDataSpan = outData.Span;
			for (int i = start; i <= end; i++)
			{
				float minDistanceSpatialIndex = ComputeMinDistanceSpatialIndex(pointsSpan[i], searchRadius, searchRadiusSquared);
				outDataSpan[i] = Math.Min(minDistanceSpatialIndex, maxDist);
			}
		});
	}
}

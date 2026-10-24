using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;

namespace Geospatial.Vectors.Rasterizers;

public class SurfaceRasterizer
{
	private const double MaxWeight = 100000.0;

	private readonly RasterDefinition _raster;

	private readonly IList<Point> _points;

	private readonly double[,] _normalizedInvDistanceWeights;

	public RasterDefinition Raster => _raster;

	public SurfaceRasterizer(RasterDefinition raster, IList<Point> points)
	{
		if (raster.IsNull())
		{
			throw new ArgumentNullException("raster");
		}
		if (points == null || points.Count == 0)
		{
			throw new ArgumentNullException("points");
		}
		_raster = raster;
		_points = points;
		_normalizedInvDistanceWeights = new double[_raster.CellCount, points.Count];
		for (int i = 0; i < _raster.CellCount; i++)
		{
			Point point = _raster.CellCenter(i);
			double normalizedInvDistanceWeightsSum = 0.0;
			for (int j = 0; j < points.Count; j++)
			{
				double normalizedInvDistanceWeights = 1.0 / point.DistanceTo(points[j]);
				if (normalizedInvDistanceWeights > 100000.0 || double.IsNaN(normalizedInvDistanceWeights))
				{
					normalizedInvDistanceWeights = 100000.0;
				}
				normalizedInvDistanceWeightsSum += normalizedInvDistanceWeights;
				_normalizedInvDistanceWeights[i, j] = normalizedInvDistanceWeights;
			}
			for (int k = 0; k < points.Count; k++)
			{
				_normalizedInvDistanceWeights[i, k] /= normalizedInvDistanceWeightsSum;
			}
		}
	}

	public void Rasterize(double[] zValues, ref double[] buffer)
	{
		if (zValues == null || zValues.Length != _points.Count)
		{
			throw new ArgumentException("Z Value buffer doesn't match input point length.");
		}
		if (_raster.CellCount != buffer.Length)
		{
			buffer = new double[_raster.CellCount];
		}
		if (zValues.All((double a) => a != -9999.0))
		{
			RasterizeInternal(zValues, buffer);
		}
	}

	private void RasterizeInternal(double[] zValues, double[] buffer)
	{
		for (int i = 0; i < _raster.CellCount; i++)
		{
			for (int j = 0; j < zValues.Length; j++)
			{
				double zValue = zValues[j];
				buffer[i] += zValue * _normalizedInvDistanceWeights[i, j];
			}
		}
	}

	private void RasterizeInternal_Reweight(double[] zValues, double[] buffer)
	{
		for (int i = 0; i < _raster.CellCount; i++)
		{
		}
	}
}

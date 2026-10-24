using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Optimization;

public sealed class ScaledZoneResampler : IResample<float>, ISurface<float>
{
	private readonly IResample<float> _inner;

	private readonly IReadOnlyList<ManningZoneScale> _zones;

	public Extent ValidExtent => _inner.ValidExtent;

	public float? NoDataValue => _inner.NoDataValue;

	public ScaledZoneResampler(IResample<float> inner, IReadOnlyList<ManningZoneScale> zones)
	{
		_inner = inner ?? throw new ArgumentNullException("inner");
		_zones = zones ?? throw new ArgumentNullException("zones");
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		_inner.Resample(args, outData);
		RasterDefinition raster = args.Raster;
		Span<float> outDataSpan = outData.Span;
		float ndv = NoDataValue ?? float.NaN;
		for (int i = 0; i < outDataSpan.Length; i++)
		{
			if (!IsNoData(outDataSpan[i], ndv))
			{
				Point point = raster.CellCenter(i);
				outDataSpan[i] *= ScaleAt(point.X, point.Y);
			}
		}
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		_inner.SamplePoints(points, outData, sampleParams);
		Span<Point> pointsSpan = points.Span;
		Span<float> outDataSpan = outData.Span;
		float ndv = NoDataValue ?? float.NaN;
		for (int i = 0; i < outDataSpan.Length; i++)
		{
			if (!IsNoData(outDataSpan[i], ndv))
			{
				outDataSpan[i] *= ScaleAt(pointsSpan[i].X, pointsSpan[i].Y);
			}
		}
	}

	private static bool IsNoData(float computed, float ndv)
	{
		if (!float.IsNaN(ndv))
		{
			return computed == ndv;
		}
		return float.IsNaN(computed);
	}

	private float ScaleAt(double x, double y)
	{
		double unity = 1.0;
		for (int i = 0; i < _zones.Count; i++)
		{
			ManningZoneScale manningZoneScale = _zones[i];
			if (manningZoneScale.Factor != 1.0 && manningZoneScale.Zone.Boundary.Contains(x, y))
			{
				unity *= manningZoneScale.Factor;
			}
		}
		return (float)unity;
	}
}

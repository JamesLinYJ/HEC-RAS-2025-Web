using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters.Synthetics;

public class BandedPerlinNoise : IMultiBandResampler<float>, ISurface<float>
{
	private PerlinNoise _basePerlin;

	public int ProfileCount { get; }

	public Extent ValidExtent => _basePerlin.ValidExtent;

	public float? NoDataValue => _basePerlin.NoDataValue;

	public BandedPerlinNoise(PerlinNoise basePerlin, int nProfiles)
	{
		_basePerlin = basePerlin;
		ProfileCount = nProfiles;
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		double z = (double)bandIndex / (double)ProfileCount;
		Extent validExtent = ValidExtent;
		Span<float> outDataSpan = outData.Span;
		float valueOrDefault = NoDataValue.GetValueOrDefault();
		RasterDefinition raster = args.Raster;
		for (int i = 0; i < raster.CellCount; i++)
		{
			Point pt = raster.CellCenter(i);
			if (!validExtent.Contains(pt))
			{
				outDataSpan[i] = valueOrDefault;
			}
			else
			{
				outDataSpan[i] = (float)_basePerlin.OctavePerlin(pt.X, pt.Y, z);
			}
		}
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<float>[] outData)
	{
		throw new NotImplementedException();
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<float> outData)
	{
		throw new NotImplementedException();
	}
}

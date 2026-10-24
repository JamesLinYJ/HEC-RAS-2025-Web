using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Utility.Units;

namespace Geospatial.Rasters;

public class ConstantVelocityRaster : ITimeSeriesVelocityRasterizer<float>, ITimeSeriesVelocityResampler<float>, ITimeSeriesResampler<float>, IMultiBandResampler<float>, ISurface<float>, IDiscreteTimesteps, IRasterDefinition
{
	private static readonly List<DateTime> _dt = new List<DateTime>(new DateTime[2]
	{
		DateTime.MinValue,
		DateTime.MaxValue
	});

	public Extent ValidExtent
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public float? NoDataValue
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public IReadOnlyList<DateTime> Times => _dt;

	public RasterDefinition RasterDefinition
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public bool CanInterpolateTimesteps
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public int ProfileCount => Times.Count;

	public ConstantVelocityRaster()
	{
		throw new NotImplementedException();
	}

	public void GetRangeVxVy(PixelRange range, int bandIndex, Memory<float> vxResult, Memory<float> vyResult, Memory<float> magResult)
	{
		throw new NotImplementedException();
	}

	public void GetRangeMagDir(PixelRange range, int bandIndex, Memory<float> magResult, Memory<float> dirResult)
	{
		throw new NotImplementedException();
	}

	public void ResampleVxVy(ResampleArgs args, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outVelMag)
	{
		throw new NotImplementedException();
	}

	public void ResampleMagDir(ResampleArgs args, int bandIndex, Memory<float> outMag, Memory<float> outDir, DirectionConvention dirConvention = DirectionConvention.GoingToward)
	{
		throw new NotImplementedException();
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] vxResult, Memory<float>[] vyResult)
	{
		throw new NotImplementedException();
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandStart, int bandCount, Memory<float>[] outVx, Memory<float>[] outVy, Memory<float>[] outVelMag)
	{
		throw new NotImplementedException();
	}

	public void SamplePointsVxVy(Memory<Point> points, int bandIndex, Memory<float> outVx, Memory<float> outVy, Memory<float> outMagnitude)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<float> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<float> outData, ResampleCache cache = null)
	{
		throw new NotImplementedException();
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

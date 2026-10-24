using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class ConstantTimeSeriesResampler<T> : ITimeSeriesResampler<T>, IMultiBandResampler<T>, ISurface<T>, IDiscreteTimesteps where T : unmanaged, IEquatable<T>
{
	private static readonly DateTime[] ConstantBuffer = new DateTime[2]
	{
		DateTime.MinValue,
		DateTime.MaxValue
	};

	public bool CanInterpolateTimesteps => true;

	public int ProfileCount => Times.Count;

	public Extent ValidExtent { get; set; }

	public T? NoDataValue { get; set; }

	public IReadOnlyList<DateTime> Times => ConstantBuffer;

	public T Value { get; set; }

	public ConstantTimeSeriesResampler(T localValue, Extent ext)
	{
		Value = localValue;
		ValidExtent = ext;
	}

	public void Resample(ResampleArgs args, DateTime time, Memory<T> outData, ResampleCache cache = null)
	{
		outData.Span.Fill(Value);
	}

	public void Resample(ResampleArgs args, int bandIndex, Memory<T> outData, ResampleCache cache = null)
	{
		outData.Span.Fill(Value);
	}

	public void Resample(ResampleArgs args, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		for (int i = 0; i < outData.Length; i++)
		{
			outData[i].Span.Fill(Value);
		}
	}

	public void SamplePoints(Memory<Point> points, int startBandIndex, int bandCount, Memory<T>[] outData)
	{
		for (int i = 0; i < outData.Length; i++)
		{
			outData[i].Span.Fill(Value);
		}
	}

	public void SamplePoints(Memory<Point> points, int bandIndex, Memory<T> outData)
	{
		outData.Span.Fill(Value);
	}

	public TimeSeries SamplePointTimeSeries(Point pt)
	{
		throw new NotImplementedException();
	}
}

using System;

namespace Geospatial.PairedData.TimeSeriesNew;

public static class ITimeSeriesExtensions
{
	internal class NormalizeSeries<T> : IFunctionalSeries<double, T> where T : unmanaged, IEquatable<T>
	{
		private readonly ITimeSeries<T> _ts;

		public T? NoDataValue => _ts.NoDataValue;

		public double LowerBound => 0.0;

		public double UpperBound => 1.0;

		public NormalizeSeries(ITimeSeries<T> ts)
		{
			_ts = ts;
		}

		public T Sample(double input)
		{
			if (input < LowerBound || input > UpperBound)
			{
				return NoDataValue.GetValueOrDefault();
			}
			long input2 = (long)((input - LowerBound) / (UpperBound - LowerBound));
			return _ts.Sample(input2);
		}

		public void Sample(Memory<double> inputs, Memory<T> outData)
		{
			Span<double> inputsSpan = inputs.Span;
			Span<T> outDataSpan = outData.Span;
			for (int i = 0; i < inputsSpan.Length; i++)
			{
				outDataSpan[i] = Sample(inputsSpan[i]);
			}
		}
	}

	public static T[] Sample<T>(this ITimeSeries<T> ts, Memory<long> times) where T : unmanaged, IEquatable<T>
	{
		T[] values = new T[times.Length];
		ts.Sample(times, values);
		return values;
	}

	public static T Sample<T>(this ITimeSeries<T> ts, DateTime time) where T : unmanaged, IEquatable<T>
	{
		return ts.Sample(time.Ticks);
	}

	public static IFunctionalSeries<double, T> NormalizeTime<T>(this ITimeSeries<T> ts) where T : unmanaged, IEquatable<T>
	{
		return new NormalizeSeries<T>(ts);
	}
}

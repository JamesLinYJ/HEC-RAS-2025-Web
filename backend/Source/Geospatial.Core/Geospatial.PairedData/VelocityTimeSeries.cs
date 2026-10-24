using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Utility.Units;

namespace Geospatial.PairedData;

public class VelocityTimeSeries : IDiscreteVelocityTimeSeries
{
	private readonly TimestepCollection _tsm;

	private readonly double[] _xValues;

	private readonly IReadOnlyList<double> _xValuesRO;

	private readonly double[] _yValues;

	private readonly IReadOnlyList<double> _yValuesRO;

	public IReadOnlyList<DateTime> Times => _tsm.Times;

	public IReadOnlyList<double> XValues => _xValuesRO;

	public IReadOnlyList<double> YValues => _yValuesRO;

	public VelocityTimeSeries(IList<DateTime> times, IList<double> xValues, IList<double> yValues)
	{
		_tsm = new TimestepCollection(times, TimeSeriesDataType.Rate);
		_xValues = xValues.ToArray();
		_yValues = yValues.ToArray();
		_xValuesRO = new ReadOnlyCollection<double>(_xValues);
		_yValuesRO = new ReadOnlyCollection<double>(_yValues);
	}

	public VelocityTimeSeries(TimestepCollection tsm, IList<double> xValues, IList<double> yValues)
	{
		_tsm = tsm;
		_xValues = XValues.ToArray();
		_yValues = yValues.ToArray();
		_xValuesRO = new ReadOnlyCollection<double>(_xValues);
		_yValuesRO = new ReadOnlyCollection<double>(_yValues);
	}

	public static VelocityTimeSeries FromMagnitudeDirection(IList<DateTime> times, IList<double> magnitudes, IList<double> direction, DirectionConvention dc)
	{
		if (magnitudes == null)
		{
			throw new ArgumentNullException("magnitudes");
		}
		if (direction == null)
		{
			throw new ArgumentNullException("direction");
		}
		if (magnitudes.Count != direction.Count)
		{
			throw new ArgumentException("Magnitude/Direction count mismatch");
		}
		int count = magnitudes.Count;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			VelocityMath.ConvertToVxVy(direction[i], magnitudes[i], dc, out var vx, out var vy);
			doubleValues[i] = vx;
			doubleValuesArray[i] = vy;
		}
		return new VelocityTimeSeries(times, doubleValues, doubleValuesArray);
	}

	public void Sample(DateTime time, out double vx, out double vy)
	{
		_tsm.GetRateWeights(time, out var weight, out var weight2);
		bool isNull = weight.IsNull();
		bool isNull2 = weight2.IsNull();
		if (isNull & isNull2)
		{
			vx = 0.0;
			vy = 0.0;
		}
		else if (isNull)
		{
			vx = weight2.Evaluate(_xValues);
			vy = weight2.Evaluate(_yValues);
		}
		else if (isNull2)
		{
			vx = weight.Evaluate(_xValues);
			vy = weight.Evaluate(_yValues);
		}
		else
		{
			vx = weight.Evaluate(_xValues) + weight2.Evaluate(_xValues);
			vy = weight.Evaluate(_yValues) + weight2.Evaluate(_yValues);
		}
	}

	public void Sample(IList<DateTime> times, out double[] vx, out double[] vy)
	{
		vx = new double[times.Count];
		vy = new double[times.Count];
		for (int i = 0; i < times.Count; i++)
		{
			DateTime dt = times[i];
			_tsm.GetRateWeights(dt, out var weight, out var weight2);
			bool isNull = weight.IsNull();
			bool isNull2 = weight2.IsNull();
			if (isNull & isNull2)
			{
				vx[i] = 0.0;
				vy[i] = 0.0;
			}
			else if (isNull)
			{
				vx[i] = weight2.Evaluate(_xValues);
				vy[i] = weight2.Evaluate(_yValues);
			}
			else if (isNull2)
			{
				vx[i] = weight.Evaluate(_xValues);
				vy[i] = weight.Evaluate(_yValues);
			}
			else
			{
				vx[i] = weight.Evaluate(_xValues) + weight2.Evaluate(_xValues);
				vy[i] = weight.Evaluate(_yValues) + weight2.Evaluate(_yValues);
			}
		}
	}
}

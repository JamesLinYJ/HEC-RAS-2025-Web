using System;
using Utility;

namespace Geospatial.Transformers;

public class RotateValueTransformer : IValueTransformer<float>, IValueTransformer<double>
{
	private readonly float _valueF;

	private readonly double _valueD;

	private readonly RotationUnits _units;

	private readonly Orientation _orientation;

	public RotateValueTransformer(float rotationAmount, RotationUnits units, Orientation orientation)
	{
		_valueF = rotationAmount;
		_valueD = rotationAmount;
		_units = units;
		_orientation = orientation;
	}

	public RotateValueTransformer(double rotationAmount, RotationUnits units, Orientation orientation)
	{
		_valueF = (float)rotationAmount;
		_valueD = rotationAmount;
		_units = units;
		_orientation = orientation;
	}

	public void Transform(Memory<float> data, float? noDataValue)
	{
		float ndv;
		if (noDataValue.HasValue)
		{
			ndv = noDataValue.Value;
		}
		else
		{
			ndv = float.NaN;
		}
		float add = _valueF;
		if (_orientation == Orientation.CounterClockwise)
		{
			add = 0f - _valueF;
		}
		float max;
		if (_units == RotationUnits.Degrees)
		{
			max = 360f;
		}
		else
		{
			max = (float)Math.PI * 2f;
		}
		Parallel.SmartFor(data.Length, (int start, int end) =>
		{
			Span<float> floatSpan = data.Span;
			for (int i = start; i <= end; i++)
			{
				float computed = floatSpan[i];
				if (computed != ndv)
				{
					computed += add;
					if (computed < 0f)
					{
						computed += max;
					}
					else if (computed >= max)
					{
						computed -= max;
					}
					floatSpan[i] = computed;
				}
			}
		});
	}

	public void Transform(Memory<double> data, double? noDataValue)
	{
		double ndv;
		if (noDataValue.HasValue)
		{
			ndv = noDataValue.Value;
		}
		else
		{
			ndv = double.NaN;
		}
		double add = _valueF;
		if (_orientation == Orientation.CounterClockwise)
		{
			add = 0f - _valueF;
		}
		double max;
		if (_units == RotationUnits.Degrees)
		{
			max = 360.0;
		}
		else
		{
			max = Math.PI * 2.0;
		}
		Parallel.SmartFor(data.Length, (int start, int end) =>
		{
			Span<double> doubleSpan = data.Span;
			for (int i = start; i <= end; i++)
			{
				double computed = doubleSpan[i];
				if (computed != ndv)
				{
					computed += add;
					if (computed < 0.0)
					{
						computed += max;
					}
					else if (computed >= max)
					{
						computed -= max;
					}
					doubleSpan[i] = computed;
				}
			}
		});
	}
}

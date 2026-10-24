using System;

namespace Ras.Optimization;

public sealed class ClampBoundsHandler : IBoundsHandler
{
	private readonly double[] _min;

	private readonly double[] _max;

	private readonly ParameterScale[] _scales;

	private readonly double[] _warpedMin;

	private readonly double[] _warpedMax;

	public int Dimension => _min.Length;

	public double[] PhysicalMin => (double[])_min.Clone();

	public double[] PhysicalMax => (double[])_max.Clone();

	public ParameterScale[] Scales => (ParameterScale[])_scales.Clone();

	public ClampBoundsHandler(double[] min, double[] max, ParameterScale[] scales = null)
	{
		if (min == null || max == null)
		{
			throw new ArgumentNullException((min == null) ? "min" : "max");
		}
		if (min.Length != max.Length)
		{
			throw new ArgumentException("Min and Max must have the same length.");
		}
		for (int i = 0; i < min.Length; i++)
		{
			if (max[i] < min[i])
			{
				throw new ArgumentException($"Max[{i}] ({max[i]}) is less than Min[{i}] ({min[i]}).");
			}
		}
		_min = (double[])min.Clone();
		_max = (double[])max.Clone();
		_scales = ParameterScaling.Resolve(scales, min.Length);
		ParameterScaling.ValidateBounds(_min, _scales);
		_warpedMin = new double[_min.Length];
		_warpedMax = new double[_min.Length];
		for (int j = 0; j < _min.Length; j++)
		{
			_warpedMin[j] = ParameterScaling.Warp(_min[j], _scales[j]);
			_warpedMax[j] = ParameterScaling.Warp(_max[j], _scales[j]);
		}
	}

	public double[] ToSearch(double[] physical)
	{
		CheckLength(physical);
		double[] doubleValues = new double[physical.Length];
		for (int i = 0; i < physical.Length; i++)
		{
			doubleValues[i] = ParameterScaling.Warp(Math.Clamp(physical[i], _min[i], _max[i]), _scales[i]);
		}
		return doubleValues;
	}

	public double[] ToPhysical(double[] search)
	{
		CheckLength(search);
		double[] doubleValues = new double[search.Length];
		for (int i = 0; i < search.Length; i++)
		{
			doubleValues[i] = ParameterScaling.Unwarp(Math.Clamp(search[i], _warpedMin[i], _warpedMax[i]), _scales[i]);
		}
		return doubleValues;
	}

	public double[] SearchScale()
	{
		double[] doubleValues = new double[_min.Length];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			doubleValues[i] = _warpedMax[i] - _warpedMin[i];
		}
		return doubleValues;
	}

	private void CheckLength(double[] v)
	{
		if (v.Length != _min.Length)
		{
			throw new ArgumentException($"Vector length {v.Length} does not match dimension {_min.Length}.");
		}
	}
}

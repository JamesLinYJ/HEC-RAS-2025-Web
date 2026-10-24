using System;

namespace Ras.Optimization;

public sealed class TransformBoundsHandler : IBoundsHandler
{
	private const double Margin = 1E-09;

	private const double LogitScale = 6.0;

	private readonly double[] _min;

	private readonly double[] _max;

	private readonly ParameterScale[] _scales;

	private readonly double[] _warpedMin;

	private readonly double[] _warpedMax;

	public int Dimension => _min.Length;

	public double[] PhysicalMin => (double[])_min.Clone();

	public double[] PhysicalMax => (double[])_max.Clone();

	public ParameterScale[] Scales => (ParameterScale[])_scales.Clone();

	public TransformBoundsHandler(double[] min, double[] max, ParameterScale[] scales = null)
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
		if (physical.Length != _min.Length)
		{
			throw new ArgumentException($"Vector length {physical.Length} does not match dimension {_min.Length}.");
		}
		double[] doubleValues = new double[physical.Length];
		for (int i = 0; i < physical.Length; i++)
		{
			double warpedMaxWarpedMinTerm = _warpedMax[i] - _warpedMin[i];
			if (warpedMaxWarpedMinTerm <= 0.0)
			{
				doubleValues[i] = 0.0;
				continue;
			}
			double computed = (ParameterScaling.Warp(Math.Clamp(physical[i], _min[i], _max[i]), _scales[i]) - _warpedMin[i]) / warpedMaxWarpedMinTerm;
			computed = Math.Clamp(computed, 1E-09, 0.999999999);
			doubleValues[i] = Math.Log(computed / (1.0 - computed));
		}
		return doubleValues;
	}

	public double[] ToPhysical(double[] search)
	{
		if (search.Length != _min.Length)
		{
			throw new ArgumentException($"Vector length {search.Length} does not match dimension {_min.Length}.");
		}
		double[] doubleValues = new double[search.Length];
		for (int i = 0; i < search.Length; i++)
		{
			double warpedMaxWarpedMinTerm = _warpedMax[i] - _warpedMin[i];
			if (warpedMaxWarpedMinTerm <= 0.0)
			{
				doubleValues[i] = _min[i];
				continue;
			}
			double searchTerm = 1.0 / (1.0 + Math.Exp(0.0 - search[i]));
			doubleValues[i] = ParameterScaling.Unwarp(_warpedMin[i] + searchTerm * warpedMaxWarpedMinTerm, _scales[i]);
		}
		return doubleValues;
	}

	public double[] SearchScale()
	{
		double[] doubleValues = new double[_min.Length];
		for (int i = 0; i < doubleValues.Length; i++)
		{
			doubleValues[i] = ((_warpedMax[i] - _warpedMin[i] > 0.0) ? 6.0 : 0.0);
		}
		return doubleValues;
	}
}

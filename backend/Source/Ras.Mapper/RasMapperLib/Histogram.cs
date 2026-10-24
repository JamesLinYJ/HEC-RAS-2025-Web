using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class Histogram
{
	private double _minimum;

	private double _min_offset;

	private double _maximum;

	private double _delta;

	private double _increment;

	private double _invIncrement;

	private double[] _bin;

	public double Maximum => _maximum;

	public double Minimum => _minimum;

	public int BinCount => _bin.Count();

	// C# has no syntax for parameterized property 'BinArea'.
	public double get_BinArea(int index)
	{
		return _bin[index];
	}

	public double BinWidth => _increment;

	// C# has no syntax for parameterized property 'BinMaximum'.
	public double get_BinMaximum(int index)
	{
		return _minimum + _increment * (double)checked(index + 1);
	}

	// C# has no syntax for parameterized property 'BinMinimum'.
	public double get_BinMinimum(int index)
	{
		return _minimum + _increment * (double)index;
	}

	// C# has no syntax for parameterized property 'BinMiddle'.
	public double get_BinMiddle(int index)
	{
		return _minimum + _increment * ((double)index + 0.5);
	}

	public static Histogram ComputeFrom(float[] values, int binCt, bool ignoreNoData = true)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		checked
		{
			int count = values.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				float computed = values[i];
				if (!ignoreNoData || computed != -9999f)
				{
					maxValue = System.Math.Min(computed, maxValue);
					minValue = System.Math.Max(computed, minValue);
				}
			}
			Histogram histogram = new Histogram(maxValue, minValue, binCt);
			int count2 = values.Length - 1;
			for (int j = 0; j <= count2; j++)
			{
				float computed2 = values[j];
				if (!ignoreNoData || computed2 != -9999f)
				{
					histogram.AddArea(computed2, 1.0);
				}
			}
			return histogram;
		}
	}

	public bool IsNull()
	{
		if (_bin.IsNullOrEmpty())
		{
			return true;
		}
		return false;
	}

	public static Histogram NullHistogram()
	{
		return new Histogram(0.0, 0.0, new int[0], 0.0);
	}

	public Histogram(int binCount)
	{
		Array.Resize(ref _bin, binCount);
	}

	public Histogram(double minimum, double maximum)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		Array.Resize(ref _bin, 256);
		_increment = (_maximum - _minimum) / (double)_bin.Count();
		_invIncrement = 1.0 / _increment;
		_min_offset = _minimum + _increment * 0.5;
	}

	public Histogram(double minimum, double maximum, int binCount)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		Array.Resize(ref _bin, binCount);
		_increment = (_maximum - _minimum) / (double)_bin.Count();
		_invIncrement = 1.0 / _increment;
		_min_offset = _minimum + _increment * 0.5;
	}

	public Histogram(double minimum, double maximum, int[] bins, double areaPerCell)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		Array.Resize(ref _bin, bins.Count());
		_increment = (_maximum - _minimum) / (double)_bin.Count();
		_invIncrement = 1.0 / _increment;
		_min_offset = _minimum + _increment * 0.5;
		checked
		{
			int count = bins.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				_bin[i] = (double)bins[i] * areaPerCell;
			}
		}
	}

	public void Reset(double minimum, double maximum)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		Array.Clear(_bin, 0, _bin.Count());
		_increment = (_maximum - _minimum) / (double)_bin.Count();
		_invIncrement = 1.0 / _increment;
		_min_offset = _minimum + _increment * 0.5;
	}

	public void AddArea(double doubleValue, double area)
	{
		_bin[BinIndex(doubleValue)] += area;
	}

	public void AddArea(int binIndex, double area)
	{
		_bin[binIndex] += area;
	}

	public void AddTriangle(double area, double v0, double v1, double v2)
	{
		if (double.IsNaN(v0) || double.IsNaN(v1) || double.IsNaN(v2))
		{
			return;
		}
		double a = v0;
		double b = v1;
		double c = v2;
		RasMapperLib.Utilities.Math.Sort3(ref a, ref b, ref c);
		int binIndexStart = BinIndex(a);
		int binIndexStart2 = BinIndex(b);
		int binIndex = BinIndex(c);
		if (binIndex == binIndexStart)
		{
			AddArea(binIndex, area);
			return;
		}
		double areaTerm = 2.0 * area / (c - a);
		checked
		{
			double computed;
			if (a < b)
			{
				computed = areaTerm / (b - a);
				int binIndexEnd = binIndexStart2;
				for (int i = binIndexStart; i <= binIndexEnd; i++)
				{
					double get_BinMinimum2 = this.get_BinMinimum(i);
					double get_BinMaximum2 = this.get_BinMaximum(i);
					if (get_BinMinimum2 < a)
					{
						get_BinMinimum2 = a;
					}
					if (get_BinMaximum2 > b)
					{
						get_BinMaximum2 = b;
					}
					double get_BinMaximum2Get_BinMinimum2Term = (get_BinMaximum2 + get_BinMinimum2) * 0.5;
					AddArea(i, (get_BinMaximum2 - get_BinMinimum2) * (get_BinMaximum2Get_BinMinimum2Term - a) * computed);
				}
			}
			if (!(b < c))
			{
				return;
			}
			computed = areaTerm / (c - b);
			int binIndexEnd2 = binIndex;
			for (int j = binIndexStart2; j <= binIndexEnd2; j++)
			{
				double get_BinMinimum3 = this.get_BinMinimum(j);
				double get_BinMaximum3 = this.get_BinMaximum(j);
				if (get_BinMinimum3 < b)
				{
					get_BinMinimum3 = b;
				}
				if (get_BinMaximum3 > c)
				{
					get_BinMaximum3 = c;
				}
				double get_BinMaximum3Get_BinMinimum3Term = (get_BinMaximum3 + get_BinMinimum3) * 0.5;
				AddArea(j, (get_BinMaximum3 - get_BinMinimum3) * (c - get_BinMaximum3Get_BinMinimum3Term) * computed);
			}
		}
	}

	public void AddSurfaceStatistics(SurfaceStatistics SurfaceStatistics)
	{
		if (SurfaceStatistics != null)
		{
			AddQuantiles(SurfaceStatistics.Area, SurfaceStatistics.Quantiles);
		}
	}

	public void AddQuantiles(double surfaceArea, IList<double> quantiles)
	{
		checked
		{
			double surfaceAreaOverCountOffset = surfaceArea / (double)(quantiles.Count - 1);
			int binIndex = BinIndex(quantiles[0]);
			int count = quantiles.Count - 1;
			for (int i = 1; i <= count; i++)
			{
				int binIndex2 = BinIndex(quantiles[i]);
				double quantile = quantiles[i - 1];
				double quantile2 = quantiles[i];
				double quantile2AboveQuantile = quantile2 - quantile;
				int binIndexStart = binIndex;
				int binIndexEnd = binIndex2;
				for (int j = binIndexStart; j <= binIndexEnd; j++)
				{
					double get_BinMinimum2 = this.get_BinMinimum(j);
					double get_BinMinimum2PlusIncrement = get_BinMinimum2 + _increment;
					double systemGet_BinMinimum2Term = System.Math.Max(get_BinMinimum2, quantile);
					double systemQuantile2Term = (System.Math.Min(get_BinMinimum2PlusIncrement, quantile2) - systemGet_BinMinimum2Term) / quantile2AboveQuantile * surfaceAreaOverCountOffset;
					_bin[j] += systemQuantile2Term;
				}
				binIndex = binIndex2;
			}
		}
	}

	public void AddHistrogam(Histogram histogram)
	{
		checked
		{
			int count = histogram.BinCount - 1;
			for (int i = 0; i <= count; i++)
			{
				double area = histogram.get_BinArea(i);
				double get_BinMiddle2 = histogram.get_BinMiddle(i);
				AddArea(get_BinMiddle2, area);
			}
		}
	}

	public double[] ComputeQuantiles(int quantilesCount = 1024)
	{
		if (_minimum == _maximum)
		{
			return new double[1] { _maximum };
		}
		checked
		{
			double[] doubleValues = new double[quantilesCount - 1 + 1];
			doubleValues[0] = _minimum;
			double quantilesCountTerm = 1.0 / (double)(quantilesCount - 1);
			double area = ComputeArea();
			if (area == 0.0)
			{
				return null;
			}
			double binSumOverArea = 0.0;
			int unity = 1;
			double computed = quantilesCountTerm;
			double binSum = 0.0;
			int count = _bin.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				binSum += _bin[i];
				double binSumOverArea2;
				for (binSumOverArea2 = binSum / area; !(computed > binSumOverArea2); computed += quantilesCountTerm)
				{
					doubleValues[unity] = this.get_BinMinimum(i) + (computed - binSumOverArea) / (binSumOverArea2 - binSumOverArea) * _increment;
					unity++;
				}
				binSumOverArea = binSumOverArea2;
			}
			doubleValues[quantilesCount - 1] = _maximum;
			return doubleValues;
		}
	}

	public void ComputeCumulativeValues(Point2DCollection pts)
	{
		pts.Clear();
		if (pts.Capacity < _bin.Count())
		{
			pts.Capacity = _bin.Count();
		}
		double binSum = 0.0;
		checked
		{
			int count = _bin.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				binSum += _bin[i];
				pts.Add(new Point2D((float)binSum, (float)this.get_BinMinimum(i)));
			}
		}
	}

	public double ComputeArea()
	{
		double binSum = 0.0;
		checked
		{
			int count = _bin.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				binSum += _bin[i];
			}
			return binSum;
		}
	}

	public double ComputeMean()
	{
		double computed = 0.0;
		double incrementSum = _increment * 0.5;
		checked
		{
			int count = _bin.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				computed += incrementSum * _bin[i];
				incrementSum += _increment;
			}
			return computed / ComputeArea() + _minimum;
		}
	}

	public double ComputeStandardDeviation()
	{
		double mean = ComputeMean();
		double computed = 0.0;
		checked
		{
			int count = _bin.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				computed += System.Math.Pow(this.get_BinMiddle(i) - mean, 2.0) * _bin[i];
			}
			return System.Math.Sqrt(computed / ComputeArea());
		}
	}

	public SurfaceStatistics ComputeSurfaceStatistics()
	{
		return new SurfaceStatistics(Maximum, Minimum, ComputeMean(), ComputeStandardDeviation(), ComputeArea(), ComputeQuantiles());
	}

	public int BinIndex(double doubleValue)
	{
		if (doubleValue <= _minimum)
		{
			return 0;
		}
		checked
		{
			if (doubleValue >= _maximum)
			{
				return _bin.Count() - 1;
			}
			return (int)System.Math.Round((doubleValue - _min_offset) * _invIncrement);
		}
	}

	public int BinIndex(double doubleValue, ref double minInclusive, ref double maxExclusive)
	{
		if (doubleValue <= _minimum)
		{
			minInclusive = _minimum;
			maxExclusive = _minimum + _increment;
			return 0;
		}
		checked
		{
			if (doubleValue >= _maximum)
			{
				minInclusive = _maximum - _increment;
				maxExclusive = _maximum;
				return _bin.Count() - 1;
			}
			int systemMin_offsetTerm = (int)System.Math.Round((doubleValue - _min_offset) * _invIncrement);
			minInclusive = _minimum + (double)systemMin_offsetTerm * _increment;
			maxExclusive = minInclusive + _increment;
			return systemMin_offsetTerm;
		}
	}

	public void WriteDataToFile(string filename)
	{
		StreamWriter streamWriter = new StreamWriter(filename);
		streamWriter.WriteLine("Bin Center\tArea");
		checked
		{
			int count = _bin.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				double get_BinMiddle2 = this.get_BinMiddle(i);
				double bin = _bin[i];
				streamWriter.WriteLine(get_BinMiddle2 + "\t" + bin);
			}
			streamWriter.Close();
		}
	}

	public static Histogram MergeHistograms(List<Histogram> histograms)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		foreach (Histogram histogram2 in histograms)
		{
			maxValue = System.Math.Min(histogram2.Minimum, maxValue);
			minValue = System.Math.Max(histogram2.Maximum, minValue);
		}
		Histogram histogram = new Histogram(maxValue, minValue);
		foreach (Histogram histogram3 in histograms)
		{
			histogram.AddHistrogam(histogram3);
		}
		return histogram;
	}
}

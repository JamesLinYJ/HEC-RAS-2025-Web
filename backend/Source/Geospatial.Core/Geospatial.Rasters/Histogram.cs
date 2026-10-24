using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Geospatial.Vectors;
using Geospatial.Vectors.Rasterizers;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Extensions;
using Utility.Logging;
using Utility.Memory;

namespace Geospatial.Rasters;

public class Histogram
{
	private static readonly ILogger _logger = LogHelper.GetLogger("Histogram");

	private readonly double _minimum;

	private readonly double _maximum;

	private readonly double _delta;

	private readonly double _binWidth;

	private readonly double _invBinWidth;

	private readonly double[] _bins;

	public double Minimum => _minimum;

	public double Maximum => _maximum;

	public double HistogramWidth => _delta;

	public double BinWidth => _binWidth;

	public IReadOnlyList<double> BinArea => _bins;

	public int BinCount => _bins.Length;

	public double BinMinimum(int index)
	{
		return _minimum + _binWidth * (double)index;
	}

	public double BinMaximum(int index)
	{
		return _minimum + _binWidth * (double)(index + 1);
	}

	public double BinCenter(int index)
	{
		return _minimum + _binWidth * ((double)index + 0.5);
	}

	public bool IsNull()
	{
		return _bins.IsNullOrEmpty();
	}

	public static Histogram NullHistogram()
	{
		return new Histogram(0.0, 0.0, Array.Empty<int>(), 0.0);
	}

	public bool IsSingleValue()
	{
		return _bins.Length == 1;
	}

	public static Histogram SingleValue(double doubleValue)
	{
		return SingleValue(doubleValue, 1.0);
	}

	public static Histogram SingleValue(double doubleValue, double area)
	{
		Histogram histogram = new Histogram(doubleValue - 0.1, doubleValue + 0.1, 1);
		histogram.AddArea(doubleValue, area);
		return histogram;
	}

	public Histogram(int binCount)
	{
		_bins = new double[binCount];
	}

	public Histogram(double minimum, double maximum)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		Array.Resize(ref _bins, 256);
		_binWidth = (_maximum - _minimum) / (double)_bins.Length;
		_invBinWidth = 1.0 / _binWidth;
	}

	public Histogram(double minimum, double maximum, int binCount)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		_bins = new double[binCount];
		_binWidth = (_maximum - _minimum) / (double)_bins.Length;
		_invBinWidth = 1.0 / _binWidth;
	}

	public Histogram(double minimum, double maximum, int[] bins, double areaPerCell)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		_bins = new double[bins.Length];
		_binWidth = (_maximum - _minimum) / (double)_bins.Length;
		_invBinWidth = 1.0 / _binWidth;
		for (int i = 0; i < bins.Length; i++)
		{
			_bins[i] = (double)bins[i] * areaPerCell;
		}
	}

	public Histogram(double minimum, double maximum, double[] binArea)
	{
		_minimum = minimum;
		_maximum = maximum;
		_delta = _maximum - _minimum;
		_bins = binArea.ToArray();
		_binWidth = (_maximum - _minimum) / (double)_bins.Length;
		_invBinWidth = 1.0 / _binWidth;
	}

	public void AddArea(double doubleValue, double area)
	{
		_bins[GetBinIndex(doubleValue)] += area;
	}

	public void AddArea(int binIndex, double area)
	{
		_bins[binIndex] += area;
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
		MathHelpers.Sort3(ref a, ref b, ref c);
		int binIndex = GetBinIndex(a);
		int binIndex2 = GetBinIndex(b);
		int binIndex3 = GetBinIndex(c);
		if (binIndex3 == binIndex)
		{
			AddArea(binIndex3, area);
			return;
		}
		double areaTerm = 2.0 * area / (c - a);
		double computed;
		if (a < b)
		{
			computed = areaTerm / (b - a);
			for (int i = binIndex; i <= binIndex2; i++)
			{
				double binMinimum = BinMinimum(i);
				double binMaximum = BinMaximum(i);
				if (binMinimum < a)
				{
					binMinimum = a;
				}
				if (binMaximum > b)
				{
					binMaximum = b;
				}
				double binMaximumBinMinimumTerm = (binMaximum + binMinimum) * 0.5;
				AddArea(i, (binMaximum - binMinimum) * (binMaximumBinMinimumTerm - a) * computed);
			}
		}
		if (!(b < c))
		{
			return;
		}
		computed = areaTerm / (c - b);
		for (int j = binIndex2; j <= binIndex3; j++)
		{
			double binMinimum2 = BinMinimum(j);
			double binMaximum2 = BinMaximum(j);
			if (binMinimum2 < b)
			{
				binMinimum2 = b;
			}
			if (binMaximum2 > c)
			{
				binMaximum2 = c;
			}
			double binMaximum2BinMinimum2Term = (binMaximum2 + binMinimum2) * 0.5;
			AddArea(j, (binMaximum2 - binMinimum2) * (c - binMaximum2BinMinimum2Term) * computed);
		}
	}

	public void AddQuantiles(double surfaceArea, IList<double> quantiles)
	{
		double surfaceAreaOverCountOffset = surfaceArea / (double)(quantiles.Count - 1);
		int binIndex2 = GetBinIndex(quantiles[0]);
		for (int i = 1; i < quantiles.Count; i++)
		{
			int binIndex = GetBinIndex(quantiles[i]);
			double quantile = quantiles[i - 1];
			double quantile2 = quantiles[i];
			double quantile2AboveQuantile = quantile2 - quantile;
			for (int j = binIndex2; j <= binIndex; j++)
			{
				double binMinimum = BinMinimum(j);
				double binMinimumPlusBinWidth = binMinimum + _binWidth;
				double binMinimumQuantileTerm = Math.Max(binMinimum, quantile);
				double quantile2Term = (Math.Min(binMinimumPlusBinWidth, quantile2) - binMinimumQuantileTerm) / quantile2AboveQuantile * surfaceAreaOverCountOffset;
				_bins[j] += quantile2Term;
			}
			binIndex2 = binIndex;
		}
	}

	public void AddHistogram(Histogram histogram)
	{
		for (int i = 0; i < histogram.BinCount; i++)
		{
			double area = histogram.BinArea[i];
			double binCenter = histogram.BinCenter(i);
			AddArea(binCenter, area);
		}
	}

	public double[] ComputeQuantiles(int quantilesCount)
	{
		if (_minimum == _maximum)
		{
			return new double[1] { _maximum };
		}
		quantilesCount++;
		double[] doubleValues = new double[quantilesCount];
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
		for (int i = 0; i < _bins.Length; i++)
		{
			binSum += _bins[i];
			double binSumOverArea2;
			for (binSumOverArea2 = binSum / area; computed <= binSumOverArea2; computed += quantilesCountTerm)
			{
				doubleValues[unity] = BinMinimum(i) + (computed - binSumOverArea) / (binSumOverArea2 - binSumOverArea) * _binWidth;
				unity++;
			}
			binSumOverArea = binSumOverArea2;
		}
		doubleValues[quantilesCount - 1] = _maximum;
		return doubleValues;
	}

	public PointCollection ComputeCumulativeValues()
	{
		PointCollection pointCollection = new PointCollection(_bins.Length);
		double xComponent = 0.0;
		for (int i = 0; i < _bins.Length; i++)
		{
			xComponent += _bins[i];
			pointCollection.Add(new Point(xComponent, BinMinimum(i)));
		}
		return pointCollection;
	}

	public double ComputeArea()
	{
		double binSum = 0.0;
		for (int i = 0; i < _bins.Length; i++)
		{
			binSum += _bins[i];
		}
		return binSum;
	}

	public double ComputeMean()
	{
		double computed = 0.0;
		double binWidthSum = _binWidth * 0.5;
		for (int i = 0; i <= _bins.Count() - 1; i++)
		{
			computed += binWidthSum * _bins[i];
			binWidthSum += _binWidth;
		}
		return computed / ComputeArea() + _minimum;
	}

	public double ComputeStandardDeviation()
	{
		double mean = ComputeMean();
		double computed = 0.0;
		for (int i = 0; i < _bins.Length; i++)
		{
			computed += Math.Pow(BinCenter(i) - mean, 2.0) * _bins[i];
		}
		return Math.Sqrt(computed / ComputeArea());
	}

	public int GetBinIndex(double doubleValue)
	{
		if (doubleValue <= _minimum)
		{
			return 0;
		}
		if (doubleValue >= _maximum)
		{
			return _bins.Length - 1;
		}
		return (int)((doubleValue - _minimum) / _binWidth);
	}

	public Histogram Resample(int newBinCount)
	{
		if (newBinCount == BinCount)
		{
			return Clone();
		}
		if (newBinCount > BinCount)
		{
			throw new ArgumentOutOfRangeException("newBinCount");
		}
		double[] doubleValues = new double[newBinCount];
		if (BinCount % newBinCount == 0)
		{
			int count = BinCount / newBinCount;
			for (int i = 0; i < newBinCount; i++)
			{
				int countTerm = i * count;
				for (int j = 0; j < count; j++)
				{
					doubleValues[i] += BinArea[countTerm + j];
				}
			}
			return new Histogram(Minimum, Maximum, doubleValues);
		}
		throw new NotImplementedException();
	}

	public Histogram Clone()
	{
		return new Histogram(Minimum, Maximum, BinArea.ToArray());
	}

	public static Histogram ComputeFrom(Memory<float> values, int binCt, double cellArea = 1.0, float noDataValue = -9999f)
	{
		Buffers.MaxMin(values.Span, noDataValue, out var max, out var min);
		if (!IsValidMinMax(min, max, out var errHisto))
		{
			return errHisto;
		}
		Histogram histogram = new Histogram(min, max, binCt);
		Span<float> floatSpan = values.Span;
		for (int i = 0; i < values.Length; i++)
		{
			float computed = floatSpan[i];
			if (computed != noDataValue)
			{
				histogram.AddArea(computed, cellArea);
			}
		}
		return histogram;
	}

	public static Histogram ComputeFrom(float[,] values, int binCt, double cellArea = 1.0, float noDataValue = -9999f)
	{
		float maxValue = float.MaxValue;
		float minValue = float.MinValue;
		for (int i = 0; i < values.GetLength(0); i++)
		{
			for (int j = 0; j < values.GetLength(1); j++)
			{
				float computed = values[i, j];
				if (computed != noDataValue)
				{
					maxValue = Math.Min(computed, maxValue);
					minValue = Math.Max(computed, minValue);
				}
			}
		}
		if (!IsValidMinMax(maxValue, minValue, out var errHisto))
		{
			return errHisto;
		}
		Histogram histogram = new Histogram(maxValue, minValue, binCt);
		for (int k = 0; k < values.GetLength(0); k++)
		{
			for (int l = 0; l < values.GetLength(1); l++)
			{
				float computed2 = values[k, l];
				if (computed2 != noDataValue)
				{
					histogram.AddArea(computed2, cellArea);
				}
			}
		}
		return histogram;
	}

	public static Histogram ComputeFrom(Memory<float>[] values, int binCt, double cellArea = 1.0, float noDataValue = -9999f)
	{
		float maxValue = float.MaxValue;
		float minValue = float.MinValue;
		foreach (Memory<float> floatSpan in values)
		{
			Buffers.MaxMin(floatSpan.Span, noDataValue, out var max, out var min);
			maxValue = Math.Min(min, maxValue);
			minValue = Math.Max(max, minValue);
		}
		if (!IsValidMinMax(maxValue, minValue, out var errHisto))
		{
			return errHisto;
		}
		Histogram histogram = new Histogram(maxValue, minValue, binCt);
		for (int j = 0; j < values.Length; j++)
		{
			Span<float> floatSpan2 = values[j].Span;
			for (int k = 0; k < floatSpan2.Length; k++)
			{
				float computed = floatSpan2[k];
				if (computed != noDataValue)
				{
					histogram.AddArea(computed, cellArea);
				}
			}
		}
		return histogram;
	}

	public static Histogram ComputeFrom(IRaster<float> raster, int binCt, float noDataValue = -9999f)
	{
		return ComputeFrom(raster.CopyData(), binCt, raster.RasterDefinition.CellArea, noDataValue);
	}

	public static Histogram ComputeFrom(Memory<float> data, RasterDefinition raster, Polygon pg, int binCt, float noDataValue = -9999f)
	{
		float min = float.MaxValue;
		float max = float.MinValue;
		PolygonRasterizer polygonRasterizer = new PolygonRasterizer(raster);
		polygonRasterizer.Rasterize(pg, (int c) =>
		{
			float computed = data.Span[c];
			if (computed != noDataValue)
			{
				min = Math.Min(computed, min);
				max = Math.Max(computed, max);
			}
		});
		if (!IsValidMinMax(min, max, out var errHisto))
		{
			return errHisto;
		}
		double cellArea = raster.CellArea;
		Histogram histo = new Histogram(min, max, binCt);
		polygonRasterizer.Replay((int c) =>
		{
			float computed2 = data.Span[c];
			if (computed2 != noDataValue)
			{
				histo.AddArea(computed2, cellArea);
			}
		});
		return histo;
	}

	public static Histogram ComputeFrom(ITileProvider<float> tileProvider, int binCt, float noDataValue = -9999f)
	{
		float maxValue = float.MaxValue;
		float minValue = float.MinValue;
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(tileProvider.BlockSize.PixelCount, out mem))
		{
			int blockCount = tileProvider.BlockCount();
			for (int i = 0; i < blockCount; i++)
			{
				tileProvider.ReadTile(i, mem);
				Buffers.MaxMin(mem.Span, noDataValue, out var max, out var min);
				maxValue = Math.Min(maxValue, min);
				minValue = Math.Max(minValue, max);
			}
			if (!IsValidMinMax(maxValue, minValue, out var errHisto))
			{
				return errHisto;
			}
			Histogram histo = new Histogram(maxValue, minValue, binCt);
			RasterOperations.ProcessTiles(tileProvider, tileProvider.RasterDefinition.Extent, clipTilesToDesiredExtent: true, (long tIdx, RasterDefinition rdef, Memory<float> buf) =>
			{
				Span<float> bufSpan = buf.Span;
				for (int j = 0; j < bufSpan.Length; j++)
				{
					float buf2 = bufSpan[j];
					if (buf2 != noDataValue)
					{
						histo.AddArea(buf2, tileProvider.RasterDefinition.CellArea);
					}
				}
			});
			return histo;
		}
	}

	public static Histogram ComputeFrom(IResample<float> raster, double cellSize, int binCt, InterpolationMode interpMode = InterpolationMode.Nearest)
	{
		ResampleArgs args = new ResampleArgs(new RasterDefinition(raster.ValidExtent, cellSize), new SamplingParams(interpMode, initBufferToNDV: true));
		return ComputeFrom(raster.Resample(args), binCt, cellSize, raster.NoDataValue.GetValueOrDefault());
	}

	public static Histogram ComputeTiled(IResample<float> resampler, int pixelCount, int binCount)
	{
		if (resampler.ValidExtent.IsNull())
		{
			return NullHistogram();
		}
		RasterDefinition rasterDef = RasterDefinition.ComputeBoundingRaster(resampler.ValidExtent, pixelCount);
		IRaster<float> raster = resampler.AsRaster(rasterDef);
		float runningMax = float.MinValue;
		float runningMin = float.MaxValue;
		RasterOperations.ProcessTiled(raster, resampler.ValidExtent, clipTilesToDesiredExtent: true, (RasterDefinition rdef, Memory<float> buf) =>
		{
			float max;
			float min;
			if (resampler.NoDataValue.HasValue)
			{
				Buffers.MaxMin(buf.Span, resampler.NoDataValue.Value, out max, out min);
			}
			else
			{
				Buffers.MaxMin(buf.Span, out max, out min);
			}
			runningMax = Math.Max(runningMax, max);
			runningMin = Math.Min(runningMin, min);
		});
		if (!IsValidMinMax(runningMin, runningMax, out var errHisto))
		{
			return errHisto;
		}
		Histogram histo = new Histogram(runningMin, runningMax, binCount);
		float ndv = float.NaN;
		if (resampler.NoDataValue.HasValue)
		{
			ndv = resampler.NoDataValue.Value;
		}
		RasterOperations.ProcessTiled(raster, resampler.ValidExtent, clipTilesToDesiredExtent: true, (RasterDefinition rdef, Memory<float> mem) =>
		{
			Span<float> memSpan = mem.Span;
			for (int i = 0; i < mem.Length; i++)
			{
				float mem2 = memSpan[i];
				if (mem2 != ndv)
				{
					histo.AddArea(mem2, 1.0);
				}
			}
		});
		return histo;
	}

	public static Histogram MergeHistograms(IList<Histogram> histograms)
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		foreach (Histogram histogram2 in histograms)
		{
			maxValue = Math.Min(histogram2.Minimum, maxValue);
			minValue = Math.Max(histogram2.Maximum, minValue);
		}
		Histogram histogram = new Histogram(maxValue, minValue);
		foreach (Histogram histogram3 in histograms)
		{
			histogram.AddHistogram(histogram3);
		}
		return histogram;
	}

	public static bool IsValidMinMax(float min, float max, out Histogram errHisto)
	{
		if (min > max)
		{
			errHisto = NullHistogram();
			return false;
		}
		if (min == max)
		{
			errHisto = SingleValue(min);
			return false;
		}
		if (float.IsNaN(min) || float.IsNaN(max) || float.IsInfinity(min) || float.IsInfinity(max))
		{
			errHisto = NullHistogram();
			return false;
		}
		errHisto = null;
		return true;
	}

	public string DebugDisplay(bool includeBinCenterText = true, int maxStringWidth = 16)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Min: " + Minimum);
		double binAreaTerm = BinArea.Max();
		for (int i = 0; i < BinCount; i++)
		{
			double a = BinArea[i] / binAreaTerm * (double)maxStringWidth;
			string padRightSum = string.Empty;
			if (includeBinCenterText)
			{
				padRightSum += BinCenter(i).ToString("G6").PadRight(11);
			}
			int ordinal = (int)Math.Round(a);
			if (ordinal >= 1)
			{
				stringBuilder.AppendLine(padRightSum + new string(Enumerable.Repeat('o', ordinal).ToArray()));
			}
			else
			{
				stringBuilder.AppendLine(padRightSum + ".");
			}
		}
		stringBuilder.AppendLine("Max: " + Maximum);
		return stringBuilder.ToString();
	}
}

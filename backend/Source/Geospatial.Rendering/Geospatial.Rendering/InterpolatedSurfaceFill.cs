using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Rendering;

public class InterpolatedSurfaceFill : ColorPalette, IMutableSpatialVersion, ISpatialVersion
{
	private static readonly ILogger _logger = LogHelper.GetLogger("InterpolatedSurfaceFill");

	private long _versionXY;

	public static int ColorMode = 0;

	private readonly float[] _bandReciprocals;

	private readonly float[] _bandMinValues;

	private readonly float[] _bandMaxValues;

	private readonly byte[] _colorsR;

	private readonly byte[] _colorsG;

	private readonly byte[] _colorsB;

	private readonly byte[] _colorsA;

	private const int NoDataColor = 0;

	public static int PaintChunkSize = 65536;

	public bool RenderZeroAsNoData { get; set; }

	public bool Discrete { get; set; }

	public bool RegenerateForScreen { get; internal set; }

	public double[] ColorValues { get; }

	public double MinValue => ColorValues[0];

	public double MaxValue => ColorValues[^1];

	public bool IsStretchedRamp { get; private set; }

	public long VersionXY => _versionXY;

	public void FreezeOne()
	{
		throw new NotImplementedException();
	}

	public void ThawOne()
	{
		throw new NotImplementedException();
	}

	public static InterpolatedSurfaceFill DefaultSurfaceFill(string name = "")
	{
		return new InterpolatedSurfaceFill(new Color[2]
		{
			Color.White,
			Color.Black
		}, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 100.0 }), isStretched: true, regenForScreen: false, name);
	}

	public static Histogram EstimateHistogramFrom(IResample<float> raster)
	{
		Extent validExtent = raster.ValidExtent;
		if (!validExtent.IsFiniteAndValid())
		{
			throw new ArgumentException("Input raster spans some infinite range, must call another InterpolatedSurfaceFill method to provide a range.");
		}
		double sqrtSqrtArea = Math.Sqrt(Math.Sqrt(validExtent.Area));
		int binCt = 1024;
		InterpolationMode interpMode = InterpolationMode.Nearest;
		float noDataValue = -9999f;
		return Histogram.ComputeFrom(raster.Resample(new ResampleArgs(new RasterDefinition(validExtent, sqrtSqrtArea), interpMode)), binCt, sqrtSqrtArea, noDataValue);
	}

	public static IReadOnlyList<double> EstimateColorValuesFrom(IResample<float> raster, int colorCount)
	{
		return MakeValuesArray(EstimateHistogramFrom(raster), colorCount);
	}

	public static IReadOnlyList<double> EstimateColorValuesFrom(IResample<float> raster, Extent extent, int colorCount)
	{
		return MakeValuesArray(EstimateHistogramFrom(raster, extent), colorCount);
	}

	public static Histogram EstimateHistogramFrom(IResample<float> raster, Extent extent)
	{
		if (extent.IsInfiniteOrInvalid())
		{
			return Histogram.NullHistogram();
		}
		extent = extent.Intersection(raster.ValidExtent);
		if (extent.IsInfiniteOrInvalid())
		{
			return Histogram.NullHistogram();
		}
		double sqrtScaledArea = Math.Sqrt(extent.Area / 4000.0);
		int binCt = 1024;
		InterpolationMode interpMode = InterpolationMode.Nearest;
		float noDataValue = raster.NoDataValue ?? (-9999f);
		return Histogram.ComputeFrom(raster.Resample(new ResampleArgs(new RasterDefinition(extent, sqrtScaledArea), interpMode)), binCt, sqrtScaledArea, noDataValue);
	}

	public static InterpolatedSurfaceFill EstimateFrom(IResample<float> raster, IEnumerable<Color> colors, string name = "")
	{
		Histogram histogram = EstimateHistogramFrom(raster);
		return new InterpolatedSurfaceFill(colors, histogram, isStretched: true, regenForScreen: false, name);
	}

	public static InterpolatedSurfaceFill EstimateFrom(ITimeSeriesResampler<float> data, TimeSpan resampleFrequency, Extent resampleExtent, IEnumerable<Color> colors)
	{
		DateTime dateTime = data.Times.FirstOrDefault();
		DateTime dateTime2 = data.Times.LastOrDefault();
		if (dateTime == DateTime.MinValue || dateTime2 == DateTime.MaxValue)
		{
			throw new ArgumentException("data");
		}
		List<DateTime> dateTimes = new List<DateTime>();
		DateTime dateTime3 = dateTime;
		do
		{
			dateTimes.Add(dateTime3);
			dateTime3 += resampleFrequency;
		}
		while (!(dateTime3 > dateTime2));
		return EstimateFrom(data, dateTimes, resampleExtent, colors);
	}

	public static InterpolatedSurfaceFill EstimateFrom(ITimeSeriesResampler<float> data, IList<DateTime> resampleTimesteps, Extent resampleExtent, IEnumerable<Color> colors)
	{
		Extent targetExtent = resampleExtent;
		double sqrtScaledArea = Math.Sqrt(targetExtent.Area / 10000.0);
		int binCt = 1024;
		InterpolationMode interpMode = InterpolationMode.Nearest;
		float noDataValue = -9999f;
		RasterDefinition queryRaster = new RasterDefinition(targetExtent, sqrtScaledArea);
		Memory<float>[] floatSpanArray = new Memory<float>[resampleTimesteps.Count];
		ResampleArgs args = new ResampleArgs(queryRaster, interpMode);
		for (int i = 0; i < resampleTimesteps.Count; i++)
		{
			DateTime dt = resampleTimesteps[i];
			floatSpanArray[i] = data.Resample(args, dt);
		}
		Histogram histogram = Histogram.ComputeFrom(floatSpanArray, binCt, sqrtScaledArea, noDataValue);
		return new InterpolatedSurfaceFill(colors, histogram);
	}

	public static Histogram EstimateHistogramFrom(IMultiBandResampler<float> data, IList<int> bandIndexes, Extent resampleExtent)
	{
		Extent targetExtent = resampleExtent;
		double sqrtScaledArea = Math.Sqrt(targetExtent.Area / 10000.0);
		int binCt = 1024;
		InterpolationMode interpMode = InterpolationMode.Nearest;
		float noDataValue = -9999f;
		RasterDefinition queryRaster = new RasterDefinition(targetExtent, sqrtScaledArea);
		Memory<float>[] floatSpanArray = new Memory<float>[bandIndexes.Count];
		ResampleArgs args = new ResampleArgs(queryRaster, interpMode);
		for (int i = 0; i < bandIndexes.Count; i++)
		{
			int profileIdx = bandIndexes[i];
			floatSpanArray[i] = data.Resample(args, profileIdx);
		}
		return Histogram.ComputeFrom(floatSpanArray, binCt, sqrtScaledArea, noDataValue);
	}

	public static IReadOnlyList<double> EstimateColorValuesFrom(IMultiBandResampler<float> data, IList<int> bandIndexes, Extent resampleExtent, int colorCount)
	{
		return MakeValuesArray(EstimateHistogramFrom(data, bandIndexes, resampleExtent), colorCount);
	}

	public static InterpolatedSurfaceFill EstimateFrom(IMultiBandResampler<float> data, IList<int> bandIndexes, Extent resampleExtent, IEnumerable<Color> colors, string name = "")
	{
		Histogram histogram = EstimateHistogramFrom(data, bandIndexes, resampleExtent);
		return new InterpolatedSurfaceFill(colors, histogram, isStretched: true, regenForScreen: false, name);
	}

	private static List<int> GetEstimationBandIndices(IMultiBandResampler<float> data, double fractionOfProfiles)
	{
		List<int> intList = new List<int>();
		int profileCount = data.ProfileCount;
		double profileCountTimesFractionOfProfiles = (double)profileCount * fractionOfProfiles;
		int profileCountTerm = (int)double.Round((double)profileCount / profileCountTimesFractionOfProfiles);
		if (profileCount == 0)
		{
			return null;
		}
		for (int i = 0; i < profileCount; i += profileCountTerm)
		{
			intList.Add(i);
		}
		if (intList.Last() != profileCount - 1)
		{
			intList.Add(profileCount - 1);
		}
		return intList;
	}

	public static IReadOnlyList<double> EstimateColorValuesFrom(IMultiBandResampler<float> data, double fractionOfProfiles, Extent resampleExtent, int colorCount)
	{
		List<int> estimationBandIndices = GetEstimationBandIndices(data, fractionOfProfiles);
		if (estimationBandIndices == null)
		{
			return new double[colorCount];
		}
		return EstimateColorValuesFrom(data, estimationBandIndices, resampleExtent, colorCount);
	}

	public static InterpolatedSurfaceFill EstimateFrom(IMultiBandResampler<float> data, double fractionOfProfiles, Extent resampleExtent, IEnumerable<Color> colors, string name = "")
	{
		List<int> estimationBandIndices = GetEstimationBandIndices(data, fractionOfProfiles);
		if (estimationBandIndices == null)
		{
			return DefaultSurfaceFill();
		}
		return EstimateFrom(data, estimationBandIndices, resampleExtent, colors, name);
	}

	public static InterpolatedSurfaceFill EstimateLinearFrom(IResample<float> raster, Extent resampleExtent, IEnumerable<Color> colors, float minRelRange = 0.001f)
	{
		List<Color> colorsList = colors.ToList();
		double cellSize = Math.Sqrt(resampleExtent.Area / 10000.0);
		float computed = -9999f;
		float[] resample = raster.Resample(new ResampleArgs(new RasterDefinition(resampleExtent, cellSize), InterpolationMode.Nearest));
		float maxValue = float.MaxValue;
		float minValue = float.MinValue;
		for (int i = 0; i < resample.Length; i++)
		{
			if (resample[i] != computed)
			{
				maxValue = Math.Min(maxValue, resample[i]);
				minValue = Math.Max(minValue, resample[i]);
			}
		}
		if (maxValue > minValue)
		{
			maxValue = 0f;
			minValue = 1f;
		}
		float maxValueMinValueTerm = 0.5f * (maxValue + minValue);
		float minValueAboveMaxValue = minValue - maxValue;
		float minRelRangeTerm = minRelRange * Math.Max(1E-05f, Math.Abs(maxValueMinValueTerm));
		if (minValueAboveMaxValue < minRelRangeTerm)
		{
			maxValue = maxValueMinValueTerm - 0.5f * minRelRangeTerm;
			minValue = maxValueMinValueTerm + 0.5f * minRelRangeTerm;
		}
		List<double> doubleList = new List<double>();
		for (int j = 0; j < colorsList.Count; j++)
		{
			doubleList.Add(maxValue + (float)j / (float)(colorsList.Count - 1) * (minValue - maxValue));
		}
		return new InterpolatedSurfaceFill(colorsList, doubleList);
	}

	public InterpolatedSurfaceFill(ColorPalette palette, IEnumerable<double> values, bool isStretched = true, bool regenForScreen = false, string name = "")
		: this(palette.Colors, values, isStretched, regenForScreen, name)
	{
	}

	public InterpolatedSurfaceFill(IEnumerable<Color> colors, IEnumerable<double> values, bool isStretched = true, bool regenForScreen = false, string name = "")
		: base(colors.ToList(), name)
	{
		if (values == null)
		{
			throw new ArgumentNullException("values");
		}
		IsStretchedRamp = isStretched;
		if (values.Count() == Colors.Count)
		{
			ColorValues = values.ToArray();
		}
		else
		{
			Histogram histogram = new Histogram(values.Min(), values.Max());
			histogram.AddQuantiles(1.0, values.ToList());
			ColorValues = MakeValuesArray(histogram, colors.Count());
		}
		RegenerateForScreen = regenForScreen;
		int count = Count;
		_bandReciprocals = new float[count];
		_bandMinValues = new float[count];
		_bandMaxValues = new float[count];
		_colorsR = new byte[count];
		_colorsG = new byte[count];
		_colorsB = new byte[count];
		_colorsA = new byte[count];
		PrecomputeFastColorData();
	}

	public InterpolatedSurfaceFill(ColorPalette palette, Histogram histogram, bool isStretched = true, bool regenForScreen = false, string name = "")
		: this(palette.Colors, histogram, isStretched, regenForScreen, name)
	{
	}

	public InterpolatedSurfaceFill(IEnumerable<Color> colors, Histogram histogram, bool isStretched = true, bool regenForScreen = false, string name = "")
		: base(colors.ToList(), name)
	{
		if (histogram == null)
		{
			throw new ArgumentNullException("histogram");
		}
		IsStretchedRamp = isStretched;
		RegenerateForScreen = regenForScreen;
		ColorValues = MakeValuesArray(histogram, colors.Count());
		if (ColorValues.Length == 1)
		{
			Colors = new List<Color> { colors.First() };
		}
		else
		{
			Colors = colors.ToList();
		}
		int count = Count;
		_bandReciprocals = new float[count];
		_bandMinValues = new float[count];
		_bandMaxValues = new float[count];
		_colorsR = new byte[count];
		_colorsG = new byte[count];
		_colorsB = new byte[count];
		_colorsA = new byte[count];
		PrecomputeFastColorData();
	}

	private static double[] MakeValuesArray(Histogram histogram, int clrCount)
	{
		if (histogram.IsNull())
		{
			if (clrCount <= 1)
			{
				return new double[1];
			}
			double[] doubleValues = new double[clrCount];
			for (int i = 0; i < clrCount; i++)
			{
				doubleValues[i] = (double)i / (double)(clrCount - 1);
			}
			return doubleValues;
		}
		return histogram.ComputeQuantiles(clrCount - 1);
	}

	private void PrecomputeFastColorData()
	{
		int count = Count;
		for (int i = 0; i < count; i++)
		{
			Color color = Colors[i];
			_colorsR[i] = color.R;
			_colorsG[i] = color.G;
			_colorsB[i] = color.B;
			_colorsA[i] = color.A;
		}
		for (int j = 1; j < count; j++)
		{
			double colorValueStep = ColorValues[j] - ColorValues[j - 1];
			_bandReciprocals[j] = ((colorValueStep > 0.0) ? ((float)(1.0 / colorValueStep)) : 0f);
			_bandMinValues[j] = (float)ColorValues[j - 1];
			_bandMaxValues[j] = (float)ColorValues[j];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int ColorARGBFast(float computed, float alphaScale)
	{
		float[] bandMaxValues = _bandMaxValues;
		float[] bandMinValues = _bandMinValues;
		int bandMaxValueLength = bandMaxValues.Length;
		byte[] colorsR = _colorsR;
		byte[] colorsG = _colorsG;
		byte[] colorsB = _colorsB;
		byte[] colorsA = _colorsA;
		if (bandMaxValueLength == 1)
		{
			return ((int)(alphaScale * (float)(int)colorsA[0] + 0.5f) << 24) | (colorsR[0] << 16) | (colorsG[0] << 8) | colorsB[0];
		}
		if (computed <= bandMinValues[1])
		{
			return ((int)(alphaScale * (float)(int)colorsA[0] + 0.5f) << 24) | (colorsR[0] << 16) | (colorsG[0] << 8) | colorsB[0];
		}
		float[] bandReciprocals = _bandReciprocals;
		for (int i = 1; i < bandMaxValueLength; i++)
		{
			if (bandMaxValues[i] >= computed)
			{
				float bandMinValuesBandReciprocalsTerm = (computed - bandMinValues[i]) * bandReciprocals[i];
				float computed2 = 1f - bandMinValuesBandReciprocalsTerm;
				int ordinal = i - 1;
				int colorsRTerm = (int)((float)(int)colorsR[ordinal] * computed2 + (float)(int)colorsR[i] * bandMinValuesBandReciprocalsTerm);
				int colorsGTerm = (int)((float)(int)colorsG[ordinal] * computed2 + (float)(int)colorsG[i] * bandMinValuesBandReciprocalsTerm);
				int colorsBTerm = (int)((float)(int)colorsB[ordinal] * computed2 + (float)(int)colorsB[i] * bandMinValuesBandReciprocalsTerm);
				return ((int)(alphaScale * ((float)(int)colorsA[ordinal] * computed2 + (float)(int)colorsA[i] * bandMinValuesBandReciprocalsTerm) + 0.5f) << 24) | (colorsRTerm << 16) | (colorsGTerm << 8) | colorsBTerm;
			}
		}
		int bandMaxValueLengthOffset = bandMaxValueLength - 1;
		return ((int)(alphaScale * (float)(int)colorsA[bandMaxValueLengthOffset] + 0.5f) << 24) | (colorsR[bandMaxValueLengthOffset] << 16) | (colorsG[bandMaxValueLengthOffset] << 8) | colorsB[bandMaxValueLengthOffset];
	}

	public int ColorARGB(double doubleValue, byte alpha = byte.MaxValue)
	{
		if (doubleValue <= ColorValues[0])
		{
			Color color = Colors[0];
			return Color.FromArgb((byte)Math.Round((double)(int)alpha / 255.0 * (double)(int)color.A), color.R, color.G, color.B).ToArgb();
		}
		for (int i = 1; i < Count; i++)
		{
			if (ColorValues[i] >= doubleValue)
			{
				double colorValue = ColorValues[i];
				double colorValue2 = ColorValues[i - 1];
				float submergenceRatio = (float)((doubleValue - colorValue2) / (colorValue - colorValue2));
				float computed = 1f - submergenceRatio;
				_ = ColorValues[i - 1];
				_ = ColorValues[i];
				Color color2 = Colors[i - 1];
				Color color3 = Colors[i];
				return Color.FromArgb((byte)Math.Round((double)(int)alpha / 255.0 * (double)((float)(int)color2.A * computed + (float)(int)color3.A * submergenceRatio)), (byte)((float)(int)color2.R * computed + (float)(int)color3.R * submergenceRatio), (byte)((float)(int)color2.G * computed + (float)(int)color3.G * submergenceRatio), (byte)((float)(int)color2.B * computed + (float)(int)color3.B * submergenceRatio)).ToArgb();
			}
		}
		List<Color> colors = Colors;
		Color color4 = colors[colors.Count - 1];
		return Color.FromArgb((byte)Math.Round((double)(int)alpha / 255.0 * (double)(int)color4.A), color4.R, color4.G, color4.B).ToArgb();
	}

	public void Paint(Memory<float> values, Memory<int> outputColorBuf, float nodata, byte alpha = byte.MaxValue)
	{
		if (RegenerateForScreen)
		{
			Histogram histogram = Histogram.ComputeFrom(values, ColorValues.Length - 1);
			InterpolatedSurfaceFill interpolatedSurfaceFill = new InterpolatedSurfaceFill(Colors, histogram, IsStretchedRamp);
			interpolatedSurfaceFill.Discrete = Discrete;
			interpolatedSurfaceFill.Paint(values, outputColorBuf, nodata, alpha);
			return;
		}
		if (Discrete)
		{
			float alphaScale = (float)(int)alpha / 255f;
			if (RenderZeroAsNoData)
			{
				Parallel.SmartFor(values.Length, (int start, int finish) =>
				{
					PaintLoop_Discrete_ZeroNDV(values.Span, outputColorBuf.Span, nodata, alphaScale, start, finish);
				}, PaintChunkSize);
			}
			else
			{
				Parallel.SmartFor(values.Length, (int start, int finish) =>
				{
					PaintLoop_Discrete(values.Span, outputColorBuf.Span, nodata, alphaScale, start, finish);
				}, PaintChunkSize);
			}
			return;
		}
		float alphaScale2 = (float)(int)alpha / 255f;
		int colorMode = ColorMode;
		if (RenderZeroAsNoData)
		{
			if (colorMode == 0)
			{
				Parallel.SmartFor(values.Length, (int start, int finish) =>
				{
					PaintLoop_Fast_ZeroNDV(values.Span, outputColorBuf.Span, nodata, alphaScale2, start, finish);
				}, PaintChunkSize);
			}
			else
			{
				Parallel.SmartFor(values.Length, (int start, int finish) =>
				{
					PaintLoop_Original_ZeroNDV(values.Span, outputColorBuf.Span, nodata, start, finish);
				}, PaintChunkSize);
			}
		}
		else if (colorMode == 0)
		{
			Parallel.SmartFor(values.Length, (int start, int finish) =>
			{
				PaintLoop_Fast(values.Span, outputColorBuf.Span, nodata, alphaScale2, start, finish);
			}, PaintChunkSize);
		}
		else
		{
			Parallel.SmartFor(values.Length, (int start, int finish) =>
			{
				PaintLoop_Original(values.Span, outputColorBuf.Span, nodata, start, finish);
			}, PaintChunkSize);
		}
	}

	private void PaintLoop_Fast(Span<float> valueSpan, Span<int> outputSpan, float nodata, float alphaScale, int start, int finish)
	{
		for (int i = start; i <= finish; i++)
		{
			float valueSpanTerm = valueSpan[i];
			if (valueSpanTerm != nodata)
			{
				outputSpan[i] = ColorARGBFast(valueSpanTerm, alphaScale);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	private void PaintLoop_Original(Span<float> valueSpan, Span<int> outputSpan, float nodata, int start, int finish)
	{
		for (int i = start; i <= finish; i++)
		{
			float valueSpanTerm = valueSpan[i];
			if (valueSpanTerm != nodata)
			{
				outputSpan[i] = ColorARGB(valueSpanTerm);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	private void PaintLoop_Fast_ZeroNDV(Span<float> valueSpan, Span<int> outputSpan, float nodata, float alphaScale, int start, int finish)
	{
		for (int i = start; i <= finish; i++)
		{
			float valueSpanTerm = valueSpan[i];
			if (valueSpanTerm != nodata && valueSpanTerm != 0f)
			{
				outputSpan[i] = ColorARGBFast(valueSpanTerm, alphaScale);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	private void PaintLoop_Original_ZeroNDV(Span<float> valueSpan, Span<int> outputSpan, float nodata, int start, int finish)
	{
		for (int i = start; i <= finish; i++)
		{
			float valueSpanTerm = valueSpan[i];
			if (valueSpanTerm != nodata && valueSpanTerm != 0f)
			{
				outputSpan[i] = ColorARGB(valueSpanTerm);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int ColorARGBDiscrete(float computed, float alphaScale)
	{
		byte[] colorsR = _colorsR;
		byte[] colorsG = _colorsG;
		byte[] colorsB = _colorsB;
		byte[] colorsA = _colorsA;
		int colorsRLength = colorsR.Length;
		if (colorsRLength == 1)
		{
			return PackDiscrete(0, colorsR, colorsG, colorsB, colorsA, alphaScale);
		}
		float[] bandMaxValues = _bandMaxValues;
		for (int i = 1; i < colorsRLength; i++)
		{
			if (bandMaxValues[i] > computed)
			{
				return PackDiscrete(i - 1, colorsR, colorsG, colorsB, colorsA, alphaScale);
			}
		}
		return PackDiscrete(colorsRLength - 1, colorsR, colorsG, colorsB, colorsA, alphaScale);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int PackDiscrete(int idx, byte[] cR, byte[] cG, byte[] cB, byte[] cA, float alphaScale)
	{
		return ((int)(alphaScale * (float)(int)cA[idx] + 0.5f) << 24) | (cR[idx] << 16) | (cG[idx] << 8) | cB[idx];
	}

	private void PaintLoop_Discrete(Span<float> valueSpan, Span<int> outputSpan, float nodata, float alphaScale, int start, int finish)
	{
		for (int i = start; i <= finish; i++)
		{
			float valueSpanTerm = valueSpan[i];
			if (valueSpanTerm != nodata)
			{
				outputSpan[i] = ColorARGBDiscrete(valueSpanTerm, alphaScale);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	private void PaintLoop_Discrete_ZeroNDV(Span<float> valueSpan, Span<int> outputSpan, float nodata, float alphaScale, int start, int finish)
	{
		for (int i = start; i <= finish; i++)
		{
			float valueSpanTerm = valueSpan[i];
			if (valueSpanTerm != nodata && valueSpanTerm != 0f)
			{
				outputSpan[i] = ColorARGBDiscrete(valueSpanTerm, alphaScale);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	private void P4PaintHelper_RenderZeroAsNoData(Memory<double> values, Memory<int> output, double nodata, int start, int finish)
	{
		Span<double> doubleSpan = values.Span;
		Span<int> outputSpan = output.Span;
		for (int i = start; i <= finish; i++)
		{
			double computed = doubleSpan[i];
			if (computed != nodata && computed != 0.0)
			{
				outputSpan[i] = ColorARGB(computed);
			}
			else
			{
				outputSpan[i] = 0;
			}
		}
	}

	public InterpolatedSurfaceFill Clone(string nameOverride = null)
	{
		return new InterpolatedSurfaceFill(Colors, ColorValues, IsStretchedRamp, RegenerateForScreen, nameOverride ?? Name)
		{
			Discrete = Discrete,
			RenderZeroAsNoData = RenderZeroAsNoData
		};
	}

	public InterpolatedSurfaceFill Clone(IEnumerable<Color> newColors, IEnumerable<double> newValues, string name = "")
	{
		return new InterpolatedSurfaceFill(newColors, newValues, IsStretchedRamp, RegenerateForScreen, name);
	}

	public InterpolatedSurfaceFill CloneDesaturated(float fractionOfOriginalColor)
	{
		Color[] colors = Colors.ToArray();
		for (int i = 0; i < colors.Length; i++)
		{
			colors[i] = ColorMath.DesaturateColor(colors[i], fractionOfOriginalColor);
		}
		return new InterpolatedSurfaceFill(colors, ColorValues, IsStretchedRamp, RegenerateForScreen);
	}

	public void ForceSetVersion(long longValue)
	{
		Interlocked.Exchange(ref _versionXY, longValue);
	}

	public override string ToString()
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < Count; i++)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 2, stringBuilder2);
			handler.AppendLiteral("Color: ");
			handler.AppendFormatted(Colors[i].ToArgb(), "X8");
			handler.AppendLiteral(", Value: ");
			handler.AppendFormatted(ColorValues[i], "F2");
			stringBuilder2.AppendLine(ref handler);
		}
		return stringBuilder.ToString();
	}
}

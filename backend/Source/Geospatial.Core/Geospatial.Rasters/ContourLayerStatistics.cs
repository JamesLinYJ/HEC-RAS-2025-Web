using System;
using System.Buffers;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Rasters;

public static class ContourLayerStatistics
{
	public const double MinReferenceSlope = 0.002;

	public const double MaxReferenceSlope = 0.35;

	private static readonly int[] OverviewDimensions = new int[5] { 192, 384, 768, 1536, 3072 };

	private const int MaxGradientSamples = 262144;

	public static ContourLayerStats Compute(IResample<float> resampler)
	{
		if (resampler == null)
		{
			return ContourLayerStats.None;
		}
		Extent validExtent = resampler.ValidExtent;
		if (!validExtent.IsFiniteAndValid())
		{
			return ContourLayerStats.None;
		}
		double[] doubleValues = new double[OverviewDimensions.Length];
		double[] doubleValuesArray = new double[OverviewDimensions.Length];
		int ordinal = 0;
		double referenceSlope = double.NaN;
		double valueMin = double.NaN;
		double valueMax = double.NaN;
		bool condition = false;
		for (int i = 0; i < OverviewDimensions.Length; i++)
		{
			int maxDimension = OverviewDimensions[i];
			if (TryMeasureOverview(resampler, validExtent, maxDimension, out var cellSize, out var medianGradient, out var activeMedianGradient, out var min, out var max))
			{
				condition = true;
				valueMin = min;
				valueMax = max;
				if (activeMedianGradient > 0.0)
				{
					doubleValues[ordinal] = cellSize;
					doubleValuesArray[ordinal] = activeMedianGradient;
					ordinal++;
				}
				if (i == 0 && medianGradient > 0.0)
				{
					double medianGradientTerm = Math.Clamp(medianGradient, 0.002, 0.35);
					double niceInterval = ContourLevelSet.NiceInterval(medianGradientTerm);
					double niceIntervalAtMost = ContourLevelSet.NiceIntervalAtMost(medianGradientTerm);
					referenceSlope = ((medianGradientTerm / niceIntervalAtMost <= niceInterval / medianGradientTerm) ? niceIntervalAtMost : niceInterval);
				}
			}
		}
		if (!condition)
		{
			return ContourLayerStats.None;
		}
		if (ordinal < doubleValues.Length)
		{
			Array.Resize(ref doubleValues, ordinal);
			Array.Resize(ref doubleValuesArray, ordinal);
		}
		return new ContourLayerStats(referenceSlope, valueMin, valueMax, doubleValues, doubleValuesArray);
	}

	public static double GetReferenceSlope(in ContourLayerStats stats, double renderCellSize)
	{
		double[] scaleCellSizes = stats.ScaleCellSizes;
		double[] scaleSlopes = stats.ScaleSlopes;
		if (scaleCellSizes == null || scaleCellSizes.Length < 2 || !(renderCellSize > 0.0))
		{
			return stats.ReferenceSlope;
		}
		if (renderCellSize >= scaleCellSizes[0])
		{
			return Math.Clamp(scaleSlopes[0], 0.002, 0.35);
		}
		int i;
		for (i = 0; i < scaleCellSizes.Length - 2 && renderCellSize < scaleCellSizes[i + 1]; i++)
		{
		}
		return Math.Clamp(MathHelpers.InterpolateLogLog(scaleCellSizes[i], scaleSlopes[i], scaleCellSizes[i + 1], scaleSlopes[i + 1], renderCellSize), 0.002, 0.35);
	}

	private static bool TryMeasureOverview(IResample<float> resampler, Extent extent, int maxDimension, out double cellSize, out double medianGradient, out double activeMedianGradient, out float min, out float max)
	{
		medianGradient = double.NaN;
		activeMedianGradient = double.NaN;
		min = 0f;
		max = 0f;
		cellSize = Math.Max(extent.Width, extent.Height) / (double)maxDimension;
		if (!(cellSize > 0.0))
		{
			return false;
		}
		RasterDefinition queryRaster = new RasterDefinition(extent, cellSize);
		int cellCount = queryRaster.CellCount;
		if (cellCount < 4)
		{
			return false;
		}
		float noDataValue = resampler.NoDataValue ?? (-9999f);
		Memory<float> mem;
		using (SharedMemoryPool<float>.Rent(cellCount, noDataValue, out mem))
		{
			resampler.Resample(new ResampleArgs(queryRaster, InterpolationMode.Linear), mem);
			Span<float> a = mem.Span.Slice(0, cellCount);
			Buffers.MaxMin(a, noDataValue, out max, out min);
			if (max < min)
			{
				return false;
			}
			int rows = queryRaster.Rows;
			int cols = queryRaster.Cols;
			int sqrtScaledCellCount = Math.Max(1, (int)Math.Sqrt((double)cellCount / 262144.0));
			float[] rent = ArrayPool<float>.Shared.Rent(cellCount / (sqrtScaledCellCount * sqrtScaledCellCount) + rows + cols);
			try
			{
				int ordinal = 0;
				for (int i = 0; i < rows - 1; i += sqrtScaledCellCount)
				{
					int colsTerm = i * cols;
					for (int j = 0; j < cols - 1; j += sqrtScaledCellCount)
					{
						float computed = a[colsTerm + j];
						float computed2 = a[colsTerm + j + 1];
						float colsTerm2 = a[colsTerm + cols + j];
						if (computed != noDataValue && computed2 != noDataValue && colsTerm2 != noDataValue)
						{
							float cellSizeTerm = (float)((double)(computed2 - computed) / cellSize);
							float cellSizeTerm2 = (float)((double)(colsTerm2 - computed) / cellSize);
							rent[ordinal++] = MathF.Sqrt(cellSizeTerm * cellSizeTerm + cellSizeTerm2 * cellSizeTerm2);
						}
					}
				}
				if (ordinal > 0)
				{
					Array.Sort(rent, 0, ordinal);
					medianGradient = rent[ordinal / 2];
					float rent2 = rent[Math.Min(ordinal - 1, (int)((double)ordinal * 0.9))];
					float scaledRent2 = 0.05f * rent2;
					int k;
					for (k = 0; k < ordinal && rent[k] <= scaledRent2; k++)
					{
					}
					activeMedianGradient = ((k < ordinal) ? ((double)rent[k + (ordinal - k) / 2]) : medianGradient);
				}
			}
			finally
			{
				ArrayPool<float>.Shared.Return(rent);
			}
			return true;
		}
	}
}

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Rasters.Convolution;
using Geospatial.Rendering.Systems;
using Geospatial.Rendering.Systems.Vector;
using Geospatial.Vectors;
using Utility;
using Utility.Memory;

namespace Geospatial.Rendering.Layers;

public static class ContourRenderHelper
{
	private sealed class TileScratch
	{
		public readonly List<ContourLevel> Levels = new List<ContourLevel>();

		public readonly List<ContourLine> Minors = new List<ContourLine>();

		public readonly List<ContourLine> Majors = new List<ContourLine>();

		public double DpEpsilon;

		public double RingCullLength;

		public double StubCullLength;

		public Extent TileExtent;

		public readonly List<Geospatial.Vectors.Point> Simplified = new List<Geospatial.Vectors.Point>();

		public readonly List<Geospatial.Vectors.Point> Smoothed = new List<Geospatial.Vectors.Point>();

		public readonly List<FeatureSegmentSubset> Candidates = new List<FeatureSegmentSubset>();

		public readonly List<ContourLine> DrawLines = new List<ContourLine>();

		public readonly List<ArcTurn> LabelTurns = new List<ArcTurn>();

		public readonly List<Geospatial.Vectors.Point> LabelSimplified = new List<Geospatial.Vectors.Point>();

		public readonly List<Geospatial.Vectors.Point> LabelSmoothed = new List<Geospatial.Vectors.Point>();

		public readonly List<Range1D> MajorGaps = new List<Range1D>();

		public readonly Dictionary<float, string> LabelTexts = new Dictionary<float, string>();

		public double LabelTextInterval = double.NaN;

		public string LabelTextUnit;

		public string LabelTextFormat;

		private readonly List<Polyline> _polyPool = new List<Polyline>();

		private int _polyCursor;

		public void ResetPolyPool()
		{
			_polyCursor = 0;
		}

		public Polyline RentPolyline()
		{
			if (_polyCursor == _polyPool.Count)
			{
				_polyPool.Add(new Polyline(0));
			}
			return _polyPool[_polyCursor++];
		}
	}

	private readonly struct ArcTurn(double arc, double turn)
	{
		public readonly double Arc = arc;

		public readonly double Turn = turn;
	}

	private sealed class SymbologyCache
	{
		public long Version = long.MinValue;

		public ConditionalVectorSymbology<List<ContourLine>> Ink;

		public VectorSymbology Label;
	}

	private const double MinorPenWidth = 1.0;

	private const double MajorPenWidth = 2.0;

	private const double MinorAlphaRatio = 0.625;

	private const int ChaikinIterations = 2;

	private const double ChaikinPinAngleDegrees = 65.0;

	private const double DouglasPeuckerEpsilonPx = 0.5;

	private const double RingCullPerimeterPx = 24.0;

	private const double StubCullLengthPx = 6.0;

	private const double LabelMinLengthPx = 120.0;

	private const double LabelWindowPx = 140.0;

	private const double LabelLatticeSpacingPx = 300.0;

	private const double LabelBaselineDpEpsilonPx = 3.0;

	private const double LabelBaselinePinAngleDegrees = 180.0;

	private const double LabelGapEmPerChar = 0.75;

	private const double LabelGapPaddingPx = 16.0;

	private const int MaxLabelsPerTile = 64;

	private const float LabelTextSize = 10f;

	private const float LabelHaloThickness = 1f;

	private const float LabelKerning = 1.25f;

	private const double HaloAlphaRatio = 0.75;

	private const double HaloTintTowardPole = 0.85;

	private const int AutoIntervalTargetLevels = 64;

	private const int AutoIntervalMinLevels = 4;

	private const int AutoIntervalFarOutLevels = 8;

	private const int MaxLayerBands = 2048;

	[ThreadStatic]
	private static TileScratch _tileScratch;

	private static readonly ConditionalWeakTable<ContourProperties, SymbologyCache> _symbologyCaches = new ConditionalWeakTable<ContourProperties, SymbologyCache>();

	public static int GutterCells => (int)Math.Ceiling(2.0 * VectorManagerOptions.RenderScaling) + 3;

	public static void Render(Memory<float> tileValues, PlotRequest plot, RasterSymbology symbology, float ndv, in ContourValueContext context)
	{
		ContourProperties contourProperties = symbology.ContourProperties;
		if (contourProperties == null || !contourProperties.IsEnabled)
		{
			return;
		}
		RasterDefinition rasterDefinition;
		ReadOnlyMemory<float> readOnlyMemory;
		if (context.HasGutter)
		{
			rasterDefinition = context.ExtendedRaster;
			readOnlyMemory = context.ExtendedValues;
		}
		else
		{
			rasterDefinition = plot.RasterQuery;
			readOnlyMemory = tileValues;
		}
		int cellCount = rasterDefinition.CellCount;
		if (readOnlyMemory.Length < cellCount || cellCount == 0)
		{
			return;
		}
		TileScratch tileScratch = _tileScratch ?? (_tileScratch = new TileScratch());
		IMemoryOwner<float> memoryOwner = null;
		IMemoryOwner<float> memoryOwner2 = null;
		try
		{
			ReadOnlySpan<float> slice = readOnlyMemory.Span.Slice(0, cellCount);
			if (ndv != -9999f)
			{
				memoryOwner = SharedMemoryPool<float>.RentNoFill(cellCount, out var mem);
				Span<float> memSpan = mem.Span;
				for (int i = 0; i < cellCount; i++)
				{
					float slice2 = slice[i];
					memSpan[i] = ((slice2 == ndv) ? (-9999f) : slice2);
				}
				slice = memSpan;
			}
			Buffers.MaxMin(slice, -9999f, out var max, out var min);
			if (max < min)
			{
				return;
			}
			bool condition = true;
			double valueMaxAboveValueMin;
			double min2;
			double max2;
			if (context.LayerStats.HasRange)
			{
				valueMaxAboveValueMin = context.LayerStats.ValueMax - context.LayerStats.ValueMin;
			}
			else if (TryGetLayerValueRange(symbology, out min2, out max2))
			{
				valueMaxAboveValueMin = max2 - min2;
				condition = false;
			}
			else
			{
				valueMaxAboveValueMin = max - min;
				condition = false;
			}
			ContourLevelSet.HumanLadderFamily family = (condition ? ContourLevelSet.ChooseLadderFamily(valueMaxAboveValueMin) : ContourLevelSet.HumanLadderFamily.FiveChain);
			double referenceSlope = ContourLayerStatistics.GetReferenceSlope(in context.LayerStats, plot.RasterQuery.CellSize);
			double naN = ((referenceSlope > 0.0) ? (70.0 * plot.RasterQuery.CellSize * referenceSlope) : double.NaN);
			double computed = 1.0;
			double humanIntervalContourLevelSetTerm;
			if (contourProperties.AutoInterval)
			{
				if (naN > 0.0)
				{
					humanIntervalContourLevelSetTerm = ContourLevelSet.HumanInterval(naN / (double)contourProperties.Density, family);
					double humanInterval = ContourLevelSet.HumanInterval(valueMaxAboveValueMin / 2048.0, family);
					if (humanIntervalContourLevelSetTerm < humanInterval)
					{
						humanIntervalContourLevelSetTerm = humanInterval;
					}
					for (int j = 0; j < 8; j++)
					{
						if (!(humanIntervalContourLevelSetTerm * 8.0 > valueMaxAboveValueMin))
						{
							break;
						}
						humanIntervalContourLevelSetTerm = ContourLevelSet.HumanIntervalBelow(humanIntervalContourLevelSetTerm, family);
					}
				}
				else
				{
					humanIntervalContourLevelSetTerm = ContourLevelSet.WalkDownToFit(ContourLevelSet.NiceInterval(valueMaxAboveValueMin / (double)(64f * contourProperties.Density)), valueMaxAboveValueMin, 4);
				}
			}
			else
			{
				humanIntervalContourLevelSetTerm = contourProperties.ContourInterval;
				if (humanIntervalContourLevelSetTerm > 0.0 && valueMaxAboveValueMin / humanIntervalContourLevelSetTerm > 2048.0)
				{
					computed = Math.Pow(2.0, Math.Ceiling(Math.Log2(valueMaxAboveValueMin / (2048.0 * humanIntervalContourLevelSetTerm))));
				}
			}
			List<ContourLevel> levels = tileScratch.Levels;
			levels.Clear();
			ContourLevelSet.SelectLevels(min, max, humanIntervalContourLevelSetTerm, computed, contourProperties.IndexEvery, levels);
			if (levels.Count == 0)
			{
				return;
			}
			memoryOwner2 = SharedMemoryPool<float>.RentNoFill(cellCount, out var mem2);
			Span<float> sliceSpan = mem2.Span.Slice(0, cellCount);
			Geospatial.Rasters.Convolution.Convolution.ConvolveBinomial3x3_NoDataAware(slice, sliceSpan, rasterDefinition);
			Buffers.MaxMin(sliceSpan, -9999f, out var max3, out var min3);
			if (max3 < min3)
			{
				return;
			}
			levels.Clear();
			ContourLevelSet.SelectLevels(min3, max3, humanIntervalContourLevelSetTerm, computed, contourProperties.IndexEvery, levels);
			if (levels.Count == 0)
			{
				return;
			}
			double cellSize = plot.RasterQuery.CellSize;
			Extent extent = plot.RasterQuery.Extent;
			tileScratch.Minors.Clear();
			tileScratch.Majors.Clear();
			tileScratch.ResetPolyPool();
			tileScratch.DpEpsilon = 0.5 * cellSize;
			tileScratch.RingCullLength = 24.0 * cellSize;
			tileScratch.StubCullLength = 6.0 * cellSize;
			tileScratch.TileExtent = extent;
			if (!GridContourVectorizer.TryVectorize(rasterDefinition, sliceSpan, CollectionsMarshal.AsSpan(levels), plot.Reporter, tileScratch, (TileScratch s, ReadOnlySpan<Geospatial.Vectors.Point> points, float level, bool isMajor) =>
			{
				SmoothCullCollect(s, points, level, isMajor);
			}) || plot.Reporter.CancelRequested)
			{
				return;
			}
			SymbologyCache symbologies = GetSymbologies(contourProperties);
			tileScratch.MajorGaps.Clear();
			if (contourProperties.ShowLabels)
			{
				EmitLabels(plot, tileScratch, humanIntervalContourLevelSetTerm * computed, cellSize, extent, symbologies.Label, contourProperties.UnitLabel);
			}
			IPolylineRenderer renderer = PolylineRenderLayer.Renderer;
			if (renderer == null)
			{
				return;
			}
			tileScratch.DrawLines.Clear();
			tileScratch.DrawLines.AddRange(tileScratch.Minors);
			tileScratch.DrawLines.AddRange(tileScratch.Majors);
			BuildStrokeCandidates(tileScratch, tileScratch.Candidates);
			if (tileScratch.Candidates.Count > 0)
			{
				renderer.Render<Polyline, List<ContourLine>>(plot, tileScratch.DrawLines, (int fid, List<ContourLine> model) => model[fid].Line, tileScratch.Candidates, symbologies.Ink);
			}
		}
		finally
		{
			memoryOwner2?.Dispose();
			memoryOwner?.Dispose();
		}
	}

	private static void SmoothCullCollect(TileScratch scratch, ReadOnlySpan<Geospatial.Vectors.Point> traced, float level, bool isMajor)
	{
		if (TrySmoothCullMaterialize(scratch, traced, out var line))
		{
			ContourLine contourLine = new ContourLine(line, level, isMajor);
			if (isMajor)
			{
				scratch.Majors.Add(contourLine);
			}
			else
			{
				scratch.Minors.Add(contourLine);
			}
		}
	}

	private static bool TrySmoothCullMaterialize(TileScratch scratch, ReadOnlySpan<Geospatial.Vectors.Point> traced, out Polyline line)
	{
		line = null;
		List<Geospatial.Vectors.Point> simplified = scratch.Simplified;
		List<Geospatial.Vectors.Point> smoothed = scratch.Smoothed;
		PolylineSmoothing.SimplifyDouglasPeucker(traced, scratch.DpEpsilon, simplified);
		bool simplifiedTerm = simplified.Count >= 3 && simplified[0] == simplified[simplified.Count - 1];
		PolylineSmoothing.ChaikinSmooth(simplified, 2, 65.0, simplifiedTerm, smoothed);
		int count = smoothed.Count;
		if (count < 2)
		{
			return false;
		}
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		double distanceToSum = 0.0;
		Geospatial.Vectors.Point point = smoothed[0];
		for (int i = 0; i < count; i++)
		{
			Geospatial.Vectors.Point point2 = smoothed[i];
			if (point2.X < maxValue)
			{
				maxValue = point2.X;
			}
			if (point2.X > minValue)
			{
				minValue = point2.X;
			}
			if (point2.Y < maxValue2)
			{
				maxValue2 = point2.Y;
			}
			if (point2.Y > minValue2)
			{
				minValue2 = point2.Y;
			}
			distanceToSum += point.DistanceTo(point2);
			point = point2;
		}
		if (scratch.TileExtent.Contains(new Extent(maxValue, minValue, maxValue2, minValue2)))
		{
			double ringCullLength = (simplifiedTerm ? scratch.RingCullLength : scratch.StubCullLength);
			if (distanceToSum < ringCullLength)
			{
				return false;
			}
		}
		line = scratch.RentPolyline();
		line.Clear();
		line.AddRange(smoothed);
		return true;
	}

	private static void BuildStrokeCandidates(TileScratch scratch, List<FeatureSegmentSubset> candidates)
	{
		candidates.Clear();
		int count = scratch.Minors.Count;
		List<ContourLine> drawLines = scratch.DrawLines;
		for (int i = 0; i < drawLines.Count; i++)
		{
			int segmentCount = drawLines[i].Line.SegmentCount;
			if (segmentCount >= 1)
			{
				int countTerm = i - count;
				Range1D gap = ((countTerm >= 0 && countTerm < scratch.MajorGaps.Count) ? scratch.MajorGaps[countTerm] : Range1D.NullRange);
				if (FeatureSegmentSubsetHelpers.TryMakeExcluding(i, segmentCount, gap, out var subset))
				{
					candidates.Add(subset);
				}
			}
		}
	}

	private static void EmitLabels(PlotRequest plot, TileScratch scratch, double effectiveInterval, double cellSize, Extent tileExtent, VectorSymbology labelSymbology, string unitLabel)
	{
		List<ContourLine> majors = scratch.Majors;
		List<Range1D> majorGaps = scratch.MajorGaps;
		for (int i = 0; i < majors.Count; i++)
		{
			majorGaps.Add(Range1D.NullRange);
		}
		if (majors.Count == 0)
		{
			return;
		}
		double scaledCellSize = 120.0 * cellSize;
		if (scratch.LabelTextInterval != effectiveInterval || scratch.LabelTextUnit != unitLabel)
		{
			scratch.LabelTexts.Clear();
			scratch.LabelTextInterval = effectiveInterval;
			scratch.LabelTextUnit = unitLabel;
			scratch.LabelTextFormat = "F" + DecimalsForInterval(effectiveInterval).ToString(CultureInfo.InvariantCulture);
		}
		int ordinal = 0;
		for (int j = 0; j < majors.Count; j++)
		{
			if (ordinal >= 64)
			{
				break;
			}
			ContourLine contourLine = majors[j];
			if (contourLine.Line.Length < scaledCellSize)
			{
				continue;
			}
			long levelIndex = (long)Math.Round((double)contourLine.Level / effectiveInterval);
			if (!scratch.LabelTexts.TryGetValue(contourLine.Level, out var labelTexts))
			{
				labelTexts = contourLine.Level.ToString(scratch.LabelTextFormat, CultureInfo.InvariantCulture);
				if (!string.IsNullOrEmpty(unitLabel))
				{
					labelTexts += unitLabel;
				}
				scratch.LabelTexts[contourLine.Level] = labelTexts;
			}
			double labelGapWorldLength = LabelGapWorldLength(labelTexts.Length, cellSize);
			double seamMargin = labelGapWorldLength * 0.5 + (double)GutterCells * cellSize;
			if (TryFindLatticeAnchor(contourLine.Line, levelIndex, cellSize, tileExtent, seamMargin, out var anchorArc) && TryPlaceLabel(contourLine.Line, cellSize, scratch, labelGapWorldLength, anchorArc, tileExtent, seamMargin, out var baseline, out var anchorArc2))
			{
				plot.AddLabel(new ContourLabelDefinition(labelTexts, baseline, labelSymbology));
				majorGaps[j] = ComputeLabelGapSegments(contourLine.Line, labelGapWorldLength, anchorArc2);
				ordinal++;
			}
		}
	}

	private static bool TryFindLatticeAnchor(Polyline line, long levelIndex, double cellSize, Extent tileExtent, double seamMargin, out double anchorArc)
	{
		anchorArc = 0.0;
		double scaledCellSize = 300.0 * cellSize;
		double computed = scaledCellSize * 0.35;
		double levelIndexTerm = (double)((levelIndex * 2654435761u) & 0xFFFF) / 65536.0;
		double levelIndexTerm2 = (double)((levelIndex * 2246822519u) & 0xFFFF) / 65536.0;
		ReadOnlySpan<Geospatial.Vectors.Point> pointBufferUnsafe = line.GetPointBufferUnsafe().Span;
		double maxValue = double.MaxValue;
		double distanceToSum = 0.0;
		for (int i = 0; i < pointBufferUnsafe.Length; i++)
		{
			if (i > 0)
			{
				distanceToSum += pointBufferUnsafe[i - 1].DistanceTo(pointBufferUnsafe[i]);
			}
			Geospatial.Vectors.Point point = pointBufferUnsafe[i];
			if (!(point.X < tileExtent.MinX + seamMargin) && !(point.X > tileExtent.MaxX - seamMargin) && !(point.Y < tileExtent.MinY + seamMargin) && !(point.Y > tileExtent.MaxY - seamMargin))
			{
				double pointTerm = (Math.Floor(point.X / scaledCellSize - levelIndexTerm + 0.5) + levelIndexTerm) * scaledCellSize;
				double pointTerm2 = (Math.Floor(point.Y / scaledCellSize - levelIndexTerm2 + 0.5) + levelIndexTerm2) * scaledCellSize;
				double pointTerm3 = point.X - pointTerm;
				double pointTerm4 = point.Y - pointTerm2;
				double maxValue2 = Math.Sqrt(pointTerm3 * pointTerm3 + pointTerm4 * pointTerm4);
				if (maxValue2 < computed && maxValue2 < maxValue)
				{
					maxValue = maxValue2;
					anchorArc = distanceToSum;
				}
			}
		}
		return maxValue < double.MaxValue;
	}

	private static bool TryPlaceLabel(Polyline chain, double cellSize, TileScratch scratch, double textArcLength, double preferredArc, Extent tileExtent, double seamMargin, out Memory<Geospatial.Vectors.Point> baseline, out double anchorArc)
	{
		double length = chain.Length;
		double scaledCellSize = 140.0 * cellSize;
		double lengthTerm = Math.Min(scaledCellSize, length) * 0.5;
		double computed = scaledCellSize * 0.5;
		double scaledCellSize2 = 7.5 * cellSize;
		double lengthPreferredArcTerm = Math.Max(lengthTerm, Math.Min(length - lengthTerm, preferredArc));
		baseline = default;
		anchorArc = 0.0;
		double maxValue = double.MaxValue;
		double anchorArc2 = 0.0;
		for (int i = 0; i <= 3; i++)
		{
			for (int j = 0; j < ((i == 0) ? 1 : 2); j++)
			{
				double anchorArc22 = lengthPreferredArcTerm + (double)((j == 0) ? i : (-i)) * computed;
				if (anchorArc22 - lengthTerm < 0.0 || anchorArc22 + lengthTerm > length)
				{
					continue;
				}
				ReadOnlySpan<Geospatial.Vectors.Point> baseline2 = BuildLabelBaselineScratch(chain, cellSize, scratch, anchorArc22);
				Geospatial.Vectors.Point point = baseline2[baseline2.Length / 2];
				if (!(point.X < tileExtent.MinX + seamMargin) && !(point.X > tileExtent.MaxX - seamMargin) && !(point.Y < tileExtent.MinY + seamMargin) && !(point.Y > tileExtent.MaxY - seamMargin))
				{
					double maxWindowedTurn = MaxWindowedTurn(baseline2, textArcLength, scaledCellSize2 * 3.0, scratch);
					if (!(maxWindowedTurn > 45.0) && !(maxWindowedTurn >= maxValue))
					{
						maxValue = maxWindowedTurn;
						anchorArc2 = anchorArc22;
					}
				}
			}
		}
		if (maxValue == double.MaxValue)
		{
			return false;
		}
		baseline = BuildLabelBaselineScratch(chain, cellSize, scratch, anchorArc2).ToArray();
		anchorArc = anchorArc2;
		return true;
	}

	private static double MaxWindowedTurn(ReadOnlySpan<Geospatial.Vectors.Point> baseline, double textArcLength, double turnWindowArc, TileScratch scratch)
	{
		if (baseline.Length < 3 || !(textArcLength > 0.0))
		{
			return 0.0;
		}
		double distanceToSum = 0.0;
		for (int i = 1; i < baseline.Length; i++)
		{
			distanceToSum += baseline[i - 1].DistanceTo(baseline[i]);
		}
		if (distanceToSum <= 0.0)
		{
			return double.MaxValue;
		}
		double distanceToSumTextArcLengthTerm = Math.Max(0.0, (distanceToSum - textArcLength) * 0.5);
		double distanceToSumTextArcLengthTerm2 = Math.Min(distanceToSum, distanceToSumTextArcLengthTerm + textArcLength);
		List<ArcTurn> labelTurns = scratch.LabelTurns;
		labelTurns.Clear();
		double distanceToSum2 = 0.0;
		for (int j = 1; j < baseline.Length - 1; j++)
		{
			distanceToSum2 += baseline[j - 1].DistanceTo(baseline[j]);
			if (!(distanceToSum2 < distanceToSumTextArcLengthTerm))
			{
				if (distanceToSum2 > distanceToSumTextArcLengthTerm2)
				{
					break;
				}
				labelTurns.Add(new ArcTurn(distanceToSum2, PolylineSmoothing.TurnDegrees(baseline[j - 1], baseline[j], baseline[j + 1])));
			}
		}
		double turnSum = 0.0;
		double turnSum2 = 0.0;
		int index = 0;
		for (int k = 0; k < labelTurns.Count; k++)
		{
			turnSum2 += labelTurns[k].Turn;
			while (labelTurns[k].Arc - labelTurns[index].Arc > turnWindowArc)
			{
				turnSum2 -= labelTurns[index++].Turn;
			}
			if (turnSum2 > turnSum)
			{
				turnSum = turnSum2;
			}
		}
		return turnSum;
	}

	private static double LabelGapWorldLength(int textLength, double cellSize)
	{
		return ((double)((float)textLength * 10f) * 0.75 + 16.0) * cellSize;
	}

	private static Range1D ComputeLabelGapSegments(Polyline line, double gapLength, double gapCenterArc)
	{
		ReadOnlySpan<Geospatial.Vectors.Point> pointBufferUnsafe = line.GetPointBufferUnsafe().Span;
		if (pointBufferUnsafe.Length < 2)
		{
			return Range1D.NullRange;
		}
		double length = line.Length;
		if (gapLength >= length)
		{
			return new Range1D(0, pointBufferUnsafe.Length - 2);
		}
		double lengthGapLengthTerm = Math.Max(0.0, Math.Min(length - gapLength, gapCenterArc - gapLength * 0.5));
		double gapLengthTerm = lengthGapLengthTerm + gapLength;
		int ordinal = -1;
		int end = pointBufferUnsafe.Length - 2;
		double computed = 0.0;
		for (int i = 0; i < pointBufferUnsafe.Length - 1; i++)
		{
			if (computed >= gapLengthTerm)
			{
				end = i - 1;
				break;
			}
			double distanceToPointBufferUnsafeTerm = computed + pointBufferUnsafe[i].DistanceTo(pointBufferUnsafe[i + 1]);
			if (ordinal < 0 && distanceToPointBufferUnsafeTerm > lengthGapLengthTerm)
			{
				ordinal = i;
			}
			computed = distanceToPointBufferUnsafeTerm;
		}
		if (ordinal < 0)
		{
			return Range1D.NullRange;
		}
		return new Range1D(ordinal, end);
	}

	private static ReadOnlySpan<Geospatial.Vectors.Point> BuildLabelBaselineScratch(Polyline chain, double cellSize, TileScratch scratch, double anchorArc)
	{
		Memory<Geospatial.Vectors.Point> arcWindow = ArcWindow(chain.GetPointBufferUnsafe(), anchorArc, 140.0 * cellSize);
		if (arcWindow.Length < 3)
		{
			return arcWindow.Span;
		}
		PolylineSmoothing.SimplifyDouglasPeucker(arcWindow.Span, 3.0 * cellSize, scratch.LabelSimplified);
		PolylineSmoothing.ChaikinSmooth(scratch.LabelSimplified, 2, 180.0, closed: false, scratch.LabelSmoothed);
		if (scratch.LabelSmoothed.Count < 2)
		{
			return arcWindow.Span;
		}
		return CollectionsMarshal.AsSpan(scratch.LabelSmoothed);
	}

	private static Memory<Geospatial.Vectors.Point> ArcWindow(Memory<Geospatial.Vectors.Point> points, double centerArc, double windowLength)
	{
		ReadOnlySpan<Geospatial.Vectors.Point> pointsSpan = points.Span;
		if (pointsSpan.Length < 3 || !(windowLength > 0.0))
		{
			return points;
		}
		double distanceToSum = 0.0;
		for (int i = 1; i < pointsSpan.Length; i++)
		{
			distanceToSum += pointsSpan[i - 1].DistanceTo(pointsSpan[i]);
		}
		if (distanceToSum <= windowLength)
		{
			return points;
		}
		double distanceToSumWindowLengthTerm = Math.Max(0.0, Math.Min(distanceToSum - windowLength, centerArc - windowLength * 0.5));
		double windowLengthTerm = distanceToSumWindowLengthTerm + windowLength;
		int ordinal = 0;
		int lengthOffset = pointsSpan.Length - 1;
		double computed = 0.0;
		for (int j = 1; j < pointsSpan.Length; j++)
		{
			double distanceToPointsSpanTerm = computed + pointsSpan[j - 1].DistanceTo(pointsSpan[j]);
			if (computed <= distanceToSumWindowLengthTerm && distanceToPointsSpanTerm > distanceToSumWindowLengthTerm)
			{
				ordinal = j - 1;
			}
			if (distanceToPointsSpanTerm >= windowLengthTerm)
			{
				lengthOffset = j;
				break;
			}
			computed = distanceToPointsSpanTerm;
		}
		return points.Slice(ordinal, lengthOffset - ordinal + 1);
	}

	private static int DecimalsForInterval(double interval)
	{
		for (int i = 0; i <= 3; i++)
		{
			double intervalTerm = interval * Math.Pow(10.0, i);
			if (Math.Abs(intervalTerm - Math.Round(intervalTerm)) < 1E-06 * Math.Max(1.0, Math.Abs(intervalTerm)))
			{
				return i;
			}
		}
		return 3;
	}

	private static bool TryGetLayerValueRange(RasterSymbology symbology, out double min, out double max)
	{
		min = 0.0;
		max = 0.0;
		InterpolatedSurfaceFill surfaceFill = symbology.SurfaceFill;
		if (surfaceFill == null || surfaceFill.ColorValues == null || surfaceFill.ColorValues.Length == 0)
		{
			return false;
		}
		min = surfaceFill.MinValue;
		max = surfaceFill.MaxValue;
		if (double.IsNaN(min) || double.IsInfinity(min) || double.IsNaN(max) || double.IsInfinity(max))
		{
			return false;
		}
		return max > min;
	}

	private static SymbologyCache GetSymbologies(ContourProperties props)
	{
		SymbologyCache orCreateValue = _symbologyCaches.GetOrCreateValue(props);
		long versionXY = props.VersionXY;
		if (Volatile.Read(in orCreateValue.Version) == versionXY)
		{
			return orCreateValue;
		}
		lock (orCreateValue)
		{
			if (orCreateValue.Version == versionXY)
			{
				return orCreateValue;
			}
			Color color = props.Color;
			int opacityPropsTerm = Math.Clamp((int)Math.Round((double)props.Opacity / 100.0 * 255.0), 0, 255);
			int alpha = (int)Math.Round(0.625 * (double)opacityPropsTerm);
			Color majorColor = Color.FromArgb(opacityPropsTerm, color.R, color.G, color.B);
			Color color2 = Color.FromArgb(alpha, color.R, color.G, color.B);
			Color color3 = Color.FromArgb(Math.Clamp((int)Math.Round((double)opacityPropsTerm * 255.0 / 224.0), 0, 255), color.R, color.G, color.B);
			double luminance = ColorMath.Luminance(color);
			Color colorB = ((luminance > 150.0) ? Color.Black : Color.White);
			Color haloColor = Color.FromArgb(Math.Clamp((int)Math.Round((double)opacityPropsTerm * 0.75), 0, 255), ColorMath.Lerp(color, colorB, 0.85));
			BlendMode blendMode = ((luminance <= 150.0 && ColorMath.Chroma(color) <= 32) ? BlendMode.Multiply : BlendMode.SourceOver);
			orCreateValue.Ink = new ConditionalVectorSymbology<List<ContourLine>>(color2, 1.0, color2)
			{
				BlendMode = blendMode,
				GetPenColorOverride = (List<ContourLine> lines, int fid) => (!lines[fid].IsMajor) ? ((Color?)null) : new Color?(majorColor),
				GetPenWidthOverride = (List<ContourLine> lines, int fid) => (!lines[fid].IsMajor) ? ((double?)null) : new double?(2.0)
			};
			SymbologyCache symbologyCache = orCreateValue;
			if (symbologyCache.Label == null)
			{
				symbologyCache.Label = new VectorSymbology(color3, 10.0, color3);
			}
			orCreateValue.Label.PenColor = color3;
			orCreateValue.Label.FillColor = color3;
			orCreateValue.Label.HaloColor = haloColor;
			orCreateValue.Label.HaloThickness = 1f;
			orCreateValue.Label.TextKerning = 1.25f;
			Volatile.Write(ref orCreateValue.Version, versionXY);
			return orCreateValue;
		}
	}
}

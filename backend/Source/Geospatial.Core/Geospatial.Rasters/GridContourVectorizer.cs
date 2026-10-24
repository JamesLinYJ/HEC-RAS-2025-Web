using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Utility.Progress;

namespace Geospatial.Rasters;

public static class GridContourVectorizer
{
	private sealed class VectorizeScratch
	{
		public int[] Slot0 = Array.Empty<int>();

		public int[] Slot1 = Array.Empty<int>();

		public bool[] Used = Array.Empty<bool>();

		public bool[] RowAllValid = Array.Empty<bool>();

		public List<Seg>[] PerLevel = Array.Empty<List<Seg>>();

		public readonly List<Point> Backward = new List<Point>();

		public readonly List<Point> Forward = new List<Point>();

		public readonly List<Point> Chain = new List<Point>();
	}

	public delegate void ContourChainVisitor<TModel>(TModel model, ReadOnlySpan<Point> points, float level, bool isMajor);

	private readonly struct Seg(Point a, int edgeA, Point b, int edgeB)
	{
		public readonly Point A = a;

		public readonly Point B = b;

		public readonly int EdgeA = edgeA;

		public readonly int EdgeB = edgeB;
	}

	private const int CancelCheckRowInterval = 32;

	[ThreadStatic]
	private static VectorizeScratch _scratch;

	private const int SegListInitialCapacity = 256;

	public static bool TryVectorize(RasterDefinition grid, ReadOnlySpan<float> values, ReadOnlySpan<ContourLevel> levels, ProgressReporter reporter, List<ContourLine> results)
	{
		return TryVectorize(grid, values, levels, reporter, results, (List<ContourLine> contourLines, ReadOnlySpan<Point> points, float level, bool isMajor) =>
		{
			contourLines.Add(new ContourLine(Polyline.CreateWithBuffer(points.ToArray()), level, isMajor));
		});
	}

	public static bool TryVectorize<TModel>(RasterDefinition grid, ReadOnlySpan<float> values, ReadOnlySpan<ContourLevel> levels, ProgressReporter reporter, TModel model, ContourChainVisitor<TModel> visitor)
	{
		int rows = grid.Rows;
		int cols = grid.Cols;
		int length = levels.Length;
		if (rows < 2 || cols < 2 || length == 0)
		{
			return true;
		}
		if (values.Length < rows * cols)
		{
			return true;
		}
		double cellSize = grid.CellSize;
		Point upperLeft = grid.Extent.UpperLeft;
		double x = upperLeft.X;
		double y = upperLeft.Y;
		int rowsColsTerm = rows * (cols - 1);
		int count = rowsColsTerm + (rows - 1) * cols;
		VectorizeScratch vectorizeScratch = _scratch ?? (_scratch = new VectorizeScratch());
		if (vectorizeScratch.PerLevel.Length < length)
		{
			Array.Resize(ref vectorizeScratch.PerLevel, length);
		}
		List<Seg>[] perLevel = vectorizeScratch.PerLevel;
		for (int i = 0; i < perLevel.Length; i++)
		{
			perLevel[i]?.Clear();
		}
		Span<Point> points = stackalloc Point[4];
		Span<int> intSpan = stackalloc int[4];
		if (vectorizeScratch.RowAllValid.Length < rows)
		{
			vectorizeScratch.RowAllValid = new bool[rows];
		}
		bool[] rowAllValid = vectorizeScratch.RowAllValid;
		for (int j = 0; j < rows; j++)
		{
			rowAllValid[j] = values.Slice(j * cols, cols).IndexOf(-9999f) < 0;
		}
		for (int k = 0; k < rows - 1; k++)
		{
			if (k % 32 == 0 && reporter != null && reporter.CancelRequested)
			{
				return false;
			}
			int colsTerm = k * cols;
			int start = colsTerm + cols;
			int colsTerm2 = k * (cols - 1);
			int colsTerm3 = colsTerm2 + (cols - 1);
			int ordinal = rowsColsTerm + colsTerm;
			ReadOnlySpan<float> slice = values.Slice(colsTerm, cols);
			ReadOnlySpan<float> sliceSpan = values.Slice(start, cols);
			bool rowAllValidTerm = rowAllValid[k] && rowAllValid[k + 1];
			float slice2 = slice[0];
			float slice3 = sliceSpan[0];
			for (int l = 0; l < cols - 1; l++)
			{
				float slice4 = slice[l + 1];
				float slice5 = sliceSpan[l + 1];
				if (rowAllValidTerm || (slice2 != -9999f && slice4 != -9999f && slice3 != -9999f && slice5 != -9999f))
				{
					float slice6 = slice2;
					float slice7 = slice2;
					if (slice4 < slice6)
					{
						slice6 = slice4;
					}
					else if (slice4 > slice7)
					{
						slice7 = slice4;
					}
					if (slice3 < slice6)
					{
						slice6 = slice3;
					}
					else if (slice3 > slice7)
					{
						slice7 = slice3;
					}
					if (slice5 < slice6)
					{
						slice6 = slice5;
					}
					else if (slice5 > slice7)
					{
						slice7 = slice5;
					}
					for (int m = FirstLevelGreaterThan(levels, slice6); m < length; m++)
					{
						float levelsTerm = levels[m].Value;
						if (levelsTerm > slice7)
						{
							break;
						}
						int ordinal2 = 0;
						if (TryCross(slice2, slice4, levelsTerm, out var fraction))
						{
							points[ordinal2] = new Point(x + ((double)l + 0.5 + (double)fraction) * cellSize, y - ((double)k + 0.5) * cellSize);
							intSpan[ordinal2++] = colsTerm2 + l;
						}
						if (TryCross(slice4, slice5, levelsTerm, out var fraction2))
						{
							points[ordinal2] = new Point(x + ((double)l + 1.5) * cellSize, y - ((double)k + 0.5 + (double)fraction2) * cellSize);
							intSpan[ordinal2++] = ordinal + l + 1;
						}
						if (TryCross(slice3, slice5, levelsTerm, out var fraction3))
						{
							points[ordinal2] = new Point(x + ((double)l + 0.5 + (double)fraction3) * cellSize, y - ((double)k + 1.5) * cellSize);
							intSpan[ordinal2++] = colsTerm3 + l;
						}
						if (TryCross(slice2, slice3, levelsTerm, out var fraction4))
						{
							points[ordinal2] = new Point(x + ((double)l + 0.5) * cellSize, y - ((double)k + 0.5 + (double)fraction4) * cellSize);
							intSpan[ordinal2++] = ordinal + l;
						}
						switch (ordinal2)
						{
						case 2:
							AddSegment(perLevel, m, points[0], intSpan[0], points[1], intSpan[1]);
							break;
						case 4:
						{
							bool slice2Term = slice2 >= levelsTerm;
							bool slice2Slice4Term = (slice2 + slice4 + slice3 + slice5) * 0.25f >= levelsTerm;
							if (slice2Term == slice2Slice4Term)
							{
								AddSegment(perLevel, m, points[0], intSpan[0], points[1], intSpan[1]);
								AddSegment(perLevel, m, points[2], intSpan[2], points[3], intSpan[3]);
							}
							else
							{
								AddSegment(perLevel, m, points[0], intSpan[0], points[3], intSpan[3]);
								AddSegment(perLevel, m, points[1], intSpan[1], points[2], intSpan[2]);
							}
							break;
						}
						}
					}
				}
				slice2 = slice4;
				slice3 = slice5;
			}
		}
		int perLevelCount = 0;
		for (int n = 0; n < length; n++)
		{
			if (perLevel[n] != null && perLevel[n].Count > perLevelCount)
			{
				perLevelCount = perLevel[n].Count;
			}
		}
		if (perLevelCount == 0)
		{
			return true;
		}
		if (vectorizeScratch.Slot0.Length < count)
		{
			vectorizeScratch.Slot0 = new int[count];
			vectorizeScratch.Slot1 = new int[count];
		}
		if (vectorizeScratch.Used.Length < perLevelCount)
		{
			vectorizeScratch.Used = new bool[perLevelCount];
		}
		int[] slot = vectorizeScratch.Slot0;
		int[] slot2 = vectorizeScratch.Slot1;
		bool[] used = vectorizeScratch.Used;
		for (int intervalIndex = 0; intervalIndex < length; intervalIndex++)
		{
			List<Seg> perLevelList = perLevel[intervalIndex];
			if (perLevelList != null && perLevelList.Count != 0)
			{
				if (reporter != null && reporter.CancelRequested)
				{
					return false;
				}
				ChainLevel(perLevelList, slot, slot2, used, vectorizeScratch, levels[intervalIndex], model, visitor);
				perLevelList.Clear();
			}
		}
		return true;
	}

	private static bool TryCross(float a, float b, float level, out float fraction)
	{
		fraction = 0f;
		bool levelTerm = a < level;
		bool levelTerm2 = b < level;
		if (levelTerm == levelTerm2)
		{
			return false;
		}
		fraction = (level - a) / (b - a);
		return true;
	}

	private static int FirstLevelGreaterThan(ReadOnlySpan<ContourLevel> levels, float computed)
	{
		int ordinal = 0;
		int levelLength = levels.Length;
		while (ordinal < levelLength)
		{
			int levelLength2 = ordinal + levelLength >> 1;
			if (levels[levelLength2].Value > computed)
			{
				levelLength = levelLength2;
			}
			else
			{
				ordinal = levelLength2 + 1;
			}
		}
		return ordinal;
	}

	private static void AddSegment(List<Seg>[] perLevel, int li, Point a, int edgeA, Point b, int edgeB)
	{
		(perLevel[li] ?? (perLevel[li] = new List<Seg>(256))).Add(new Seg(a, edgeA, b, edgeB));
	}

	private static void ChainLevel<TModel>(List<Seg> segs, int[] slot0, int[] slot1, bool[] used, VectorizeScratch scratch, ContourLevel level, TModel model, ContourChainVisitor<TModel> visitor)
	{
		int count = segs.Count;
		Array.Clear(used, 0, count);
		for (int i = 0; i < count; i++)
		{
			Seg seg = segs[i];
			Register(slot0, slot1, seg.EdgeA, i);
			Register(slot0, slot1, seg.EdgeB, i);
		}
		List<Point> backward = scratch.Backward;
		List<Point> forward = scratch.Forward;
		List<Point> chain = scratch.Chain;
		for (int j = 0; j < count; j++)
		{
			if (!used[j])
			{
				used[j] = true;
				Seg seg2 = segs[j];
				backward.Clear();
				Walk(segs, slot0, slot1, used, seg2.EdgeA, backward);
				forward.Clear();
				Walk(segs, slot0, slot1, used, seg2.EdgeB, forward);
				chain.Clear();
				for (int countOffset = backward.Count - 1; countOffset >= 0; countOffset--)
				{
					chain.Add(backward[countOffset]);
				}
				chain.Add(seg2.A);
				chain.Add(seg2.B);
				for (int k = 0; k < forward.Count; k++)
				{
					chain.Add(forward[k]);
				}
				if (chain.Count >= 2)
				{
					visitor(model, CollectionsMarshal.AsSpan(chain), level.Value, level.IsMajor);
				}
			}
		}
		for (int l = 0; l < count; l++)
		{
			Seg seg3 = segs[l];
			slot0[seg3.EdgeA] = 0;
			slot1[seg3.EdgeA] = 0;
			slot0[seg3.EdgeB] = 0;
			slot1[seg3.EdgeB] = 0;
		}
	}

	private static void Walk(List<Seg> segs, int[] slot0, int[] slot1, bool[] used, int edge, List<Point> output)
	{
		while (true)
		{
			int unusedSegmentOnEdge = UnusedSegmentOnEdge(slot0, slot1, used, edge);
			if (unusedSegmentOnEdge < 0)
			{
				break;
			}
			used[unusedSegmentOnEdge] = true;
			Seg seg = segs[unusedSegmentOnEdge];
			if (seg.EdgeA == edge)
			{
				output.Add(seg.B);
				edge = seg.EdgeB;
			}
			else
			{
				output.Add(seg.A);
				edge = seg.EdgeA;
			}
		}
	}

	private static void Register(int[] slot0, int[] slot1, int edge, int segIdx)
	{
		if (slot0[edge] == 0)
		{
			slot0[edge] = segIdx + 1;
		}
		else
		{
			slot1[edge] = segIdx + 1;
		}
	}

	private static int UnusedSegmentOnEdge(int[] slot0, int[] slot1, bool[] used, int edge)
	{
		int slot02 = slot0[edge];
		if (slot02 != 0 && !used[slot02 - 1])
		{
			return slot02 - 1;
		}
		slot02 = slot1[edge];
		if (slot02 != 0 && !used[slot02 - 1])
		{
			return slot02 - 1;
		}
		return -1;
	}
}

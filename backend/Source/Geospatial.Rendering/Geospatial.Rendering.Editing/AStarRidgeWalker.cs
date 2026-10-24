using System;
using System.Collections.Generic;

namespace Geospatial.Rendering.Editing;

public static class AStarRidgeWalker
{
	private static readonly double _sqrt2 = Math.Sqrt(2.0);

	private static readonly (int dr, int dc, double len)[] _neighbors = new (int, int, double)[8]
	{
		(-1, -1, _sqrt2),
		(-1, 0, 1.0),
		(-1, 1, _sqrt2),
		(0, -1, 1.0),
		(0, 1, 1.0),
		(1, -1, _sqrt2),
		(1, 0, 1.0),
		(1, 1, _sqrt2)
	};

	public static bool TryFindPath(float[] zGrid, int rows, int cols, int aRow, int aCol, int bRow, int bCol, double alpha, double zMin, double zMax, int maxExpansions, double bailPixelLength, int[] parentBuf, double[] gScoreBuf, double[] lengthBuf, PriorityQueue<int, double> openPq, List<(int row, int col)> pixelPathBuf, out List<(int row, int col)> path)
	{
		path = null;
		if (zGrid == null)
		{
			return false;
		}
		if (rows <= 0 || cols <= 0)
		{
			return false;
		}
		int count = rows * cols;
		if (zGrid.Length < count)
		{
			return false;
		}
		if (!InBounds(aRow, aCol, rows, cols))
		{
			return false;
		}
		if (!InBounds(bRow, bCol, rows, cols))
		{
			return false;
		}
		int aRowColsTerm = aRow * cols + aCol;
		int bRowColsTerm = bRow * cols + bCol;
		List<(int, int)> pixelPathBufList = pixelPathBuf ?? new List<(int, int)>();
		pixelPathBufList.Clear();
		if (aRowColsTerm == bRowColsTerm)
		{
			pixelPathBufList.Add((aRow, aCol));
			path = pixelPathBufList;
			return true;
		}
		double zMaxAboveZMin = Math.Max(1E-09, zMax - zMin);
		int[] parentBufArray = ((parentBuf != null && parentBuf.Length >= count) ? parentBuf : new int[count]);
		double[] gScoreBufArray = ((gScoreBuf != null && gScoreBuf.Length >= count) ? gScoreBuf : new double[count]);
		double[] lengthBufArray = ((lengthBuf != null && lengthBuf.Length >= count) ? lengthBuf : new double[count]);
		Array.Fill(parentBufArray, -1, 0, count);
		Array.Fill(gScoreBufArray, double.PositiveInfinity, 0, count);
		Array.Fill(lengthBufArray, double.PositiveInfinity, 0, count);
		PriorityQueue<int, double> priorityQueue = openPq ?? new PriorityQueue<int, double>();
		priorityQueue.Clear();
		gScoreBufArray[aRowColsTerm] = 0.0;
		lengthBufArray[aRowColsTerm] = 0.0;
		priorityQueue.Enqueue(aRowColsTerm, Heuristic(aRow, aCol, bRow, bCol));
		int ordinal = 0;
		while (priorityQueue.Count > 0)
		{
			int dequeue = priorityQueue.Dequeue();
			if (dequeue == bRowColsTerm)
			{
				Reconstruct(parentBufArray, dequeue, cols, pixelPathBufList);
				path = pixelPathBufList;
				return true;
			}
			ordinal++;
			if (ordinal > maxExpansions)
			{
				return false;
			}
			int dequeueOverCols = dequeue / cols;
			int dequeueColsTerm = dequeue - dequeueOverCols * cols;
			double gScoreBuf2 = gScoreBufArray[dequeue];
			double lengthBuf2 = lengthBufArray[dequeue];
			if (lengthBuf2 > bailPixelLength)
			{
				continue;
			}
			for (int i = 0; i < _neighbors.Length; i++)
			{
				(int dr, int dc, double len) tuple = _neighbors[i];
				int dr = tuple.dr;
				int dc = tuple.dc;
				double len = tuple.len;
				int drTerm = dequeueOverCols + dr;
				int dcTerm = dequeueColsTerm + dc;
				if (InBounds(drTerm, dcTerm, rows, cols))
				{
					int dequeueDrTerm = dequeue + dr * cols + dc;
					float zGrid2 = zGrid[dequeueDrTerm];
					double zMin2 = (float.IsNaN(zGrid2) ? zMin : ((double)zGrid2));
					double alphaZMaxTerm = 1.0 + alpha * (zMax - zMin2) / zMaxAboveZMin;
					double gScoreBufArray2 = gScoreBuf2 + len * alphaZMaxTerm;
					if (!(gScoreBufArray2 >= gScoreBufArray[dequeueDrTerm]))
					{
						gScoreBufArray[dequeueDrTerm] = gScoreBufArray2;
						lengthBufArray[dequeueDrTerm] = lengthBuf2 + len;
						parentBufArray[dequeueDrTerm] = dequeue;
						double priority = gScoreBufArray2 + Heuristic(drTerm, dcTerm, bRow, bCol);
						priorityQueue.Enqueue(dequeueDrTerm, priority);
					}
				}
			}
		}
		return false;
	}

	private static bool InBounds(int row, int col, int rows, int cols)
	{
		if (row >= 0 && row < rows && col >= 0)
		{
			return col < cols;
		}
		return false;
	}

	private static double Heuristic(int r0, int c0, int r1, int c1)
	{
		double r1AboveR0 = r1 - r0;
		double c1AboveC0 = c1 - c0;
		return Math.Sqrt(r1AboveR0 * r1AboveR0 + c1AboveC0 * c1AboveC0);
	}

	private static void Reconstruct(int[] parent, int goalIdx, int cols, List<(int row, int col)> output)
	{
		for (int goalIdx2 = goalIdx; goalIdx2 != -1; goalIdx2 = parent[goalIdx2])
		{
			int goalIdx2OverCols = goalIdx2 / cols;
			int goalIdx2ColsTerm = goalIdx2 - goalIdx2OverCols * cols;
			output.Add((goalIdx2OverCols, goalIdx2ColsTerm));
		}
		output.Reverse();
	}
}

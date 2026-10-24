using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters.TileProcessing.Contours;

public sealed class TiledContourVectorizer : ITileProcessor<float, ContourTileState, List<Polyline>>
{
	private readonly record struct VKey(long X, long Y);

	private readonly float _threshold;

	public ITileStateSerializer<ContourTileState> Serializer { get; } = new ContourTileStateSerializer();

	public TiledContourVectorizer(float threshold)
	{
		_threshold = threshold;
	}

	public ContourTileState CreateState(long tileIndex, RasterDefinition tileDefinition)
	{
		return new ContourTileState();
	}

	public void Accumulate(in TileAccumulateContext<float, ContourTileState> context)
	{
		TileWithNeighbors<float> tiles = context.Tiles;
		RasterDefinition tileDefinition = context.TileDefinition;
		int cols = tileDefinition.Cols;
		double cellSize = tileDefinition.CellSize;
		Point upperLeft = tileDefinition.Extent.UpperLeft;
		double x = upperLeft.X;
		double y = upperLeft.Y;
		List<(Point, Point)> valueTuples = new List<(Point, Point)>();
		Span<Point> points = stackalloc Point[4];
		for (int i = 0; i < cols; i++)
		{
			for (int j = 0; j < cols; j++)
			{
				float a = SampleVertex(tiles, cols, i, j);
				float sampleVertex = SampleVertex(tiles, cols, i, j + 1);
				float sampleVertex2 = SampleVertex(tiles, cols, i + 1, j);
				float b = SampleVertex(tiles, cols, i + 1, j + 1);
				int ordinal = 0;
				if (TryCross(a, sampleVertex, out var fraction))
				{
					points[ordinal++] = new Point(VertexX(x, cellSize, j, fraction), VertexY(y, cellSize, i, 0.0));
				}
				if (TryCross(sampleVertex, b, out var fraction2))
				{
					points[ordinal++] = new Point(VertexX(x, cellSize, j, 1.0), VertexY(y, cellSize, i, fraction2));
				}
				if (TryCross(sampleVertex2, b, out var fraction3))
				{
					points[ordinal++] = new Point(VertexX(x, cellSize, j, fraction3), VertexY(y, cellSize, i, 1.0));
				}
				if (TryCross(a, sampleVertex2, out var fraction4))
				{
					points[ordinal++] = new Point(VertexX(x, cellSize, j, 0.0), VertexY(y, cellSize, i, fraction4));
				}
				switch (ordinal)
				{
				case 2:
					valueTuples.Add((points[0], points[1]));
					break;
				case 4:
					valueTuples.Add((points[0], points[1]));
					valueTuples.Add((points[2], points[3]));
					break;
				}
			}
		}
		if (valueTuples.Count == 0)
		{
			return;
		}
		foreach (List<Point> chainSegment in ChainSegments(valueTuples, cellSize))
		{
			context.State.Fragments.Add(chainSegment);
		}
	}

	public bool Resolve(in TileResolveContext<ContourTileState> context)
	{
		ContourTileState state = context.State;
		if (state.Fragments.Count == 0)
		{
			return false;
		}
		double cellSize = context.TileDefinition.CellSize;
		double eps = cellSize * 0.001;
		bool result = false;
		bool condition = true;
		while (condition)
		{
			condition = false;
			for (int i = 0; i < state.Fragments.Count; i++)
			{
				List<Point> fragment = state.Fragments[i];
				if (fragment.Count >= 2 && (TryStitchEnd(in context, fragment, atEnd: true, cellSize, eps) || TryStitchEnd(in context, fragment, atEnd: false, cellSize, eps)))
				{
					result = true;
					condition = true;
					break;
				}
			}
		}
		return result;
	}

	public List<Polyline> Reduce(ITileStateStore<ContourTileState> states, ITiledRasterDefinition tiling)
	{
		List<Polyline> polylines = new List<Polyline>();
		foreach (long tileIndex in states.TileIndices)
		{
			if (!states.TryPeek(tileIndex, out var state))
			{
				continue;
			}
			foreach (List<Point> fragment in state.Fragments)
			{
				if (fragment.Count >= 2)
				{
					polylines.Add(new Polyline(new List<Point>(fragment)));
				}
			}
		}
		return polylines;
	}

	private static bool TryStitchEnd(in TileResolveContext<ContourTileState> context, List<Point> frag, bool atEnd, double cs, double eps)
	{
		int count = frag.Count;
		Point b = (atEnd ? frag[count - 1] : frag[0]);
		Point point = (atEnd ? frag[count - 2] : frag[1]);
		double pointTerm = b.X - point.X;
		double pointTerm2 = b.Y - point.Y;
		double computed = Math.Sqrt(pointTerm * pointTerm + pointTerm2 * pointTerm2);
		if (computed < 1E-12)
		{
			return false;
		}
		Point worldPoint = new Point(b.X + pointTerm / computed * cs * 0.5, b.Y + pointTerm2 / computed * cs * 0.5);
		if (!context.TryGetTileAt(worldPoint, out var _, out var state))
		{
			return false;
		}
		for (int i = 0; i < state.Fragments.Count; i++)
		{
			List<Point> fragment = state.Fragments[i];
			if (fragment.Count < 2)
			{
				continue;
			}
			bool close = Close(fragment[0], b, eps);
			bool close2 = Close(fragment[fragment.Count - 1], b, eps);
			if (!close && !close2)
			{
				continue;
			}
			if (close2)
			{
				fragment.Reverse();
			}
			state.Fragments.RemoveAt(i);
			if (atEnd)
			{
				for (int j = 1; j < fragment.Count; j++)
				{
					frag.Add(fragment[j]);
				}
			}
			else
			{
				for (int k = 1; k < fragment.Count; k++)
				{
					frag.Insert(0, fragment[k]);
				}
			}
			return true;
		}
		return false;
	}

	private bool TryCross(float a, float b, out float fraction)
	{
		fraction = 0f;
		if (a == -9999f || b == -9999f)
		{
			return false;
		}
		bool thresholdTerm = a < _threshold;
		bool thresholdTerm2 = b < _threshold;
		if (thresholdTerm == thresholdTerm2)
		{
			return false;
		}
		fraction = (_threshold - a) / (b - a);
		return true;
	}

	private static float SampleVertex(TileWithNeighbors<float> tiles, int ts, int r, int c)
	{
		RasterData<float> rasterData = tiles.Center;
		if (c >= ts)
		{
			c -= ts;
			if (r >= ts)
			{
				r -= ts;
				rasterData = tiles.LowerRight;
			}
			else
			{
				rasterData = tiles.Right;
			}
		}
		else if (r >= ts)
		{
			r -= ts;
			rasterData = tiles.Bottom;
		}
		if (rasterData == null)
		{
			return -9999f;
		}
		int cols = rasterData.RasterDefinition.Cols;
		int colsTerm = r * cols + c;
		ReadOnlySpan<float> floatSpan = rasterData.Data.Span;
		if ((uint)colsTerm >= (uint)floatSpan.Length)
		{
			return -9999f;
		}
		return floatSpan[colsTerm];
	}

	private static double VertexX(double left, double cs, int col, double fraction)
	{
		return left + ((double)col + 0.5 + fraction) * cs;
	}

	private static double VertexY(double top, double cs, int row, double fraction)
	{
		return top - ((double)row + 0.5 + fraction) * cs;
	}

	private static bool Close(Point a, Point b, double eps)
	{
		if (Math.Abs(a.X - b.X) < eps)
		{
			return Math.Abs(a.Y - b.Y) < eps;
		}
		return false;
	}

	private static VKey Key(Point p, double q)
	{
		return new VKey((long)Math.Round(p.X / q), (long)Math.Round(p.Y / q));
	}

	private static List<List<Point>> ChainSegments(List<(Point a, Point b)> segments, double cs)
	{
		double q = cs * 0.0001;
		int count = segments.Count;
		Dictionary<VKey, List<int>> incidence = new Dictionary<VKey, List<int>>();
		for (int i = 0; i < count; i++)
		{
			AddIncidence(Key(segments[i].a, q), i);
			AddIncidence(Key(segments[i].b, q), i);
		}
		bool[] boolValues = new bool[count];
		List<List<Point>> pointListList = new List<List<Point>>();
		for (int j = 0; j < count; j++)
		{
			if (!boolValues[j])
			{
				boolValues[j] = true;
				LinkedList<Point> linkedList = new LinkedList<Point>();
				linkedList.AddLast(segments[j].a);
				linkedList.AddLast(segments[j].b);
				ExtendChain(linkedList, segments, boolValues, incidence, q, fromTail: true);
				ExtendChain(linkedList, segments, boolValues, incidence, q, fromTail: false);
				pointListList.Add(new List<Point>(linkedList));
			}
		}
		return pointListList;
		void AddIncidence(VKey k, int ordinal)
		{
			if (!incidence.TryGetValue(k, out var incidence2))
			{
				incidence2 = new List<int>();
				incidence[k] = incidence2;
			}
			incidence2.Add(ordinal);
		}
	}

	private static void ExtendChain(LinkedList<Point> pts, List<(Point a, Point b)> segments, bool[] used, Dictionary<VKey, List<int>> incidence, double q, bool fromTail)
	{
		while (true)
		{
			VKey vKey = Key(fromTail ? pts.Last.Value : pts.First.Value, q);
			if (!incidence.TryGetValue(vKey, out var localValue))
			{
				break;
			}
			int ordinal = -1;
			foreach (int ordinal2 in localValue)
			{
				if (!used[ordinal2])
				{
					ordinal = ordinal2;
					break;
				}
			}
			if (ordinal >= 0)
			{
				used[ordinal] = true;
				(Point, Point) tuple = segments[ordinal];
				Point point;
				if (!Key(tuple.Item1, q).Equals(vKey))
				{
					(point, _) = tuple;
				}
				else
				{
					point = tuple.Item2;
				}
				Point point2 = point;
				if (fromTail)
				{
					pts.AddLast(point2);
				}
				else
				{
					pts.AddFirst(point2);
				}
				continue;
			}
			break;
		}
	}

	void ITileProcessor<float, ContourTileState, List<Polyline>>.Accumulate(in TileAccumulateContext<float, ContourTileState> context)
	{
		Accumulate(in context);
	}

	bool ITileProcessor<float, ContourTileState, List<Polyline>>.Resolve(in TileResolveContext<ContourTileState> context)
	{
		return Resolve(in context);
	}
}

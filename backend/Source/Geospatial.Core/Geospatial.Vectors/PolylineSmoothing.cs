using System;
using System.Buffers;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public static class PolylineSmoothing
{
	[ThreadStatic]
	private static Stack<(int First, int Last)> _dpStack;

	[ThreadStatic]
	private static List<Point> _chaikinScratch;

	public static void SimplifyDouglasPeucker(ReadOnlySpan<Point> input, double epsilon, List<Point> output)
	{
		output.Clear();
		int length = input.Length;
		if (length < 3 || !(epsilon > 0.0))
		{
			for (int i = 0; i < length; i++)
			{
				output.Add(input[i]);
			}
			return;
		}
		bool[] rent = ArrayPool<bool>.Shared.Rent(length);
		Array.Clear(rent, 0, length);
		rent[0] = true;
		rent[length - 1] = true;
		Stack<(int, int)> dpStack = _dpStack ?? (_dpStack = new Stack<(int, int)>());
		dpStack.Clear();
		dpStack.Push((0, length - 1));
		while (dpStack.Count > 0)
		{
			(int, int) tuple = dpStack.Pop();
			int tupleTerm = tuple.Item1;
			int count = tuple.Item2;
			Point point = input[tupleTerm];
			Point point2 = input[count];
			double point2PointTerm = point2.X - point.X;
			double point2PointTerm2 = point2.Y - point.Y;
			double computed = point2PointTerm * point2PointTerm + point2PointTerm2 * point2PointTerm2;
			double computed2 = 0.0;
			int unity = -1;
			if (computed > 0.0)
			{
				for (int j = tupleTerm + 1; j < count; j++)
				{
					Point point3 = input[j];
					double pointPoint3Term = Math.Abs(point2PointTerm * (point.Y - point3.Y) - point2PointTerm2 * (point.X - point3.X));
					if (pointPoint3Term > computed2)
					{
						computed2 = pointPoint3Term;
						unity = j;
					}
				}
				computed2 /= Math.Sqrt(computed);
			}
			else
			{
				for (int k = tupleTerm + 1; k < count; k++)
				{
					double distanceTo = point.DistanceTo(input[k]);
					if (distanceTo > computed2)
					{
						computed2 = distanceTo;
						unity = k;
					}
				}
			}
			if (unity != -1 && computed2 > epsilon)
			{
				rent[unity] = true;
				dpStack.Push((tupleTerm, unity));
				dpStack.Push((unity, count));
			}
		}
		for (int l = 0; l < length; l++)
		{
			if (rent[l])
			{
				output.Add(input[l]);
			}
		}
		ArrayPool<bool>.Shared.Return(rent);
	}

	public static void ChaikinSmooth(IReadOnlyList<Point> input, int iterations, double pinAngleDegrees, bool closed, List<Point> output)
	{
		output.Clear();
		if (input == null)
		{
			return;
		}
		int count = input.Count;
		for (int i = 0; i < count; i++)
		{
			output.Add(input[i]);
		}
		if (iterations < 1 || count < 3)
		{
			return;
		}
		bool closed2 = closed && output.Count >= 2 && output[0] == output[output.Count - 1];
		if (closed2)
		{
			output.RemoveAt(output.Count - 1);
		}
		double cosPin = Math.Cos(pinAngleDegrees * (Math.PI / 180.0));
		List<Point> chaikinScratch = _chaikinScratch ?? (_chaikinScratch = new List<Point>());
		List<Point> outputList = output;
		List<Point> chaikinScratchList = chaikinScratch;
		for (int j = 0; j < iterations; j++)
		{
			int count2 = outputList.Count;
			if (count2 < 3)
			{
				break;
			}
			chaikinScratchList.Clear();
			if (closed)
			{
				for (int k = 0; k < count2; k++)
				{
					Point prev = outputList[(k - 1 + count2) % count2];
					Point cur = outputList[k];
					Point ahead = outputList[(k + 1) % count2];
					AppendSmoothed(chaikinScratchList, prev, cur, ahead, cosPin);
				}
			}
			else
			{
				chaikinScratchList.Add(outputList[0]);
				for (int l = 1; l < count2 - 1; l++)
				{
					Point prev2 = outputList[l - 1];
					Point cur2 = outputList[l];
					Point ahead2 = outputList[l + 1];
					AppendSmoothed(chaikinScratchList, prev2, cur2, ahead2, cosPin);
				}
				chaikinScratchList.Add(outputList[count2 - 1]);
			}
			List<Point> chaikinScratch2 = chaikinScratchList;
			chaikinScratchList = outputList;
			outputList = chaikinScratch2;
		}
		if (outputList != output)
		{
			output.Clear();
			output.AddRange(outputList);
		}
		if (closed2 && output.Count > 0)
		{
			output.Add(output[0]);
		}
	}

	private static void AppendSmoothed(List<Point> next, Point prev, Point cur, Point ahead, double cosPin)
	{
		if (IsPinned(prev, cur, ahead, cosPin))
		{
			next.Add(cur);
			return;
		}
		next.Add(new Point(0.75 * cur.X + 0.25 * prev.X, 0.75 * cur.Y + 0.25 * prev.Y));
		next.Add(new Point(0.75 * cur.X + 0.25 * ahead.X, 0.75 * cur.Y + 0.25 * ahead.Y));
	}

	private static bool IsPinned(Point prev, Point cur, Point ahead, double cosPin)
	{
		double curPrevTerm = cur.X - prev.X;
		double curPrevTerm2 = cur.Y - prev.Y;
		double aheadCurTerm = ahead.X - cur.X;
		double aheadCurTerm2 = ahead.Y - cur.Y;
		double computed = (curPrevTerm * curPrevTerm + curPrevTerm2 * curPrevTerm2) * (aheadCurTerm * aheadCurTerm + aheadCurTerm2 * aheadCurTerm2);
		if (computed <= 0.0)
		{
			return false;
		}
		return curPrevTerm * aheadCurTerm + curPrevTerm2 * aheadCurTerm2 < cosPin * Math.Sqrt(computed);
	}

	public static double TurnDegrees(Point prev, Point cur, Point ahead)
	{
		double curPrevTerm = cur.X - prev.X;
		double curPrevTerm2 = cur.Y - prev.Y;
		double aheadCurTerm = ahead.X - cur.X;
		double aheadCurTerm2 = ahead.Y - cur.Y;
		double computed = Math.Sqrt(curPrevTerm * curPrevTerm + curPrevTerm2 * curPrevTerm2);
		double computed2 = Math.Sqrt(aheadCurTerm * aheadCurTerm + aheadCurTerm2 * aheadCurTerm2);
		if (computed <= 0.0 || computed2 <= 0.0)
		{
			return 0.0;
		}
		double computed3 = (curPrevTerm * aheadCurTerm + curPrevTerm2 * aheadCurTerm2) / (computed * computed2);
		if (computed3 > 1.0)
		{
			computed3 = 1.0;
		}
		else if (computed3 < -1.0)
		{
			computed3 = -1.0;
		}
		return Math.Acos(computed3) * (180.0 / Math.PI);
	}
}

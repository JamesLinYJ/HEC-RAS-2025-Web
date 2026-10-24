using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;

namespace Geospatial.PairedData;

public class FunctionalPairedDataF
{
	public class Shape
	{
		public bool IsRectangle;

		public int PrevPointIdx;

		public int NextPointIdx;

		public float Area;

		public PointF CenterOfMass;

		public Shape(bool isRect, int pidx, int nIdx, float area, PointF com)
		{
			IsRectangle = isRect;
			PrevPointIdx = pidx;
			NextPointIdx = nIdx;
			Area = area;
			CenterOfMass = com;
		}
	}

	public List<PointF> Points;

	public float Start => Points.First().X;

	public float End => Points.Last().X;

	public float Width => End - Start;

	public FunctionalPairedDataF(float[] x, float[] y)
	{
		if (x == null || y == null || x.Length != y.Length)
		{
			throw new ArgumentException();
		}
		Points = new List<PointF>(x.Length);
		for (int i = 0; i < x.Length; i++)
		{
			Points.Add(new PointF(x[i], y[i]));
		}
	}

	public PointF ComputeCentroid()
	{
		double xAreaSum = 0.0;
		double yAreaSum = 0.0;
		double netAreaSum = 0.0;
		for (int i = 1; i < Points.Count; i++)
		{
			PointF prev = Points[i - 1];
			PointF next = Points[i];
			ComputeCenterOfMassTrapezoid(prev, next, ref xAreaSum, ref yAreaSum, ref netAreaSum);
		}
		return new PointF((float)(xAreaSum / netAreaSum), (float)(yAreaSum / netAreaSum));
	}

	public double IntegrateArea()
	{
		double areaTrapezoidSum = 0.0;
		for (int i = 1; i < Points.Count; i++)
		{
			PointF prev = Points[i - 1];
			PointF cur = Points[i];
			areaTrapezoidSum += ComputeAreaTrapezoid(prev, cur);
		}
		return areaTrapezoidSum;
	}

	private static double ComputeAreaTrapezoid(PointF prev, PointF cur)
	{
		float curPrevTerm = cur.X - prev.X;
		if (cur.Y > 0f && prev.Y > 0f)
		{
			float curPrevTerm2 = Math.Min(cur.Y, prev.Y);
			float curPrevTerm3 = Math.Abs(cur.Y - prev.Y);
			return curPrevTerm2 * curPrevTerm + 0.5f * curPrevTerm3 * curPrevTerm;
		}
		if (cur.Y < 0f && prev.Y < 0f)
		{
			float curPrevTerm4 = Math.Max(cur.Y, prev.Y);
			float curPrevTerm5 = -1f * Math.Abs(cur.Y - prev.Y);
			return curPrevTerm4 * curPrevTerm + 0.5f * curPrevTerm5 * curPrevTerm;
		}
		if (cur.Y == 0f)
		{
			return prev.Y * curPrevTerm * 0.5f;
		}
		if (prev.Y == 0f)
		{
			return cur.Y * curPrevTerm * 0.5f;
		}
		float curPrevTerm6 = (cur.Y - prev.Y) / curPrevTerm;
		float prevTerm = Math.Abs(prev.Y / curPrevTerm6);
		float prevTerm2 = 0.5f * prevTerm * prev.Y;
		float curTerm = 0.5f * (curPrevTerm - prevTerm) * cur.Y;
		return prevTerm2 + curTerm;
	}

	public void ComputeForShape(Action<Shape> ReportShape)
	{
		for (int i = 1; i < Points.Count; i++)
		{
			OperateOnShape(i - 1, i, ReportShape);
		}
	}

	private void OperateOnShape(int prevIdx, int nextIdx, Action<Shape> ReportShape)
	{
		PointF pointF = Points[prevIdx];
		PointF pointF2 = Points[nextIdx];
		float pointF2PointFTerm = pointF2.X - pointF.X;
		float pointF2PointFTerm2 = pointF2.Y - pointF.Y;
		float pointFPointF2Term = Math.Min(pointF.Y, pointF2.Y);
		float pointFPointF2Term2 = Math.Max(pointF.Y, pointF2.Y);
		if (pointF.Y > 0f && pointF2.Y > 0f)
		{
			float computed = pointFPointF2Term;
			float area = computed * pointF2PointFTerm;
			float x = pointF.X + 0.5f * pointF2PointFTerm;
			float y = 0.5f * computed;
			ReportShape(new Shape(isRect: true, prevIdx, nextIdx, area, new PointF(x, y)));
			float computed2 = Math.Abs(pointF2PointFTerm2);
			float area2 = 0.5f * computed2 * pointF2PointFTerm;
			x = ((!(pointF.Y < pointF2.Y)) ? (pointF.X + 1f / 3f * pointF2PointFTerm) : (pointF.X + 2f / 3f * pointF2PointFTerm));
			y = pointFPointF2Term + 1f / 3f * computed2;
			ReportShape(new Shape(isRect: false, prevIdx, nextIdx, area2, new PointF(x, y)));
		}
		else if (pointF2.Y < 0f && pointF.Y < 0f)
		{
			float computed3 = pointFPointF2Term2;
			float area3 = Math.Abs(computed3 * pointF2PointFTerm);
			float x2 = pointF.X + 0.5f * pointF2PointFTerm;
			float y2 = 0.5f * computed3;
			ReportShape(new Shape(isRect: true, prevIdx, nextIdx, area3, new PointF(x2, y2)));
			float computed4 = Math.Abs(pointF2PointFTerm2);
			float area4 = 0.5f * computed4 * pointF2PointFTerm;
			x2 = ((!(pointF.Y > pointF2.Y)) ? (pointF.X + 1f / 3f * pointF2PointFTerm) : (pointF.X + 2f / 3f * pointF2PointFTerm));
			y2 = pointFPointF2Term2 - 1f / 3f * computed4;
			ReportShape(new Shape(isRect: false, prevIdx, nextIdx, area4, new PointF(x2, y2)));
		}
		else if (pointF2.Y == 0f)
		{
			float computed5 = Math.Abs(pointF2PointFTerm2);
			float area5 = 0.5f * computed5 * pointF2PointFTerm;
			float x3 = pointF.X + 1f / 3f * pointF2PointFTerm;
			float y3 = -1f / 3f * pointF2PointFTerm2;
			ReportShape(new Shape(isRect: false, prevIdx, nextIdx, area5, new PointF(x3, y3)));
		}
		else if (pointF.Y == 0f)
		{
			float computed6 = Math.Abs(pointF2PointFTerm2);
			float area6 = 0.5f * computed6 * pointF2PointFTerm;
			float x4 = pointF.X + 2f / 3f * pointF2PointFTerm;
			float y4 = 1f / 3f * pointF2PointFTerm2;
			ReportShape(new Shape(isRect: false, prevIdx, nextIdx, area6, new PointF(x4, y4)));
		}
		else
		{
			float computed7 = pointF2PointFTerm2 / pointF2PointFTerm;
			float pointFTerm = Math.Abs(pointF.Y / computed7);
			float computed8 = pointF2PointFTerm - pointFTerm;
			float area7 = 0.5f * pointFTerm * Math.Abs(pointF.Y);
			float area8 = 0.5f * computed8 * Math.Abs(pointF2.Y);
			float x5 = pointF.X + 1f / 3f * pointFTerm;
			float y5 = 1f / 3f * pointF.Y;
			float x6 = pointF2.X - 1f / 3f * computed8;
			float y6 = 1f / 3f * pointF2.Y;
			ReportShape(new Shape(isRect: false, prevIdx, nextIdx, area7, new PointF(x5, y5)));
			ReportShape(new Shape(isRect: false, prevIdx, nextIdx, area8, new PointF(x6, y6)));
		}
	}

	private static void ComputeCenterOfMassTrapezoid(PointF prev, PointF next, ref double xAreaSum, ref double yAreaSum, ref double netAreaSum)
	{
		float nextPrevTerm = next.X - prev.X;
		float nextPrevTerm2 = next.Y - prev.Y;
		float prevNextTerm = Math.Min(prev.Y, next.Y);
		float prevNextTerm2 = Math.Max(prev.Y, next.Y);
		float tempASum = 0f;
		float tempXSum = 0f;
		float tempYSum = 0f;
		Action<float, float, float> action = (float a, float cX, float cY) =>
		{
			tempXSum += a * cX;
			tempYSum += a * cY;
			tempASum += a;
		};
		if (prev.Y > 0f && next.Y > 0f)
		{
			float computed = prevNextTerm;
			float computed2 = computed * nextPrevTerm;
			float prevNextTerm3 = prev.X + 0.5f * nextPrevTerm;
			float computed3 = 0.5f * computed;
			action(computed2, prevNextTerm3, computed3);
			float computed4 = Math.Abs(nextPrevTerm2);
			float computed5 = 0.5f * computed4 * nextPrevTerm;
			prevNextTerm3 = ((!(prev.Y < next.Y)) ? (prev.X + 1f / 3f * nextPrevTerm) : (prev.X + 2f / 3f * nextPrevTerm));
			computed3 = prevNextTerm + 1f / 3f * computed4;
			action(computed5, prevNextTerm3, computed3);
		}
		else if (next.Y < 0f && prev.Y < 0f)
		{
			float computed6 = prevNextTerm2;
			float computed7 = Math.Abs(computed6 * nextPrevTerm);
			float prevNextTerm4 = prev.X + 0.5f * nextPrevTerm;
			float computed8 = 0.5f * computed6;
			action(computed7, prevNextTerm4, computed8);
			float computed9 = Math.Abs(nextPrevTerm2);
			float computed10 = 0.5f * computed9 * nextPrevTerm;
			prevNextTerm4 = ((!(prev.Y > next.Y)) ? (prev.X + 1f / 3f * nextPrevTerm) : (prev.X + 2f / 3f * nextPrevTerm));
			computed8 = prevNextTerm2 - 1f / 3f * computed9;
			action(computed10, prevNextTerm4, computed8);
		}
		else if (next.Y == 0f)
		{
			float computed11 = Math.Abs(nextPrevTerm2);
			float computed12 = 0.5f * computed11 * nextPrevTerm;
			float prevTerm = prev.X + 1f / 3f * nextPrevTerm;
			float computed13 = -1f / 3f * nextPrevTerm2;
			action(computed12, prevTerm, computed13);
		}
		else if (prev.Y == 0f)
		{
			float computed14 = Math.Abs(nextPrevTerm2);
			float computed15 = 0.5f * computed14 * nextPrevTerm;
			float prevTerm2 = prev.X + 2f / 3f * nextPrevTerm;
			float computed16 = 1f / 3f * nextPrevTerm2;
			action(computed15, prevTerm2, computed16);
		}
		else
		{
			float computed17 = nextPrevTerm2 / nextPrevTerm;
			float prevTerm3 = Math.Abs(prev.Y / computed17);
			float computed18 = nextPrevTerm - prevTerm3;
			float prevTerm4 = 0.5f * prevTerm3 * Math.Abs(prev.Y);
			float nextTerm = 0.5f * computed18 * Math.Abs(next.Y);
			float prevTerm5 = prev.X + 1f / 3f * prevTerm3;
			float prevTerm6 = -1f / 3f * prev.Y;
			float nextTerm2 = next.X - 1f / 3f * computed18;
			float nextTerm3 = 1f / 3f * next.Y;
			action(prevTerm4, prevTerm5, prevTerm6);
			action(nextTerm, nextTerm2, nextTerm3);
		}
		xAreaSum += tempXSum;
		yAreaSum += tempYSum;
		netAreaSum += tempASum;
	}
}

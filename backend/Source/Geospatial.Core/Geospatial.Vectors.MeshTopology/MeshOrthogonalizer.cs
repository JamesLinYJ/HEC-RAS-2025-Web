using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class MeshOrthogonalizer : OptimizeConstructed
{
	public int method = 1;

	public double scaler = 1.0;

	public double alpha = 0.95;

	public bool sliding = true;

	public bool useArea = true;

	public int smoothmethod = 1;

	public bool nudgeTriangles = true;

	private MeshScaledSmoother smoother;

	public MeshOrthogonalizer(SlidingMesh mesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
		smoother = new MeshScaledSmoother(mesh, nodes);
		smoother.weightByArea = useArea;
	}

	public override bool ConstructedPoint(int nIdx, out double x, out double y)
	{
		x = 0.0;
		y = 0.0;
		int ordinal = 0;
		List<int> intList = new List<int>();
		foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
		{
			if (!(mesh.FaceTargetLength[nodeToHalfEdge.fIdx] < 0f))
			{
				intList.Add(mesh.HalfEdgeNodeForward(nodeToHalfEdge));
				ordinal++;
			}
		}
		if (method == 0 && smoothmethod == 0)
		{
			(x, y) = NudgeNodeDelft(nIdx, mesh, alpha);
		}
		else
		{
			double computed = 0.0;
			double computed2 = 0.0;
			double x2 = 0.0;
			double y2 = 0.0;
			if (method != 0)
			{
				(computed, computed2) = NudgeNodeOrthogonal(nIdx, mesh, nudgeTriangles);
			}
			else
			{
				(computed, computed2) = OrthogonalizeNodeDelft(nIdx, mesh);
			}
			if (smoothmethod == 0)
			{
				(x2, y2) = SmoothNodeDelft(nIdx, mesh);
			}
			else
			{
				smoother.ConstructedPoint(nIdx, out x2, out y2);
			}
			x = alpha * computed + (1.0 - alpha) * x2;
			y = alpha * computed2 + (1.0 - alpha) * y2;
		}
		if (x != 0.0)
		{
			double x3 = mesh.NodePoints[nIdx].X;
			double y3 = mesh.NodePoints[nIdx].Y;
			x = x3 + (x - x3) * scaler;
			y = y3 + (y - y3) * scaler;
			return true;
		}
		return false;
	}

	public static (double x, double y) NudgeNodeOrthogonal(int nIdx, SlidingMesh mesh, bool nudgeTriangles)
	{
		double point2Term = 0.0;
		double point2Term2 = 0.0;
		Point point = mesh.NodePoints[nIdx];
		IEnumerable<int> enumerable = mesh.NodeToCells(nIdx);
		int ordinal = 0;
		Point point2 = default;
		foreach (int enumerable2 in enumerable)
		{
			List<int> cellToNodes = mesh.CellToNodes(enumerable2);
			if (!nudgeTriangles && cellToNodes.Count <= 3)
			{
				continue;
			}
			Point point3 = mesh.CellNudgedCircumcenter(enumerable2, SlidingMesh.DefaultCircumcenterClearance);
			if (cellToNodes.Count == 3 && mesh.CellCircumcenter(enumerable2, allPoints: true) == point3)
			{
				continue;
			}
			double distanceToSum = 0.0;
			int ordinal2 = 0;
			foreach (int cellToNode in cellToNodes)
			{
				distanceToSum += mesh.NodePoints[cellToNode].DistanceTo(point3);
				ordinal2++;
			}
			double distanceToSumTerm = distanceToSum / (double)ordinal2;
			Vector vector = (point - point3).UnitVector();
			ordinal++;
			point2 += point3 + distanceToSumTerm * vector;
		}
		if (ordinal > 0)
		{
			point2Term = point2.X / (double)ordinal;
			point2Term2 = point2.Y / (double)ordinal;
		}
		return (x: point2Term, y: point2Term2);
	}

	public static (double x, double y) NudgeNodeDelft(int n, SlidingMesh g, double alpha = 0.5)
	{
		if (alpha < 0.0 || alpha > 1.0)
		{
			alpha = Math.Max(0.0, Math.Min(1.0, alpha));
		}
		double computed = 0.0;
		IEnumerable<int> enumerable = g.NodeToCells(n);
		double x = g.NodePoints[n].X;
		double y = g.NodePoints[n].Y;
		double computed2 = 0.0;
		double computed3 = 0.0;
		double computed4 = 0.0;
		double computed5 = 0.0;
		int ordinal = 0;
		int ordinal2 = 0;
		foreach (int enumerable2 in enumerable)
		{
			List<int> cellToNodes = g.CellToNodes(enumerable2);
			if (cellToNodes.Count == 4)
			{
				double computed6 = 0.0;
				double computed7 = 0.0;
				int ordinal3 = 0;
				foreach (int cellToNode in cellToNodes)
				{
					if (cellToNode != n)
					{
						computed6 += g.NodePoints[cellToNode].X;
						computed7 += g.NodePoints[cellToNode].Y;
						ordinal3++;
					}
				}
				double computed8 = computed6 / (double)ordinal3;
				double computed9 = computed7 / (double)ordinal3;
				computed2 += computed8 - x;
				computed3 += computed9 - y;
				ordinal++;
			}
			Point point = g.CellNudgedCircumcenter(enumerable2, 0.25);
			computed4 += point.X - x;
			computed5 += point.Y - y;
			ordinal2++;
		}
		if (ordinal > 0)
		{
			computed2 /= (double)ordinal;
			computed3 /= (double)ordinal;
		}
		if (ordinal2 > 0)
		{
			computed4 /= (double)ordinal2;
			computed5 /= (double)ordinal2;
		}
		double alphaTerm = alpha * computed2 + (1.0 - alpha) * computed4;
		double alphaTerm2 = alpha * computed3 + (1.0 - alpha) * computed5;
		double computed10 = 0.5;
		double computed11 = x + alphaTerm * computed10;
		computed = y + alphaTerm2 * computed10;
		return (x: computed11, y: computed);
	}

	public static (double x, double y) OrthogonalizeNodeDelft(int n, SlidingMesh g)
	{
		double computed = 0.0;
		IEnumerable<int> enumerable = g.NodeToCells(n);
		double x = g.NodePoints[n].X;
		double y = g.NodePoints[n].Y;
		double computed2 = 0.0;
		double computed3 = 0.0;
		int ordinal = 0;
		foreach (int enumerable2 in enumerable)
		{
			List<int> cellToNodes = g.CellToNodes(enumerable2);
			if (cellToNodes.Count != 4)
			{
				continue;
			}
			double computed4 = 0.0;
			double computed5 = 0.0;
			int ordinal2 = 0;
			foreach (int cellToNode in cellToNodes)
			{
				if (cellToNode != n)
				{
					computed4 += g.NodePoints[cellToNode].X;
					computed5 += g.NodePoints[cellToNode].Y;
					ordinal2++;
				}
			}
			double computed6 = computed4 / (double)ordinal2;
			double computed7 = computed5 / (double)ordinal2;
			computed2 += computed6 - x;
			computed3 += computed7 - y;
			ordinal++;
		}
		if (ordinal > 0)
		{
			computed2 /= (double)ordinal;
			computed3 /= (double)ordinal;
		}
		double computed8 = 0.5;
		double computed9 = x + computed2 * computed8;
		computed = y + computed3 * computed8;
		return (x: computed9, y: computed);
	}

	public static (double x, double y) SmoothNodeDelft(int n, SlidingMesh g)
	{
		double computed = 0.0;
		IEnumerable<int> enumerable = g.NodeToCells(n);
		double x = g.NodePoints[n].X;
		double y = g.NodePoints[n].Y;
		double computed2 = 0.0;
		double computed3 = 0.0;
		int ordinal = 0;
		foreach (int enumerable2 in enumerable)
		{
			g.CellToNodes(enumerable2);
			Point point = g.CellNudgedCircumcenter(enumerable2, SlidingMesh.DefaultCircumcenterClearance);
			computed2 += point.X - x;
			computed3 += point.Y - y;
			ordinal++;
		}
		if (ordinal > 0)
		{
			computed2 /= (double)ordinal;
			computed3 /= (double)ordinal;
		}
		double computed4 = 0.5;
		double computed5 = x + computed2 * computed4;
		computed = y + computed3 * computed4;
		return (x: computed5, y: computed);
	}
}

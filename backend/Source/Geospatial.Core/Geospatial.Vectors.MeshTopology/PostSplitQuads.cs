using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class PostSplitQuads : MeshPostprocess
{
	public double circumcenterThreshold = 0.15;

	public PostSplitQuads(SlidingMesh mesh)
		: base(mesh)
	{
	}

	public PostSplitQuads(SlidingMesh mesh, double circumcenterThreshold)
		: base(mesh)
	{
		this.circumcenterThreshold = circumcenterThreshold;
	}

	public bool TestShouldSplit(int cIdx, out int nodeA, out int nodeB)
	{
		nodeA = -1;
		nodeB = -1;
		if (mesh.CellFaces[cIdx].Count != 4)
		{
			return false;
		}
		List<int> cellToNodes = mesh.CellToNodes(cIdx);
		List<Point> points = cellToNodes.Select((int i) => mesh.NodePoints[i]).ToList();
		List<Point> pointsList = new List<Point>();
		for (int intervalIndex = 0; intervalIndex < points.Count; intervalIndex++)
		{
			pointsList.Add(Triangle.ComputeCircumCircle(points[intervalIndex], points[(intervalIndex + 1) % points.Count], points[(intervalIndex + 2) % points.Count]).Center);
		}
		Vector other = (points[2] - points[0]).UnitVector().Perpendicular(clockwise: false);
		double dot = (pointsList[2] - pointsList[0]).Dot(other);
		other = (points[3] - points[1]).UnitVector().Perpendicular(clockwise: false);
		double dot2 = (pointsList[3] - pointsList[1]).Dot(other);
		double sqrtCellSimpleArea = Math.Sqrt(mesh.CellSimpleArea(cIdx));
		if (dot > dot2)
		{
			if (dot > circumcenterThreshold * sqrtCellSimpleArea)
			{
				nodeA = cellToNodes[0];
				nodeB = cellToNodes[2];
				return true;
			}
		}
		else if (dot2 > circumcenterThreshold * sqrtCellSimpleArea)
		{
			nodeA = cellToNodes[1];
			nodeB = cellToNodes[3];
			return true;
		}
		return false;
	}

	public override void Apply(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int ordinal = 0;
		foreach (int cellIterator in mesh.CellIterator())
		{
			if (TestShouldSplit(cellIterator, out var nodeA, out var nodeB) && mesh.SplitCell(cellIterator, nodeA, nodeB))
			{
				ordinal++;
			}
		}
	}
}

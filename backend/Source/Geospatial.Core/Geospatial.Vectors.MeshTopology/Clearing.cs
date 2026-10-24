using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class Clearing
{
	public class OptNegArea : OptimizeNodeMetric
	{
		public OptNegArea(SlidingMesh mesh, IList<int> nodes = null)
			: base(mesh, nodes)
		{
			checkMoves = false;
		}

		public override double NodeLoss(int nIdx, Point p)
		{
			Point nodePoint = mesh.NodePoints[nIdx];
			mesh.NodePoints[nIdx] = p;
			double cellSimpleAreaSum = 0.0;
			foreach (int nodeToCellIndex in mesh.NodeToCells(nIdx))
			{
				double cellSimpleArea = mesh.CellSimpleArea(nodeToCellIndex);
				if (cellSimpleArea < 0.0)
				{
					cellSimpleAreaSum -= cellSimpleArea;
				}
			}
			mesh.NodePoints[nIdx] = nodePoint;
			return cellSimpleAreaSum;
		}
	}

	public class OptInternalAngles : OptimizeNodeMetric
	{
		private List<(int, int, int, double)>[] targets;

		public OptInternalAngles(SlidingMesh mesh, List<(int, int, int, double)>[] targets, IList<int> nodes = null)
			: base(mesh, nodes)
		{
			this.targets = targets;
			checkMoves = false;
		}

		public override double NodeLoss(int nIdx, Point p)
		{
			Point nodePoint = mesh.NodePoints[nIdx];
			mesh.NodePoints[nIdx] = p;
			double internalAnglePolynomialSum = 0.0;
			foreach (var target in targets[nIdx])
			{
				double internalAngle = InternalAngle(mesh, target.Item1, target.Item2, target.Item3);
				if (internalAngle < 0.0)
				{
					internalAnglePolynomialSum += internalAngle * internalAngle;
				}
			}
			mesh.NodePoints[nIdx] = nodePoint;
			return internalAnglePolynomialSum;
		}

		public static double InternalAngle(SlidingMesh m, int nA, int nB, int nC)
		{
			Vector vector = m.NodePoints[nB] - m.NodePoints[nA];
			Vector other = m.NodePoints[nC] - m.NodePoints[nB];
			return vector.CrossProduct(other) / (vector.Length * other.Length);
		}
	}

	public Dictionary<Point, int> pointToNode;

	public ConceptualMesh cmesh;

	public SlidingMesh mesh;

	public Clearing()
	{
		pointToNode = new Dictionary<Point, int>();
	}

	public int FindOrAddNode(Point pnt, ConstraintState constraint, bool checkFixed = false)
	{
		if (!pointToNode.TryGetValue(pnt, out var pointToNode2))
		{
			pointToNode2 = mesh.AddNode(pnt);
			pointToNode[pnt] = pointToNode2;
			mesh.NodeConstraints[pointToNode2] = constraint;
			if (constraint >= (ConstraintState)0)
			{
				if (checkFixed && (pnt == mesh.constraints[(int)constraint].Polyline.First() || pnt == mesh.constraints[(int)constraint].Polyline.Last()))
				{
					mesh.NodeConstraints[pointToNode2] = ConstraintState.FIXED;
				}
				else
				{
					mesh.UpdatePointOnPolyline(pointToNode2);
				}
			}
		}
		return pointToNode2;
	}
}

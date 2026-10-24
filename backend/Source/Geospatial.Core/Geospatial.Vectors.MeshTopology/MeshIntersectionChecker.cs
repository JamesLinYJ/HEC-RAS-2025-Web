using System.Collections.Generic;
using Geospatial.DataStructures;

namespace Geospatial.Vectors.MeshTopology;

public class MeshIntersectionChecker
{
	public struct MeshIntersection(int faceA = -1, int faceB = -1, int node = -1, int cell = -1, int arc = -1)
	{
		public int faceA = faceA;

		public int faceB = faceB;

		public int node = node;

		public int cell = cell;

		public int arc = arc;

		public Point point = default;

		public override string ToString()
		{
			string messageSum = "MeshIntersection(";
			if (faceA >= 0)
			{
				messageSum += $"face={faceA}, ";
			}
			if (faceB >= 0)
			{
				messageSum += $"face={faceB}, ";
			}
			if (cell >= 0)
			{
				messageSum += $"cell={cell}, ";
			}
			if (node >= 0)
			{
				messageSum += $"node={node}, ";
			}
			if (arc >= 0)
			{
				messageSum += $"arc={arc}, ";
			}
			return messageSum.Substring(0, messageSum.Length - 2) + ")";
		}
	}

	private SlidingMesh mesh;

	protected SpatialIndex<int> _faceIndex;

	protected SpatialIndex<int> _pointIndex;

	public SpatialIndex<int> FaceIndex
	{
		get
		{
			if (_faceIndex == null)
			{
				_faceIndex = mesh.FaceIndex();
			}
			return _faceIndex;
		}
	}

	public SpatialIndex<int> PointIndex
	{
		get
		{
			if (_pointIndex == null)
			{
				_pointIndex = mesh.PointIndex();
			}
			return _pointIndex;
		}
	}

	public MeshIntersectionChecker(SlidingMesh mesh)
	{
		this.mesh = mesh;
	}

	public bool Check(out List<MeshIntersection> intersections)
	{
		intersections = new List<MeshIntersection>();
		CheckFaceFaceIntersection(ref intersections);
		CheckNodeCellIntersection(ref intersections);
		return intersections.Count == 0;
	}

	public bool CheckFaceFaceIntersection(ref List<MeshIntersection> intersections)
	{
		bool result = true;
		SpatialIndex<int> faceIndex = FaceIndex;
		foreach (int faceIterator in mesh.FaceIterator())
		{
			foreach (int element in faceIndex.GetElements(mesh.FaceSegment(faceIterator)))
			{
				if (element < faceIterator && FaceFaceIntersection(faceIterator, element))
				{
					intersections.Add(new MeshIntersection(faceIterator, element));
					result = false;
				}
			}
		}
		return result;
	}

	public bool CheckNodeCellIntersection(ref List<MeshIntersection> intersections)
	{
		bool result = true;
		SpatialIndex<int> pointIndex = PointIndex;
		foreach (int cellIterator in mesh.CellIterator())
		{
			Polygon polygon = mesh.CellSimplePolygon(cellIterator);
			foreach (int element in pointIndex.GetElements(polygon.Extent))
			{
				if (!mesh.NodeInCellTopology(element, cellIterator) && polygon.Contains(mesh.NodePoints[element]))
				{
					result = false;
					intersections.Add(new MeshIntersection(-1, -1, element, cellIterator));
				}
			}
		}
		return result;
	}

	public bool RaysIntersect(Vector vecA, Vector vecB, double epsilon = 1E-10)
	{
		vecA.Normalize();
		vecB.Normalize();
		return 1.0 - vecA.Dot(vecB) < double.Epsilon;
	}

	public bool FaceFaceIntersection(int fIdx1, int fIdx2)
	{
		Segment segment = mesh.FaceSegment(fIdx1);
		Segment segment2 = mesh.FaceSegment(fIdx2);
		if (!segment.MBROverlaps(segment2))
		{
			return false;
		}
		int a = mesh.FaceNodes[fIdx1].A;
		int b = mesh.FaceNodes[fIdx1].B;
		int a2 = mesh.FaceNodes[fIdx2].A;
		int b2 = mesh.FaceNodes[fIdx2].B;
		if (a == a2)
		{
			if (b == b2)
			{
				return true;
			}
			return RaysIntersect(segment.B - segment.A, segment2.B - segment2.A);
		}
		if (a == b2)
		{
			if (b == a2)
			{
				return true;
			}
			return RaysIntersect(segment.B - segment.A, segment2.A - segment2.B);
		}
		if (b == a2)
		{
			return RaysIntersect(segment.A - segment.B, segment2.B - segment2.A);
		}
		if (b == b2)
		{
			return RaysIntersect(segment.A - segment.B, segment2.A - segment2.B);
		}
		return segment.Intersects(segment2);
	}

	public bool CheckAgainstArcs(ConceptualMesh cmesh, out List<MeshIntersection> intersections, double eps = 1E-05)
	{
		intersections = new List<MeshIntersection>();
		SpatialIndex<int> faceIndex = FaceIndex;
		foreach (int faceIterator in cmesh.FaceIterator())
		{
			Polyline polyline = cmesh.FaceSmoothLine[faceIterator];
			foreach (int element in faceIndex.GetElements(polyline))
			{
				ConstraintState constraintState = mesh.FaceConstraints[element];
				if (constraintState >= (ConstraintState)0 && mesh.constraints[(int)constraintState].ArcIdx == faceIterator)
				{
					continue;
				}
				foreach (LineIntersection allIntersection in polyline.GetAllIntersections(mesh.FaceSegment(element)))
				{
					if (!(allIntersection.BIntersection.Fraction <= eps) && !(allIntersection.BIntersection.Fraction >= 1.0 - eps))
					{
						MeshIntersection meshIntersection = new MeshIntersection(element, -1, -1, -1, faceIterator);
						meshIntersection.point = allIntersection.Point;
						intersections.Add(meshIntersection);
					}
				}
			}
		}
		return intersections.Count == 0;
	}
}

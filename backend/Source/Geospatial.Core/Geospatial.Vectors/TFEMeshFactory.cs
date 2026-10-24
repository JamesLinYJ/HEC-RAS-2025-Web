using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Geospatial.DataStructures;
using Utility;
using Utility.Progress;

namespace Geospatial.Vectors;

public class TFEMeshFactory
{
	public const string DiagBaseMeshTin = "Base Mesh Tin";

	public const string DiagMeshTinWithFaces = "Mesh Tin 1 (Faces)";

	public const string DiagMeshTinWithPerim = "Mesh Tin 2 (Perimeter)";

	public const string DiagMeshTinWithBlines = "Mesh Tin 3 (Breaklines)";

	private double _filterToleranceTIN;

	private Mesh _mesh;

	public bool TrackDiagnostics;

	public MeshDiagnosticInfo DiagnosticInfo;

	public static readonly int SuperPointCount = 8;

	private Extent Extent => _mesh.Extent;

	private List<FacePoint> FacePoints { get; set; } = new List<FacePoint>();

	private List<Face> Faces { get; set; } = new List<Face>();

	private Cell[] Cells { get; set; }

	private Polygon Perimeter { get; set; }

	public Mesh CreateMesh(Polygon perimeter, IList<Point> cellCenters, IList<Polyline> breaklines, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (TrackDiagnostics)
		{
			DiagnosticInfo = new MeshDiagnosticInfo();
		}
		_mesh = new Mesh();
		_mesh.Perimeter = perimeter;
		_filterToleranceTIN = TinFactory.ComputeNearPointTolerance(perimeter.Extent);
		List<int> duplicatePoints = new List<int>();
		Extent extent = perimeter.Extent;
		extent = MeshFactoryOperations.RatioMBR(cellCenters, extent);
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory(new TriangulationParameters
		{
			Tolerance = _filterToleranceTIN,
			EnforcedMBR = extent
		});
		delaunayTinFactory.MarkDuplicatePoints = (int pt) =>
		{
			duplicatePoints.Add(pt);
		};
		delaunayTinFactory.AddMassPoints(cellCenters);
		Tin tin = delaunayTinFactory.ComputeTIN(reporter);
		if (TrackDiagnostics)
		{
			DiagnosticInfo.MeshTin = tin;
		}
		Cells = new Cell[cellCenters.Count];
		FacePoints = new List<FacePoint>(cellCenters.Count * 2);
		if (TrackDiagnostics)
		{
			DiagnosticInfo.Tins["Base Mesh Tin"] = tin.Clone();
			DiagnosticInfo.StepByStep.Add(tin.Clone());
		}
		BurnFaces(tin, cellCenters, reporter);
		if (TrackDiagnostics)
		{
			Tin tin2 = tin.Clone();
			DiagnosticInfo.StepByStep.Add(tin2);
			DiagnosticInfo.Tins["Mesh Tin 1 (Faces)"] = tin2;
		}
		Burn(tin, perimeter, isPerimeter: true);
		if (TrackDiagnostics)
		{
			Tin tin3 = tin.Clone();
			DiagnosticInfo.StepByStep.Add(tin3);
			DiagnosticInfo.Tins["Mesh Tin 2 (Perimeter)"] = tin3;
		}
		Burn(tin, breaklines, isPerimeter: false);
		if (TrackDiagnostics)
		{
			Tin tin4 = tin.Clone();
			DiagnosticInfo.StepByStep.Add(tin4);
			DiagnosticInfo.Tins["Mesh Tin 3 (Breaklines)"] = tin4;
		}
		return _mesh;
	}

	private void BurnFaces(Tin meshTin, IList<Point> cells, ProgressReporter reporter)
	{
		List<Segment> segments = new List<Segment>();
		int t = 0;
		for (int i = 0; i < cells.Count; i++)
		{
			Point point = cells[i];
			meshTin.FindTriangleAndNearestPointIndex(point, ref t, out var p);
			List<Tuple<int, int, int>> triEdgeTriCCW = meshTin.TriEdgeTriCCW(p, t);
			for (int j = 0; j < triEdgeTriCCW.Count; j++)
			{
				int triEdgeTriCCWTerm = triEdgeTriCCW[j].Item1;
				int triEdgeTriCCWTerm2 = triEdgeTriCCW[j].Item2;
				int pointToCellIndex = PointToCellIndex(triEdgeTriCCWTerm2);
				int triEdgeTriCCWTerm3 = triEdgeTriCCW[j].Item3;
				if (pointToCellIndex < 0 || pointToCellIndex >= cells.Count || triEdgeTriCCWTerm2 < p)
				{
					continue;
				}
				Point ptA = meshTin.Triangles[triEdgeTriCCWTerm].CircumCircleCenter();
				Point point2 = meshTin.Triangles[triEdgeTriCCWTerm3].CircumCircleCenter();
				double distanceToPointsTerm = meshTin.Points[p].DistanceTo(meshTin.Points[triEdgeTriCCWTerm2]) * 0.05;
				if (!(ptA.DistanceTo(point2) < distanceToPointsTerm))
				{
					Segment segment = new Segment(ptA, point2);
					bool isSuperTriangle = IsSuperTriangle(meshTin, triEdgeTriCCWTerm);
					bool isSuperTriangle2 = IsSuperTriangle(meshTin, triEdgeTriCCWTerm3);
					if (isSuperTriangle)
					{
						Point b = segment.B;
						double length = segment.Length;
						Segment segment2 = new Segment(meshTin.Points[p], meshTin.Points[triEdgeTriCCWTerm2]).PerpendicularSegment(length);
						Vector delta = segment2.A.To(b);
						segment = segment2.Translate(delta);
					}
					else if (isSuperTriangle2)
					{
						Point a = segment.A;
						double length2 = segment.Length;
						Segment segment3 = new Segment(meshTin.Points[p], meshTin.Points[triEdgeTriCCWTerm2]).PerpendicularSegment(length2).Reverse();
						Vector delta2 = segment3.A.To(a);
						segment = segment3.Translate(delta2);
					}
					segments.Add(segment);
					if (TrackDiagnostics)
					{
						DiagnosticInfo.BurnedSegments.Add(new BurnedSegmentDiagnosticInfo(segment, i, pointToCellIndex));
					}
				}
			}
		}
		SimpleTinInsertion simpleTinInsertion = new SimpleTinInsertion(meshTin);
		if (TrackDiagnostics)
		{
			simpleTinInsertion.TrackDiagnosticSteps((Tin tt) =>
			{
				DiagnosticInfo.StepByStep.Add(tt.Clone());
			});
		}
		simpleTinInsertion.InsertSegments(segments, isPerimeter: false);
	}

	private bool IsSuperTriangle(Tin meshTin, int tIdx)
	{
		TriangleDefinition triangleDefinition = meshTin.TriangleDefinition[tIdx];
		if (triangleDefinition.p0 >= SuperPointCount && triangleDefinition.p1 >= SuperPointCount)
		{
			return triangleDefinition.p2 < SuperPointCount;
		}
		return true;
	}

	private void Burn(Tin meshTin, IReadOnlyList<Segment> segments, bool isPerimeter)
	{
		if (segments == null)
		{
			return;
		}
		SimpleTinInsertion simpleTinInsertion = new SimpleTinInsertion(meshTin);
		if (TrackDiagnostics)
		{
			simpleTinInsertion.TrackDiagnosticSteps((Tin tt) =>
			{
				DiagnosticInfo.StepByStep.Add(tt.Clone());
			});
		}
		simpleTinInsertion.InsertSegments(segments, isPerimeter);
	}

	private void Burn(Tin meshTin, IList<Polyline> polylines, bool isPerimeter)
	{
		if (polylines == null)
		{
			return;
		}
		foreach (Polyline polyline in polylines)
		{
			Burn(meshTin, polyline, isPerimeter);
		}
	}

	private List<int> Floodfill(Tin devTin, int startFromTri, int[] triangleValues, Dictionary<int, byte> triangleEdgesBlocked, int stopIfTriangleIndexFound = -1)
	{
		int triangleValue = triangleValues[startFromTri];
		HashSet<int> intSet = new HashSet<int>();
		intSet.Add(startFromTri);
		List<int> intList = new List<int>();
		intList.Add(startFromTri);
		while (intList.Count > 0)
		{
			List<int> intListList = new List<int>();
			foreach (int ordinal in intList)
			{
				TriangleDefinition triangleDefinition = devTin.TriangleDefinition[ordinal];
				int at = triangleDefinition.at0;
				int at2 = triangleDefinition.at1;
				int at3 = triangleDefinition.at2;
				triangleEdgesBlocked.TryGetValue(ordinal, out var localValue);
				bool condition = false;
				bool condition2 = false;
				bool condition3 = false;
				if ((localValue & 1) > 0)
				{
					condition = true;
				}
				if ((localValue & 2) > 0)
				{
					condition2 = true;
				}
				if ((localValue & 4) > 0)
				{
					condition3 = true;
				}
				if (at != -1 && triangleValues[at] == triangleValue && !devTin.TriangleDefinition[at].IsDeleted && !condition && !intSet.Contains(at))
				{
					intSet.Add(at);
					intListList.Add(at);
					if (at == stopIfTriangleIndexFound)
					{
						break;
					}
				}
				if (at2 != -1 && triangleValues[at2] == triangleValue && !devTin.TriangleDefinition[at2].IsDeleted && !condition2 && !intSet.Contains(at2))
				{
					intSet.Add(at2);
					intListList.Add(at2);
					if (at2 == stopIfTriangleIndexFound)
					{
						break;
					}
				}
				if (at3 != -1 && triangleValues[at3] == triangleValue && !devTin.TriangleDefinition[at3].IsDeleted && !condition3 && !intSet.Contains(at3))
				{
					intSet.Add(at3);
					intListList.Add(at3);
					if (at3 == stopIfTriangleIndexFound)
					{
						break;
					}
				}
			}
			intList = intListList;
		}
		return intSet.ToList();
	}

	private void NeutralizeRecursive(Tin devTin, int ptIndexStartFrom, int[] triOwner, List<int>[] cellTris, Dictionary<int, byte> triangleEdgesBlocked, List<Polygon> bucketPolys = null)
	{
		int neutralTrianglesTerm = -1;
		while (true)
		{
			List<int> neutralTriangles = GetNeutralTriangles(devTin, triOwner);
			if (neutralTriangles.Count == neutralTrianglesTerm)
			{
				List<Point> points = new List<Point>();
				{
					foreach (int neutralTriangle in neutralTriangles)
					{
						points.Add(devTin.Triangles[neutralTriangle].Centroid());
					}
					break;
				}
			}
			neutralTrianglesTerm = neutralTriangles.Count;
			if (neutralTriangles.Count != 0)
			{
				NeutralizeTriangles(devTin, ptIndexStartFrom, triOwner, cellTris, triangleEdgesBlocked, neutralTriangles, bucketPolys);
				continue;
			}
			break;
		}
	}

	private void NeutralizeTriangles(Tin devTin, int ptIndexStartFrom, int[] triOwner, List<int>[] cellTris, Dictionary<int, byte> triangleEdgesBlocked, List<int> neutralTriangles, List<Polygon> bucketPolys = null)
	{
		BitArray bitArray = new BitArray(neutralTriangles.Count);
		for (int i = 0; i < neutralTriangles.Count; i++)
		{
			if (bitArray[i])
			{
				continue;
			}
			int startFromTri = neutralTriangles[i];
			List<int> floodfill = Floodfill(devTin, startFromTri, triOwner, triangleEdgesBlocked);
			bucketPolys?.Add(MergeTrianglesToPolygon(devTin, floodfill));
			Dictionary<int, Point> pointByInt = new Dictionary<int, Point>();
			foreach (int floodfill2 in floodfill)
			{
				bitArray[neutralTriangles.IndexOf(floodfill2)] = true;
				TriangleDefinition triangleDefinition = devTin.TriangleDefinition[floodfill2];
				int at = triangleDefinition.at0;
				int at2 = triangleDefinition.at1;
				int at3 = triangleDefinition.at2;
				int triOwner2 = ((at == -1) ? (-1) : triOwner[triangleDefinition.at0]);
				int triOwner3 = ((at2 == -1) ? (-1) : triOwner[triangleDefinition.at1]);
				int triOwner4 = ((at3 == -1) ? (-1) : triOwner[triangleDefinition.at2]);
				byte localValue = 0;
				triangleEdgesBlocked.TryGetValue(floodfill2, out localValue);
				bool condition = false;
				bool condition2 = false;
				bool condition3 = false;
				if ((localValue & 1) > 0)
				{
					condition = true;
				}
				if ((localValue & 2) > 0)
				{
					condition2 = true;
				}
				if ((localValue & 4) > 0)
				{
					condition3 = true;
				}
				if (triOwner2 != -1 && !condition && !pointByInt.ContainsKey(triOwner2))
				{
					pointByInt.Add(triOwner2, devTin.Points[ptIndexStartFrom + triOwner2]);
				}
				if (triOwner3 != -1 && !condition2 && !pointByInt.ContainsKey(triOwner3))
				{
					pointByInt.Add(triOwner3, devTin.Points[ptIndexStartFrom + triOwner3]);
				}
				if (triOwner4 != -1 && !condition3 && !pointByInt.ContainsKey(triOwner4))
				{
					pointByInt.Add(triOwner4, devTin.Points[ptIndexStartFrom + triOwner4]);
				}
			}
			if (pointByInt.Count == 0)
			{
				continue;
			}
			if (pointByInt.Count == 0)
			{
				throw new Exception("Disconnected group of triangles");
			}
			foreach (int floodfill3 in floodfill)
			{
				Point centroid = devTin.Triangles[floodfill3].Centroid();
				cellTris[triOwner[floodfill3] = pointByInt.OrderBy((KeyValuePair<int, Point> kvp) => kvp.Value.DistanceTo(centroid)).First().Key].Add(floodfill3);
			}
			foreach (int floodfill4 in floodfill)
			{
				int triOwner5 = triOwner[floodfill4];
				int cellTrisTriOwner5Term = cellTris[triOwner5].First();
				if (Floodfill(devTin, floodfill4, triOwner, triangleEdgesBlocked, cellTrisTriOwner5Term).Last() != cellTrisTriOwner5Term)
				{
					triOwner[floodfill4] = -1;
					cellTris[triOwner5].Remove(floodfill4);
				}
			}
		}
	}

	private List<int> GetNeutralTriangles(Tin devTin, int[] triOwner)
	{
		List<int> neutralTriangles = new List<int>();
		for (int i = 0; i <= triOwner.Count() - 1; i++)
		{
			if (triOwner[i] == -1 && !devTin.TriangleDefinition[i].IsDeleted)
			{
				neutralTriangles.Add(i);
			}
		}
		return neutralTriangles;
	}

	private void TrianglePass(Tin devTin, int cellCt, int ptIndexStartFrom, Polyline breakline, ref List<int>[] cellTris, ref int[] triOwner, ref Dictionary<int, byte> triangleEdgesBlocked, out List<Polyline> blockedSegs)
	{
		triOwner = new int[devTin.TriangleCount];
		cellTris = new List<int>[cellCt];
		for (int i = 0; i <= cellCt - 1; i++)
		{
			cellTris[i] = new List<int>(8);
		}
		blockedSegs = new List<Polyline>();
		triangleEdgesBlocked = new Dictionary<int, byte>();
		SpatialIndex<int> spatialIndex = SpatialIndex.OLD_ComputeSegmentSpatialIndex(breakline);
		for (int j = 0; j < devTin.TriangleCount; j++)
		{
			TriangleDefinition triangleDefinition = devTin.TriangleDefinition[j];
			int p = triangleDefinition.p0;
			int p2 = triangleDefinition.p1;
			int p3 = triangleDefinition.p2;
			Point point = devTin.Points[p];
			Point point2 = devTin.Points[p2];
			Point point3 = devTin.Points[p3];
			MathHelpers.MaxMin3(point.X, point2.X, point3.X, out var max, out var min);
			MathHelpers.MaxMin3(point.Y, point2.Y, point3.Y, out var max2, out var min2);
			List<int> elements = spatialIndex.GetElements(min - _filterToleranceTIN, max + _filterToleranceTIN, min2 - _filterToleranceTIN, max2 + _filterToleranceTIN);
			byte b = 0;
			foreach (int element in elements)
			{
				Segment segment = breakline.Segments[element];
				bool closeTo = segment.CloseTo(point, _filterToleranceTIN);
				bool closeTo2 = segment.CloseTo(point2, _filterToleranceTIN);
				bool closeTo3 = segment.CloseTo(point3, _filterToleranceTIN);
				bool condition = false;
				bool condition2 = false;
				bool condition3 = false;
				if (closeTo & closeTo2)
				{
					condition = true;
				}
				if (closeTo2 & closeTo3)
				{
					condition2 = true;
				}
				if (closeTo3 & closeTo)
				{
					condition3 = true;
				}
				if (condition)
				{
					b |= 1;
					blockedSegs.Add(new Polyline(new Point[2] { point, point2 }));
				}
				if (condition2)
				{
					b |= 2;
					blockedSegs.Add(new Polyline(new Point[2] { point2, point3 }));
				}
				if (condition3)
				{
					b |= 4;
					blockedSegs.Add(new Polyline(new Point[2] { point, point3 }));
				}
			}
			if (b != 0)
			{
				triangleEdgesBlocked.Add(j, b);
			}
			int triOwner2 = p - ptIndexStartFrom;
			if (triOwner2 >= 0 && triOwner2 < cellCt)
			{
				cellTris[triOwner2].Add(j);
				triOwner[j] = triOwner2;
				continue;
			}
			int triOwner3 = p2 - ptIndexStartFrom;
			if (triOwner3 >= 0 && triOwner3 < cellCt)
			{
				cellTris[triOwner3].Add(j);
				triOwner[j] = triOwner3;
				continue;
			}
			int triOwner4 = p3 - ptIndexStartFrom;
			if (triOwner4 >= 0 && triOwner4 < cellCt)
			{
				cellTris[triOwner4].Add(j);
				triOwner[j] = triOwner4;
			}
			else
			{
				triOwner[j] = -1;
			}
		}
	}

	private void GetNaiiveFaceSegments(Tin baseTin, out List<Point> faceSegs, out List<int> facePlineStarts)
	{
		List<Point> points = new List<Point>(baseTin.Triangles.Count);
		for (int i = 0; i < baseTin.Triangles.Count; i++)
		{
			points.Add(baseTin.Triangles[i].CircumCircleCenter());
		}
		BitArray bitArray = new BitArray(baseTin.Triangles.Count);
		faceSegs = new List<Point>((int)((double)baseTin.Points.Count / 2.0));
		facePlineStarts = new List<int>();
		int ordinal = 0;
		int ordinal2 = 7;
		for (int j = 0; j < baseTin.Triangles.Count; j++)
		{
			TriangleDefinition triangleDefinition = baseTin.TriangleDefinition[j];
			int at = triangleDefinition.at0;
			int at2 = triangleDefinition.at1;
			int at3 = triangleDefinition.at2;
			int p = triangleDefinition.p0;
			int p2 = triangleDefinition.p1;
			int p3 = triangleDefinition.p2;
			Point point = points[j];
			if (at != -1 && !bitArray[at] && p > ordinal2 && p2 > ordinal2)
			{
				faceSegs.Add(point);
				faceSegs.Add(points[at]);
				facePlineStarts.Add(ordinal);
				ordinal += 2;
			}
			if (at2 != -1 && !bitArray[at2] && p3 > ordinal2 && p2 > ordinal2)
			{
				faceSegs.Add(point);
				faceSegs.Add(points[at2]);
				facePlineStarts.Add(ordinal);
				ordinal += 2;
			}
			if (at3 != -1 && !bitArray[at3] && p > ordinal2 && p3 > ordinal2)
			{
				faceSegs.Add(point);
				faceSegs.Add(points[at3]);
				facePlineStarts.Add(ordinal);
				ordinal += 2;
			}
			bitArray[j] = true;
		}
	}

	private Polygon MergeTrianglesToPolygon(Tin devTin, List<int> tris)
	{
		HashSet<int> trisSet = new HashSet<int>(tris);
		List<Tuple<int, int>> valueTupleList = new List<Tuple<int, int>>();
		foreach (int tri in trisSet)
		{
			TriangleDefinition triangleDefinition = devTin.TriangleDefinition[tri];
			int at = triangleDefinition.at0;
			int at2 = triangleDefinition.at1;
			int at3 = triangleDefinition.at2;
			int p = triangleDefinition.p0;
			int p2 = triangleDefinition.p1;
			int p3 = triangleDefinition.p2;
			if (!trisSet.Contains(at))
			{
				valueTupleList.Add(new Tuple<int, int>(p, p2));
			}
			if (!trisSet.Contains(at2))
			{
				valueTupleList.Add(new Tuple<int, int>(p2, p3));
			}
			if (!trisSet.Contains(at3))
			{
				valueTupleList.Add(new Tuple<int, int>(p3, p));
			}
		}
		List<Point> points = new List<Point>(8);
		Tuple<int, int> tuple = valueTupleList.Last();
		int curPt = tuple.Item1;
		int tupleTerm = tuple.Item2;
		points.Add(devTin.Points[curPt]);
		valueTupleList.RemoveAt(valueTupleList.Count - 1);
		while (true)
		{
			if (valueTupleList.Count == 0)
			{
				throw new NotImplementedException();
			}
			Tuple<int, int> tuple2 = valueTupleList.FirstOrDefault((Tuple<int, int> kvp) => kvp.Item1 == curPt || kvp.Item2 == curPt);
			if (tuple2 == null)
			{
				throw new NotImplementedException();
			}
			valueTupleList.Remove(tuple2);
			if (tuple2.Item1 == curPt)
			{
				curPt = tuple2.Item2;
				points.Add(devTin.Points[curPt]);
				if (curPt == tupleTerm)
				{
					break;
				}
			}
			else
			{
				curPt = tuple2.Item1;
				points.Add(devTin.Points[curPt]);
				if (curPt == tupleTerm)
				{
					break;
				}
			}
		}
		Polygon polygon = new Polygon(points);
		if (polygon.Area > 0.0)
		{
			polygon.Reverse();
		}
		return polygon;
	}

	private static int PointToCellIndex(int pIdx)
	{
		return pIdx - 8;
	}

	private static int CellToPointIndex(int cIdx)
	{
		return cIdx + 8;
	}
}

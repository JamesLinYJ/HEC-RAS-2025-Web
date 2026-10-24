using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Vectors.MeshTopology;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors;

public class DynamicTINFactory<Tpred> : DelaunayTinFactory where Tpred : struct, IPredicates
{
	private struct Pair
	{
		public readonly int A;

		public readonly int B;

		public Pair(int intValue, int intValue2)
		{
			if (intValue < intValue2)
			{
				A = intValue;
				B = intValue2;
			}
			else
			{
				A = intValue2;
				B = intValue;
			}
		}

		public override bool Equals(object obj)
		{
			if (obj is Pair pair)
			{
				return pair == this;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(A, B);
		}

		public static bool operator ==(Pair edge1, Pair edge2)
		{
			if (edge1.A == edge2.A)
			{
				return edge1.B == edge2.B;
			}
			return false;
		}

		public static bool operator !=(Pair eA, Pair eB)
		{
			return !(eA == eB);
		}
	}

	private struct ActiveTriangle
	{
		public int p0;

		public int p1;

		public int p2;

		public int idx;
	}

	public static class MessageCodes
	{
		public static readonly MessageClass UnderResolvedChannelLocation = new MessageClass("DynamicTin_UnderResolvedChannel", MessageSeverity.Error, "Possible under-resolved channel in Region[{RegionIndex}], near ({Coordinate}).");

		public static readonly MessageClass UnderResolvedChannel = new MessageClass("DynamicTin_UnderResolvedChannel", MessageSeverity.Error, "Possible under-resolved channel in Region[{RegionIndex}].");

		public static Message GetUnderResolvedChannel(int rgn, Point pt)
		{
			return UnderResolvedChannelLocation.Create(rgn, pt);
		}

		public static Message GetUnderResolvedChannel(int rgn)
		{
			return UnderResolvedChannel.Create(rgn);
		}
	}

	public List<int> dynDeletedTriangles;

	public List<int> dynCreatedTriangles;

	public bool errorOnInvalidBoundaries = true;

	public DynamicTINFactory(TriangulationParameters tparams = null)
		: base(tparams)
	{
		TriangulationParameters.NormalizedOffset = new Vector(0.0, 0.0);
		_retriangulateConstrainedFace = true;
		dynDeletedTriangles = new List<int>();
		dynCreatedTriangles = new List<int>();
	}

	public DynamicTINFactory<Tpred> Clone()
	{
		DynamicTINFactory<Tpred> dynamicTINFactory = new DynamicTINFactory<Tpred>(TriangulationParameters);
		dynamicTINFactory._polygons.AddRange(_polygons);
		dynamicTINFactory._delaunayBreaklines.AddRange(_delaunayBreaklines);
		dynamicTINFactory._delaunayPoints.AddRange(_delaunayPoints);
		dynamicTINFactory._perimConstraints = new Dictionary<(int, int), int>(_perimConstraints);
		dynamicTINFactory._blineConstraints = new Dictionary<(int, int), int>(_blineConstraints);
		dynamicTINFactory._tin = _tin.Clone();
		return dynamicTINFactory;
	}

	public bool TriFaceIsConstrained(int t, int at012)
	{
		Tin.TriangleDefinition[t].EdgePoints(at012, out var ptA, out var ptB);
		return IsConstrained(ptA, ptB);
	}

	public IEnumerable<(int, int)> ConstrainedEdges()
	{
		foreach (var key in _perimConstraints.Keys)
		{
			yield return key;
		}
		foreach (var key2 in _blineConstraints.Keys)
		{
			yield return key2;
		}
	}

	public double ConstrainedRadius(int t)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		Circle circle = _tin.Triangles[t].CircumCircle();
		Point center = circle.Center;
		double radius = circle.Radius;
		for (int i = 0; i < 3; i++)
		{
			triangleDefinition.EdgePoints(i, out var ptA, out var ptB);
			if (IsConstrained(ptA, ptB) && 0.0 > Tpred.CounterClockwise(_tin.Points[ptA], _tin.Points[ptB], center))
			{
				Point point = _tin.Points[triangleDefinition.Point((i + 2) % 3)];
				Point other = 0.5 * (_tin.Points[ptA] + _tin.Points[ptB]);
				radius = Math.Min(radius, point.DistanceTo(other));
			}
		}
		return radius;
	}

	public void CopyToDynMesh(DynamicMesh mesh, bool copyDeleted = false, ProgressReporter reporter = null)
	{
		for (int i = 0; i < _tin.Count; i++)
		{
			if (reporter != null && reporter.CancelRequested)
			{
				return;
			}
			int nIdx = mesh.AddNode(_tin[i]);
			if (!copyDeleted && IsSuperPoint(i))
			{
				mesh.DeleteNode(nIdx);
			}
		}
		for (int j = 0; j < _tin.TriangleDefinition.Count; j++)
		{
			if (reporter != null && reporter.CancelRequested)
			{
				return;
			}
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[j];
			if (copyDeleted || !triangleDefinition.IsDeleted)
			{
				int cIdx = mesh.AddCell();
				mesh.SetCellNodes(cIdx, new List<int> { triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2 }, ElementMode.CREATE_OR_EXISTING);
			}
		}
		if (!copyDeleted)
		{
			mesh.DeleteNakedNodes();
		}
	}

	public bool DynInsertPoint(Point pt, ref int t, bool translatePoint, out int p, int newPointIdx = -1)
	{
		dynDeletedTriangles.Clear();
		dynCreatedTriangles.Clear();
		if (translatePoint)
		{
			pt = pt.Translate(_normalizedOffset);
		}
		_tin.FindTriangleCheckNearbyCorners(pt, _nearPointTolerance, ref t, out p);
		if (p >= 0)
		{
			return false;
		}
		p = DelaunayInsert(t, pt, newPointIdx);
		return true;
	}

	private int DelaunayInsert(int triIdx, Point pt, int newPointIdx = -1)
	{
		if (newPointIdx < 0)
		{
			_tin.Add(pt);
			newPointIdx = _tin.PointCount - 1;
		}
		else
		{
			_tin[newPointIdx] = pt;
		}
		int count = _tin.Triangles.Count;
		DelaunayRetriangulateInternal(pt, triIdx, newPointIdx);
		dynCreatedTriangles.AddRange(dynDeletedTriangles);
		for (int i = count; i < _tin.Triangles.Count; i++)
		{
			dynCreatedTriangles.Add(i);
		}
		return newPointIdx;
	}

	private void DelaunayRetriangulateInternal(Point pt, int triIdx, int insertedPointIdx)
	{
		Stack<int> intStack = new Stack<int>();
		intStack.Push(triIdx);
		while (intStack.Count != 0)
		{
			triIdx = intStack.Pop();
			_deletedTrianglesStack.Push(triIdx);
			dynDeletedTriangles.Add(triIdx);
			MarkDeleted(triIdx);
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[triIdx];
			for (int i = 0; i < 3; i++)
			{
				int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(i);
				triangleDefinition.EdgePoints(i, out var ptA, out var ptB);
				if (!IsConstrained(ptA, ptB) && !dynDeletedTriangles.Contains(adjacentTriangleIdx) && DynIsTriangleInsideInsertionHole(adjacentTriangleIdx, pt))
				{
					intStack.Push(adjacentTriangleIdx);
				}
			}
		}
		MakeInsertionHoleFromDeletedTrianglesAndFill(insertedPointIdx);
	}

	protected bool DynIsTriangleInsideInsertionHole(int at, Point point)
	{
		if (at == -1)
		{
			return false;
		}
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[at];
		return Tpred.InCircle(_tin.Points[triangleDefinition.p0], _tin.Points[triangleDefinition.p1], _tin.Points[triangleDefinition.p2], point) >= 0.0;
	}

	private List<InsertionHoleEdge> MakeInsertionHoleFromDeletedTrianglesAndFill(int p)
	{
		List<InsertionHoleEdge> insertionHoleEdges = new List<InsertionHoleEdge>();
		foreach (int deletedTrianglesStack in _deletedTrianglesStack)
		{
			TriangleDefinition triangleDefinition = _tin.TriangleDefinition[deletedTrianglesStack];
			if (triangleDefinition.at0 == -1 || !_deletedTrianglesStack.Contains(triangleDefinition.at0))
			{
				insertionHoleEdges.Add(new InsertionHoleEdge(triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.at0, -1));
			}
			if (triangleDefinition.at1 == -1 || !_deletedTrianglesStack.Contains(triangleDefinition.at1))
			{
				insertionHoleEdges.Add(new InsertionHoleEdge(triangleDefinition.p1, triangleDefinition.p2, triangleDefinition.at1, -1));
			}
			if (triangleDefinition.at2 == -1 || !_deletedTrianglesStack.Contains(triangleDefinition.at2))
			{
				insertionHoleEdges.Add(new InsertionHoleEdge(triangleDefinition.p2, triangleDefinition.p0, triangleDefinition.at2, -1));
			}
		}
		Dictionary<int, InsertionHolePointTriangles> insertionHolePointTrianglesByInt = new Dictionary<int, InsertionHolePointTriangles>();
		for (int i = 0; i < insertionHoleEdges.Count; i++)
		{
			InsertionHoleEdge insertionHoleEdge = insertionHoleEdges[i];
			int insideTriangleInsertionHoleEdgeTerm = (insertionHoleEdge.InsideTriangle = AddTriangle(insertionHoleEdge.p0, insertionHoleEdge.p1, p, insertionHoleEdge.OutsideTriangle, -1, -1));
			insertionHoleEdges[i] = insertionHoleEdge;
			bool tryGetValue = insertionHolePointTrianglesByInt.TryGetValue(insertionHoleEdge.p0, out var localValue);
			bool tryGetValue2 = insertionHolePointTrianglesByInt.TryGetValue(insertionHoleEdge.p1, out var localValue2);
			localValue.RightTriangle = insideTriangleInsertionHoleEdgeTerm;
			localValue2.LeftTriangle = insideTriangleInsertionHoleEdgeTerm;
			if (tryGetValue)
			{
				insertionHolePointTrianglesByInt[insertionHoleEdge.p0] = localValue;
			}
			else
			{
				insertionHolePointTrianglesByInt.Add(insertionHoleEdge.p0, localValue);
			}
			if (tryGetValue2)
			{
				insertionHolePointTrianglesByInt[insertionHoleEdge.p1] = localValue2;
			}
			else
			{
				insertionHolePointTrianglesByInt.Add(insertionHoleEdge.p1, localValue2);
			}
			if (insertionHoleEdge.OutsideTriangle >= 0)
			{
				TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[insertionHoleEdge.OutsideTriangle];
				if (triangleDefinition2.p0 == insertionHoleEdge.p1)
				{
					triangleDefinition2.at0 = insideTriangleInsertionHoleEdgeTerm;
				}
				else if (triangleDefinition2.p1 == insertionHoleEdge.p1)
				{
					triangleDefinition2.at1 = insideTriangleInsertionHoleEdgeTerm;
				}
				else if (triangleDefinition2.p2 == insertionHoleEdge.p1)
				{
					triangleDefinition2.at2 = insideTriangleInsertionHoleEdgeTerm;
				}
				_tin.TriangleDefinition[insertionHoleEdge.OutsideTriangle] = triangleDefinition2;
			}
		}
		foreach (InsertionHoleEdge insertionHoleEdge2 in insertionHoleEdges)
		{
			TriangleDefinition triangleDefinition3 = _tin.TriangleDefinition[insertionHoleEdge2.InsideTriangle];
			triangleDefinition3.at1 = insertionHolePointTrianglesByInt[insertionHoleEdge2.p1].RightTriangle;
			triangleDefinition3.at2 = insertionHolePointTrianglesByInt[insertionHoleEdge2.p0].LeftTriangle;
			_tin.TriangleDefinition[insertionHoleEdge2.InsideTriangle] = triangleDefinition3;
		}
		return insertionHoleEdges;
	}

	protected override void EnforceEdgeSegmentRetriangulateInternal(int pA, int pB, int t, bool isPerimeter, int enforceIdx, TinEditLog log = null)
	{
		EnforceEdgeSegmentRetriangulateInternal<Tpred>(pA, pB, t, isPerimeter, enforceIdx, log);
	}

	public void ConstrainEdge(int pA, int pB, bool isPerimeter, int enforcedFromIdx, TinEditLog log = null)
	{
		EnforceEdgeSegmentRetriangulate(pA, pB, isPerimeter, enforcedFromIdx, log);
	}

	public void ConstrainEdge(int pA, int pB, bool isPerimeter, int enforcedFromIdx, ref int t, TinEditLog log = null)
	{
		EnforceEdgeSegmentRetriangulate(pA, pB, isPerimeter, enforcedFromIdx, ref t, log);
	}

	public void UnconstrainEdge(int pA, int pB, ref int t, TinEditLog log = null)
	{
		_ = _tin.TriangleDefinition;
		_tin.FindTriangleAndNearestPointIndex(_tin.Points[pA], ref t, out var _);
		(int, int) key = ((pA > pB) ? (pB, pA) : (pA, pB));
		if (_perimConstraints.ContainsKey(key))
		{
			_perimConstraints.Remove(key);
		}
		else
		{
			if (!_blineConstraints.ContainsKey(key))
			{
				throw new DynamicMeshException("Tried to relax nonexistent constraint");
			}
			_blineConstraints.Remove(key);
		}
		PropagatingFlip(new HashSet<Pair>
		{
			new Pair(pA, pB)
		}, ref t, log);
	}

	public bool FlipNeeded(EdgeDefinition edge)
	{
		if (edge.APointIndex < 8 && edge.BPointIndex < 8)
		{
			return false;
		}
		if (IsConstrained(edge.APointIndex, edge.BPointIndex))
		{
			return false;
		}
		return FlipNeededInternal(edge);
	}

	public bool FlipNeededInternal(EdgeDefinition edge)
	{
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		return FlipNeededInternal(edge.APointIndex, edge.BPointIndex, triangleDefinition[edge.ATriangleIndex].Point((edge.AEdge012 + 2) % 3), triangleDefinition[edge.BTriangleIndex].Point((edge.BEdge012 + 2) % 3));
	}

	public bool FlipNeededInternal(int A, int B, int C, int D)
	{
		return Tpred.InCircle(Tin.Points[A], Tin.Points[B], Tin.Points[C], Tin.Points[D]) > 0.0;
	}

	public bool FlipNeededInternal(Point pntA, int B, int C, int D)
	{
		return Tpred.InCircle(pntA, Tin.Points[B], Tin.Points[C], Tin.Points[D]) > 0.0;
	}

	public bool FlipNeededInternal(int A, int B, Point pntC, int D)
	{
		return Tpred.InCircle(Tin.Points[A], Tin.Points[B], pntC, Tin.Points[D]) > 0.0;
	}

	public void DynDeletePoint(int pointIdx, ref int t, TinEditLog log = null, bool clearData = true)
	{
		if (pointIdx < 8)
		{
			throw new Exception("Cannot delete super points of triangulation");
		}
		Point point = _tin.Points[pointIdx];
		_tin.FindTriangleAndNearestPointIndex(point, ref t, out var _);
		List<int> trianglesAroundPointCCW = _tin.TrianglesAroundPointCCW(pointIdx, t);
		List<TriangleDefinition> triangleDefinition = _tin.TriangleDefinition;
		double distanceSquaredToPointTerm = 0.0;
		foreach (int trianglesAroundPointCCW2 in trianglesAroundPointCCW)
		{
			int clockwisePointWithinTriangle = triangleDefinition[trianglesAroundPointCCW2].GetClockwisePointWithinTriangle(pointIdx);
			if (IsConstrained(pointIdx, clockwisePointWithinTriangle))
			{
				throw new Exception("Cannot delete constrained point");
			}
			distanceSquaredToPointTerm = Math.Max(distanceSquaredToPointTerm, point.DistanceSquaredTo(_tin.Points[clockwisePointWithinTriangle]));
		}
		distanceSquaredToPointTerm = Math.Sqrt(distanceSquaredToPointTerm);
		int intervalIndex = 0;
		int countPolynomial = trianglesAroundPointCCW.Count * trianglesAroundPointCCW.Count;
		while (countPolynomial > 0 && trianglesAroundPointCCW.Count > 3)
		{
			if (intervalIndex >= trianglesAroundPointCCW.Count)
			{
				intervalIndex = 0;
			}
			int trianglesAroundPointCCW3 = trianglesAroundPointCCW[intervalIndex];
			int trianglesAroundPointCCW4 = trianglesAroundPointCCW[(intervalIndex + 1) % trianglesAroundPointCCW.Count];
			TriangleDefinition triangleDefinition2 = triangleDefinition[trianglesAroundPointCCW3];
			TriangleDefinition triangleDefinition3 = triangleDefinition[trianglesAroundPointCCW4];
			int point012ByGlobalIdx = triangleDefinition2.Point012ByGlobalIdx(pointIdx);
			int index = triangleDefinition2.Point((point012ByGlobalIdx + 1) % 3);
			int index2 = triangleDefinition2.Point((point012ByGlobalIdx + 2) % 3);
			int point012ByGlobalIdx2 = triangleDefinition3.Point012ByGlobalIdx(pointIdx);
			int index3 = triangleDefinition3.Point((point012ByGlobalIdx2 + 2) % 3);
			double counterClockwise = Tpred.CounterClockwise(point, _tin.Points[index], _tin.Points[index3]);
			double counterClockwise2 = Tpred.CounterClockwise(_tin.Points[index], _tin.Points[index2], _tin.Points[index3]);
			bool condition = true;
			if ((trianglesAroundPointCCW.Count == 4 || counterClockwise > 0.0) && counterClockwise2 > 0.0)
			{
				Circle circle = Triangle.ComputeCircumCircle(_tin.Points[index], _tin.Points[index2], _tin.Points[index3]);
				double radiusCircleTerm = 1E-08 * Math.Min(circle.Radius, 2.0 * distanceSquaredToPointTerm);
				for (int i = 2; i < trianglesAroundPointCCW.Count - 1; i++)
				{
					int index4 = trianglesAroundPointCCW[(intervalIndex + i) % trianglesAroundPointCCW.Count];
					Point other = _tin.Points[triangleDefinition[index4].GetClockwisePointWithinTriangle(pointIdx)];
					if (circle.Center.DistanceTo(other) < circle.Radius - radiusCircleTerm)
					{
						condition = false;
						break;
					}
				}
			}
			else
			{
				condition = false;
			}
			if (condition)
			{
				Flip(trianglesAroundPointCCW3, trianglesAroundPointCCW4, pointIdx, log);
				trianglesAroundPointCCW.RemoveAt((intervalIndex + 1) % trianglesAroundPointCCW.Count);
			}
			else
			{
				intervalIndex++;
			}
			countPolynomial--;
		}
		if (trianglesAroundPointCCW.Count > 3)
		{
			throw new DynamicMeshException("Collinearity trouble while deleting TIN node");
		}
		if (clearData)
		{
			_tin.Points[pointIdx] = new Point(double.NaN, double.NaN);
		}
		TriangleDefinition triangleDefinition4 = new TriangleDefinition(triangleDefinition[trianglesAroundPointCCW[0]].GetCounterClockwisePointWithinTriangle(pointIdx), triangleDefinition[trianglesAroundPointCCW[1]].GetCounterClockwisePointWithinTriangle(pointIdx), triangleDefinition[trianglesAroundPointCCW[2]].GetCounterClockwisePointWithinTriangle(pointIdx), triangleDefinition[trianglesAroundPointCCW[0]].AdjacentTriangleOppositePoint_GlobalIndex(pointIdx), triangleDefinition[trianglesAroundPointCCW[1]].AdjacentTriangleOppositePoint_GlobalIndex(pointIdx), triangleDefinition[trianglesAroundPointCCW[2]].AdjacentTriangleOppositePoint_GlobalIndex(pointIdx), triangleDefinition[trianglesAroundPointCCW[0]].IsDeleted);
		if (log != null)
		{
			log.LogDeleteTriangle(triangleDefinition[trianglesAroundPointCCW[0]]);
			log.LogDeleteTriangle(triangleDefinition[trianglesAroundPointCCW[1]]);
			log.LogDeleteTriangle(triangleDefinition[trianglesAroundPointCCW[2]]);
			log.LogDeleteEdge(pointIdx, triangleDefinition4.p0);
			log.LogDeleteEdge(pointIdx, triangleDefinition4.p1);
			log.LogDeleteEdge(pointIdx, triangleDefinition4.p2);
			log.LogAddTriangle(triangleDefinition4);
		}
		triangleDefinition[trianglesAroundPointCCW[0]] = triangleDefinition4;
		FixAdjacents(trianglesAroundPointCCW[0]);
		t = trianglesAroundPointCCW[0];
		int trianglesAroundPointCCW5 = trianglesAroundPointCCW[1];
		int trianglesAroundPointCCW6 = trianglesAroundPointCCW[2];
		if (trianglesAroundPointCCW5 > trianglesAroundPointCCW6)
		{
			trianglesAroundPointCCW5 = trianglesAroundPointCCW[2];
			trianglesAroundPointCCW6 = trianglesAroundPointCCW[1];
		}
		if (trianglesAroundPointCCW6 + 1 == triangleDefinition.Count)
		{
			triangleDefinition.RemoveAt(trianglesAroundPointCCW6);
			trianglesAroundPointCCW6 = -1;
			if (trianglesAroundPointCCW5 + 1 == triangleDefinition.Count)
			{
				triangleDefinition.RemoveAt(trianglesAroundPointCCW5);
				trianglesAroundPointCCW5 = -1;
			}
		}
		if (trianglesAroundPointCCW6 >= 0)
		{
			MoveTriangle(triangleDefinition.Count - 1, trianglesAroundPointCCW6);
			triangleDefinition.RemoveAt(triangleDefinition.Count - 1);
		}
		if (trianglesAroundPointCCW5 >= 0)
		{
			MoveTriangle(triangleDefinition.Count - 1, trianglesAroundPointCCW5);
			triangleDefinition.RemoveAt(triangleDefinition.Count - 1);
		}
	}

	public void RepairIsDeleted(TinEditLog tinEditLog, ref int t)
	{
		foreach (KeyValuePair<Tuple<int, int, int>, bool> deletedTriangle in tinEditLog.DeletedTriangles)
		{
			for (int i = 0; i < 3; i++)
			{
				int unity = -1;
				int unity2 = -1;
				switch (i)
				{
				case 0:
					unity = deletedTriangle.Key.Item1;
					unity2 = deletedTriangle.Key.Item2;
					break;
				case 1:
					unity = deletedTriangle.Key.Item2;
					unity2 = deletedTriangle.Key.Item3;
					break;
				case 2:
					unity = deletedTriangle.Key.Item3;
					unity2 = deletedTriangle.Key.Item1;
					break;
				}
				if (tinEditLog.EdgeWasDeleted(unity, unity2))
				{
					continue;
				}
				EdgeDefinition edgeDefinition = Tin.FindEdge(unity, unity2, ref t);
				if (edgeDefinition.IsNull())
				{
					continue;
				}
				if (edgeDefinition.APointIndex == unity && edgeDefinition.BPointIndex == unity2)
				{
					FloodFillIsDeleted(edgeDefinition.ATriangleIndex, deletedTriangle.Value);
					continue;
				}
				if (edgeDefinition.BPointIndex == unity && edgeDefinition.APointIndex == unity2)
				{
					FloodFillIsDeleted(edgeDefinition.BTriangleIndex, deletedTriangle.Value);
					continue;
				}
				throw new Exception("Error during dynamic updates to TIN");
			}
		}
	}

	public void RepairIsDeleted(EdgeDefinition origEdge, bool origADeleted, bool origBDeleted, int pA, int pB, ref int triStart)
	{
		EdgeDefinition newEdge = Tin.FindEdge(pA, pB, ref triStart);
		bool newADeleted;
		bool newBDeleted;
		if (origEdge.APointIndex == newEdge.APointIndex || origEdge.BPointIndex == newEdge.BPointIndex)
		{
			newADeleted = origADeleted;
			newBDeleted = origBDeleted;
		}
		else
		{
			if (origEdge.APointIndex != newEdge.BPointIndex && origEdge.BPointIndex != newEdge.APointIndex)
			{
				throw new DynamicMeshException("Edge IsDeleted corrupted");
			}
			newADeleted = origBDeleted;
			newBDeleted = origADeleted;
		}
		RepairIsDeleted(newADeleted, newBDeleted, newEdge, ref triStart);
	}

	public void RepairIsDeleted(int pA, int pB, bool ADeleted, bool BDeleted, ref int triStart)
	{
		EdgeDefinition newEdge = Tin.FindEdge(pA, pB, ref triStart);
		if (newEdge.APointIndex == pA && newEdge.BPointIndex == pB)
		{
			RepairIsDeleted(ADeleted, BDeleted, newEdge, ref triStart);
			return;
		}
		if (newEdge.BPointIndex == pA && newEdge.APointIndex == pB)
		{
			RepairIsDeleted(BDeleted, ADeleted, newEdge, ref triStart);
			return;
		}
		throw new Exception("Internal TIN error");
	}

	public void RepairIsDeleted(bool newADeleted, bool newBDeleted, EdgeDefinition newEdge, ref int triStart)
	{
		FloodFillIsDeleted(newEdge.ATriangleIndex, newADeleted);
		FloodFillIsDeleted(newEdge.BTriangleIndex, newBDeleted);
	}

	private void FloodFillIsDeleted(int tri0, bool IsDeleted)
	{
		List<int> intList = new List<int> { tri0 };
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		int scaledTriangleCount = Tin.TriangleCount * 3;
		while (scaledTriangleCount > 0 && intList.Count > 0)
		{
			int ordinal = intList.Last();
			intList.RemoveAt(intList.Count - 1);
			TriangleDefinition triangleDefinition2 = triangleDefinition[ordinal];
			if (triangleDefinition2.IsDeleted != IsDeleted)
			{
				triangleDefinition2.IsDeleted = IsDeleted;
				triangleDefinition[ordinal] = triangleDefinition2;
				for (int i = 0; i < 3; i++)
				{
					int adjacentTriangleIdx = triangleDefinition2.AdjacentTriangleIdx(i);
					if (adjacentTriangleIdx >= 0 && triangleDefinition[adjacentTriangleIdx].IsDeleted != IsDeleted && !TriFaceIsConstrained(ordinal, i))
					{
						intList.Add(adjacentTriangleIdx);
					}
				}
			}
			scaledTriangleCount--;
		}
	}

	public bool DynMovePoint(int pointIdx, Point newLocation, ref int t, bool checkInside, TinEditLog log = null)
	{
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		if (pointIdx < 8)
		{
			throw new Exception("Cannot delete super points of triangulation");
		}
		Point point = _tin.Points[pointIdx];
		_tin.FindTriangleAndNearestPointIndex(point, ref t, out var _);
		List<(int, bool, bool)> valueTuples = new List<(int, bool, bool)>();
		HashSet<Pair> pairs = new HashSet<Pair>(14);
		int index3 = t;
		int counterClockwisePointWithinTriangle3 = _tin.TriangleDefinition[index3].GetCounterClockwisePointWithinTriangle(pointIdx);
		int index = _tin.TriangleDefinition[index3].AdjacentTriangleIdx(pointIdx, counterClockwisePointWithinTriangle3);
		int counterClockwisePointWithinTriangle = _tin.TriangleDefinition[index].GetCounterClockwisePointWithinTriangle(pointIdx);
		bool condition = false;
		bool condition2 = true;
		do
		{
			TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[index3];
			int clockwisePointWithinTriangle = triangleDefinition2.GetClockwisePointWithinTriangle(pointIdx);
			bool isConstrained = IsConstrained(pointIdx, counterClockwisePointWithinTriangle3);
			if (isConstrained)
			{
				(int, bool, bool) counterClockwisePointWithinTriangle3Term = (counterClockwisePointWithinTriangle3, triangleDefinition2.IsDeleted, _tin.TriangleDefinition[index].IsDeleted);
				valueTuples.Add(counterClockwisePointWithinTriangle3Term);
				if (counterClockwisePointWithinTriangle3Term.Item2 != counterClockwisePointWithinTriangle3Term.Item3)
				{
					condition = true;
				}
			}
			if (condition2)
			{
				if (Tpred.CounterClockwise(newLocation, _tin[counterClockwisePointWithinTriangle3], _tin[clockwisePointWithinTriangle]) <= 0.0)
				{
					condition2 = false;
				}
				else
				{
					if (!isConstrained && FlipNeededInternal(newLocation, counterClockwisePointWithinTriangle3, clockwisePointWithinTriangle, counterClockwisePointWithinTriangle))
					{
						pairs.Add(new Pair(pointIdx, counterClockwisePointWithinTriangle3));
					}
					if ((counterClockwisePointWithinTriangle3 >= 8 || clockwisePointWithinTriangle >= 8) && !IsConstrained(counterClockwisePointWithinTriangle3, clockwisePointWithinTriangle))
					{
						int index2 = triangleDefinition2.AdjacentTriangleIdx(counterClockwisePointWithinTriangle3, clockwisePointWithinTriangle);
						int counterClockwisePointWithinTriangle2 = _tin.TriangleDefinition[index2].GetCounterClockwisePointWithinTriangle(counterClockwisePointWithinTriangle3);
						if (FlipNeededInternal(counterClockwisePointWithinTriangle3, clockwisePointWithinTriangle, newLocation, counterClockwisePointWithinTriangle2))
						{
							pairs.Add(new Pair(counterClockwisePointWithinTriangle3, clockwisePointWithinTriangle));
						}
					}
				}
			}
			counterClockwisePointWithinTriangle3 = clockwisePointWithinTriangle;
			index = index3;
			index3 = triangleDefinition2.GetCounterClockwiseTriangleAroundPoint(pointIdx);
		}
		while (index3 != t && index3 != -1);
		if (index3 < 0)
		{
			throw new Exception("DynMovePoint cannot be applied to point on convex hull");
		}
		if (checkInside && !condition)
		{
			Tin.FindTriangleContainingPoint(newLocation, ref t);
			if (triangleDefinition[t].IsDeleted)
			{
				return false;
			}
		}
		if (condition2)
		{
			_tin[pointIdx] = newLocation;
			PropagatingFlip(pairs, ref t, log);
		}
		else
		{
			foreach (var localValue in valueTuples)
			{
				UnconstrainEdge(pointIdx, localValue.Item1, ref t, log);
			}
			DynDeletePoint(pointIdx, ref t, log);
			DynInsertPoint(newLocation, ref t, translatePoint: false, out var p2, pointIdx);
			foreach (var localValue2 in valueTuples)
			{
				ConstrainEdge(p2, localValue2.Item1, isPerimeter: false, -1, log);
			}
			for (int i = 0; i < valueTuples.Count; i++)
			{
				RepairIsDeleted(p2, valueTuples[i].Item1, valueTuples[i].Item2, valueTuples[i].Item3, ref t);
			}
		}
		return true;
	}

	private void PropagatingFlip(HashSet<Pair> flipSet, ref int t, TinEditLog log = null)
	{
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		int pointCountPolynomial = _tin.PointCount * _tin.PointCount;
		while (pointCountPolynomial > 0 && flipSet.Count != 0)
		{
			Pair pair = flipSet.First();
			flipSet.Remove(pair);
			EdgeDefinition edge = _tin.FindEdge(pair.A, pair.B, ref t);
			if (FlipNeeded(edge))
			{
				int point = triangleDefinition[edge.ATriangleIndex].Point((edge.AEdge012 + 2) % 3);
				int point2 = triangleDefinition[edge.BTriangleIndex].Point((edge.BEdge012 + 2) % 3);
				Flip(edge, log);
				flipSet.Add(new Pair(pair.B, point));
				flipSet.Add(new Pair(point, pair.A));
				flipSet.Add(new Pair(pair.A, point2));
				flipSet.Add(new Pair(point2, pair.B));
			}
			pointCountPolynomial--;
		}
	}

	private void Flip(int triCW, int triCCW, int pointIdx, TinEditLog log = null)
	{
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		TriangleDefinition triangleDefinition2 = triangleDefinition[triCW];
		TriangleDefinition triangleDefinition3 = triangleDefinition[triCCW];
		int point012ByGlobalIdx = triangleDefinition2.Point012ByGlobalIdx(pointIdx);
		int point = triangleDefinition2.Point((point012ByGlobalIdx + 1) % 3);
		int point2 = triangleDefinition2.Point((point012ByGlobalIdx + 2) % 3);
		int point012ByGlobalIdx2 = triangleDefinition3.Point012ByGlobalIdx(pointIdx);
		int point3 = triangleDefinition3.Point((point012ByGlobalIdx2 + 2) % 3);
		TriangleDefinition triangleDefinition4 = new TriangleDefinition(pointIdx, point, point3, triangleDefinition2.AdjacentTriangleIdx(point012ByGlobalIdx), triCCW, triangleDefinition3.AdjacentTriangleIdx((point012ByGlobalIdx2 + 2) % 3), triangleDefinition2.IsDeleted);
		TriangleDefinition triangleDefinition5 = new TriangleDefinition(point, point2, point3, triangleDefinition2.AdjacentTriangleIdx((point012ByGlobalIdx + 1) % 3), triangleDefinition3.AdjacentTriangleIdx((point012ByGlobalIdx2 + 1) % 3), triCW, triangleDefinition2.IsDeleted);
		if (log != null)
		{
			log.LogDeleteEdge(pointIdx, point2);
			log.LogDeleteTriangle(triangleDefinition[triCW]);
			log.LogDeleteTriangle(triangleDefinition[triCCW]);
			log.LogAddEdge(point, point3);
			log.LogAddTriangle(triangleDefinition4);
			log.LogAddTriangle(triangleDefinition5);
		}
		triangleDefinition[triCW] = triangleDefinition4;
		triangleDefinition[triCCW] = triangleDefinition5;
		FixAdjacents(triCW);
		FixAdjacents(triCCW);
	}

	private void Flip(EdgeDefinition edge, TinEditLog log = null)
	{
		Flip(edge.ATriangleIndex, edge.BTriangleIndex, edge.BPointIndex, log);
	}

	public void FixAdjacents(int t)
	{
		TriangleDefinition triangleDefinition = _tin.TriangleDefinition[t];
		for (int i = 0; i < 3; i++)
		{
			int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(i);
			if (adjacentTriangleIdx >= 0)
			{
				TriangleDefinition triangleDefinition2 = _tin.TriangleDefinition[adjacentTriangleIdx];
				triangleDefinition2.SetAdjacentTriangle(triangleDefinition.Point((i + 1) % 3), triangleDefinition.Point(i), t);
				_tin.TriangleDefinition[adjacentTriangleIdx] = triangleDefinition2;
			}
		}
	}

	public void MoveTriangle(int tOrig, int tNew)
	{
		_tin.TriangleDefinition[tNew] = _tin.TriangleDefinition[tOrig];
		FixAdjacents(tNew);
	}

	public void AddInteriorFill(double bias = 0.7, double target = 10000000000.0, bool setSizeInFill = true)
	{
		AddInteriorFill(PointToSizeParameters, setSizeInFill);
		SizeParams PointToSizeParameters(Point p)
		{
			return new SizeParams(target, bias, -1);
		}
	}

	public void AddInteriorFill(Func<Point, SizeParams> PointToSizeParameters, bool setSizeInFill, List<int> nodeToRegion = null)
	{
		int ordinal = 0;
		int ordinal2 = 0;
		List<TriangleDefinition> triDefs = _tin.TriangleDefinition;
		if (nodeToRegion != null)
		{
			nodeToRegion.Clear();
			nodeToRegion.EnsureCapacity(_tin.PointCount);
			for (int i = 0; i < _tin.PointCount; i++)
			{
				nodeToRegion.Add(-1);
			}
		}
		_tin.TrackM(_tin.Z);
		for (int j = 0; j < _tin.PointCount; j++)
		{
			if (double.IsNaN(_tin.M[j]))
			{
				_tin.M[j] = double.MaxValue;
			}
		}
		PriorityQueue<ActiveTriangle, double> activeTriangleQueue = new PriorityQueue<ActiveTriangle, double>();
		for (int k = 0; k < triDefs.Count; k++)
		{
			if (!triDefs[k].IsDeleted)
			{
				SizeParams sizeParams = PointToSizeParameters(_tin.Triangles[k].Centroid());
				if (!double.IsNaN(sizeParams.stretchRate) && sizeParams.stretchRate != 0.0)
				{
					QueueTriangle(k);
				}
			}
		}
		int pointCount = _tin.PointCount;
		double priority = 0.0;
		for (int l = 0; l < pointCount; l++)
		{
			int triIdx = -1;
			ActiveTriangle element;
			while (activeTriangleQueue.Count > 0 && activeTriangleQueue.TryDequeue(out element, out priority))
			{
				if (triDefs[element.idx].p0 == element.p0 && triDefs[element.idx].p1 == element.p1 && triDefs[element.idx].p2 == element.p2)
				{
					triIdx = element.idx;
					break;
				}
			}
			if (triIdx < 0)
			{
				break;
			}
			SizeParams sizeParams2 = PointToSizeParameters(_tin.Triangles[triIdx].Centroid());
			if (0.0 - priority < 1.0 + sizeParams2.stretchRate)
			{
				break;
			}
			Point point = _tin.Triangles[triIdx].CircumCircleCenter();
			if (!FindConstrainedInsertion(point, ref triIdx))
			{
				continue;
			}
			DynInsertPoint(point, ref triIdx, translatePoint: false, out var p);
			nodeToRegion?.Add(sizeParams2.region);
			if (setSizeInFill)
			{
				_tin.Z[p] = sizeParams2.target;
			}
			List<Tuple<int, bool>> pointsAroundPointCCW = _tin.PointsAroundPointCCW(p, triIdx);
			double maxValue = double.MaxValue;
			foreach (Tuple<int, bool> pointsAroundPointCCW2 in pointsAroundPointCCW)
			{
				if (!pointsAroundPointCCW2.Item2)
				{
					double tinPointsAroundPointCCW2Term = _tin.M[pointsAroundPointCCW2.Item1] + _tin.Points[pointsAroundPointCCW2.Item1].DistanceTo(point) * sizeParams2.stretchRate;
					maxValue = Math.Min(maxValue, tinPointsAroundPointCCW2Term);
				}
			}
			_tin.M[p] = maxValue;
			ordinal++;
			foreach (int dynCreatedTriangle in dynCreatedTriangles)
			{
				QueueTriangle(dynCreatedTriangle);
				ordinal2++;
			}
		}
		void QueueTriangle(int idx2)
		{
			TriangleDefinition triangleDefinition = triDefs[idx2];
			if (!triangleDefinition.IsDeleted)
			{
				double constrainedRadius = ConstrainedRadius(idx2);
				double tinP0Term = Math.Min(Math.Min(_tin.M[triangleDefinition.p0], _tin.M[triangleDefinition.p1]), _tin.M[triangleDefinition.p2]);
				ActiveTriangle element2 = new ActiveTriangle
				{
					p0 = triangleDefinition.p0,
					p1 = triangleDefinition.p1,
					p2 = triangleDefinition.p2,
					idx = idx2
				};
				activeTriangleQueue.Enqueue(element2, (0.0 - constrainedRadius) / tinP0Term);
			}
		}
	}

	public bool FindConstrainedInsertion(Point point, ref int triIdx, bool simpleCheck = false)
	{
		double computed = 0.01;
		if (simpleCheck)
		{
			_tin.Triangles[triIdx].Contains(point, out var weights);
			if (weights.Weight0 > computed && weights.Weight1 > computed)
			{
				return weights.Weight2 > computed;
			}
			return false;
		}
		int i;
		for (i = 0; i < _tin.TriangleCount; i++)
		{
			_tin.Triangles[triIdx].Contains(point, out var weights2);
			int triIdx2 = -1;
			for (int j = 0; j < 3; j++)
			{
				double weights22 = weights2[j];
				int at = (j + 1) % 3;
				if (!(weights22 <= computed))
				{
					continue;
				}
				if (TriFaceIsConstrained(triIdx, at))
				{
					return false;
				}
				if (weights22 < 0.0)
				{
					triIdx2 = _tin.TriangleDefinition[triIdx].AdjacentTriangleIdx(at);
					if (triIdx2 >= 0)
					{
						break;
					}
					return false;
				}
			}
			if (triIdx2 >= 0)
			{
				triIdx = triIdx2;
				continue;
			}
			return true;
		}
		if (i == _tin.TriangleCount)
		{
			throw new DynamicMeshException("Triangulation for size function failed");
		}
		return false;
	}

	public OperationResult TryLabelTrianglesFromConstraints(Dictionary<Segment, Tuple<int, int>> edgeLabels, out List<int> labels)
	{
		Tin tin = Tin;
		int t = 0;
		OperationResult operationResult = new OperationResult();
		labels = Enumerable.Repeat(-1, tin.TriangleCount).ToList();
		FloodFillHelper floodFillHelper = new FloodFillHelper((int ordinal) =>
		{
			if (ordinal == -1)
			{
				return (Memory<int>)null;
			}
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[ordinal];
			return new int[3] { triangleDefinition.at0, triangleDefinition.at1, triangleDefinition.at2 };
		}, (int from, int to) =>
		{
			if (from == -1 || to == -1)
			{
				return false;
			}
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[from];
			return !TriFaceIsConstrained(from, triangleDefinition.GetEdge012(to));
		});
		HashSet<int> intSet = new HashSet<int>();
		List<int> intList = new List<int>(1);
		foreach (KeyValuePair<Segment, Tuple<int, int>> edgeLabel in edgeLabels)
		{
			int[] intValues = new int[2]
			{
				edgeLabel.Value.Item1,
				edgeLabel.Value.Item2
			};
			Segment key = edgeLabel.Key;
			EdgeDefinition edgeDefinition = EdgeDefinition.NullEdgeDefinition();
			int aPointIndexEdgeDefinitionTerm = -1;
			for (int intervalIndex = 0; intervalIndex < 2; intervalIndex++)
			{
				if (intValues[intervalIndex] < 0 || intSet.Contains(intValues[intervalIndex]))
				{
					continue;
				}
				if (aPointIndexEdgeDefinitionTerm < 0)
				{
					Tin.FindTriangleAndNearestPointIndex(key.A, ref t, out var p);
					Tin.FindTriangleAndNearestPointIndex(key.B, ref t, out var p2);
					edgeDefinition = Tin.FindEdge(p, p2, ref t);
					if (edgeDefinition.IsNull())
					{
						operationResult.AddMessage(MessageCodes.GetUnderResolvedChannel(intValues[intervalIndex], key.A));
						continue;
					}
					aPointIndexEdgeDefinitionTerm = ((edgeDefinition.APointIndex != p) ? 1 : 0);
				}
				intList.Clear();
				if (aPointIndexEdgeDefinitionTerm == intervalIndex)
				{
					intList.Add(edgeDefinition.ATriangleIndex);
				}
				else
				{
					intList.Add(edgeDefinition.BTriangleIndex);
				}
				foreach (int start in floodFillHelper.Start(intList, emitSeeds: true))
				{
					if (labels[start] >= 0)
					{
						Point pt = Point.NullPoint;
						if (!errorOnInvalidBoundaries)
						{
							continue;
						}
						int tIdx = start;
						foreach (var selectMany in new List<Dictionary<(int, int), int>.KeyCollection> { _perimConstraints.Keys, _blineConstraints.Keys }.SelectMany((Dictionary<(int, int), int>.KeyCollection e) => e))
						{
							if (Tin.FindEdge(selectMany.Item1, selectMany.Item2, ref tIdx).IsNull())
							{
								pt = Tin[selectMany.Item1];
								operationResult.AddFailure(MessageCodes.GetUnderResolvedChannel(intValues[intervalIndex], pt));
							}
						}
						if (pt.IsNull())
						{
							operationResult.AddFailure(MessageCodes.GetUnderResolvedChannel(intValues[intervalIndex]));
						}
						return operationResult;
					}
					labels[start] = intValues[intervalIndex];
				}
				intSet.Add(intValues[intervalIndex]);
			}
		}
		return operationResult;
	}

	public bool SegmentViolatesConstraints(int pA, int pB, ref int tri, int ignorePoint = -1)
	{
		_tin.FindTriangleAndNearestPointIndex(_tin.Points[pA], ref tri, out var p);
		Segment segment = new Segment(_tin.Points[pA], _tin.Points[pB]);
		SpinFindTriangleToBisect(segment, pA, ref tri, out var triA, out var _);
		if (IsConstrained(pA, pB))
		{
			return CrossEdge(_tin.GetEdgeDefinition(tri, pA, pB));
		}
		for (int i = 0; i < _tin.TriangleCount; i++)
		{
			int tA = -1;
			triA = _tin.TriangleDefinition[tri];
			if (CrossTriangle(tri))
			{
				return true;
			}
			double[] doubleValues = new double[3];
			for (int j = 0; j < 3; j++)
			{
				doubleValues[j] = segment.PerpendicularSignedDistance(_tin.Points[triA.Point(j)]);
				if (Math.Abs(doubleValues[j]) < TriangulationParameters.Tolerance)
				{
					doubleValues[j] = 0.0;
				}
			}
			for (int k = 0; k < 3; k++)
			{
				int ordinal = (k + 1) % 3;
				if (doubleValues[k] < 0.0 && doubleValues[ordinal] > 0.0)
				{
					EdgeDefinition edgeDefinition = _tin.GetEdgeDefinition(tri, triA.Point(k), triA.Point(ordinal));
					if (CrossEdge(edgeDefinition))
					{
						return true;
					}
					tA = edgeDefinition.OtherTriangle(tri);
					break;
				}
				if (doubleValues[k] != 0.0)
				{
					continue;
				}
				int ordinal2 = (k + 2) % 3;
				int unity = -1;
				int unity2 = -1;
				if (doubleValues[ordinal] > 0.0 && doubleValues[ordinal2] < 0.0)
				{
					if (triA.Point(k) == pB)
					{
						return false;
					}
					if (CrossPoint(triA.Point(k)))
					{
						return true;
					}
					unity2 = triA.Point(k);
				}
				else
				{
					if (doubleValues[ordinal] < 0.0 && doubleValues[ordinal2] > 0.0)
					{
						continue;
					}
					if (doubleValues[ordinal] == 0.0)
					{
						if (doubleValues[ordinal2] < 0.0)
						{
							unity = triA.Point(ordinal);
							unity2 = triA.Point(k);
						}
						else
						{
							unity = triA.Point(k);
							unity2 = triA.Point(ordinal);
						}
					}
					else if (doubleValues[ordinal2] == 0.0)
					{
						if (doubleValues[ordinal] < 0.0)
						{
							unity = triA.Point(k);
							unity2 = triA.Point(ordinal2);
						}
						else
						{
							unity = triA.Point(ordinal2);
							unity2 = triA.Point(k);
						}
					}
				}
				if (unity >= 0)
				{
					EdgeDefinition edgeDefinition2 = _tin.GetEdgeDefinition(tri, unity, unity2);
					if (CrossEdge(edgeDefinition2))
					{
						return true;
					}
				}
				if (unity2 == pB)
				{
					return false;
				}
				tA = tri;
				Segment segment2 = new Segment(_tin.Points[unity2], segment.B);
				SpinFindTriangleToBisect(segment2, unity2, ref tA, out triA, out p);
				break;
			}
			tri = tA;
		}
		throw new Exception($"TIN traversal failed to terminate {segment.A} to {segment.B}");
		bool CrossEdge(EdgeDefinition edgeDefinition3)
		{
			if (edgeDefinition3.APointIndex == ignorePoint || edgeDefinition3.BPointIndex == ignorePoint)
			{
				return false;
			}
			return IsConstrained(edgeDefinition3.APointIndex, edgeDefinition3.BPointIndex);
		}
		bool CrossPoint(int ordinal3)
		{
			return ordinal3 != ignorePoint;
		}
		static bool CrossTriangle(int t)
		{
			return false;
		}
	}

	protected override void FlagDeletedTriangles()
	{
		if (_polygons.Count == 0)
		{
			FlagDeletedTriangles_SimpleRing();
			return;
		}
		bool[] boolValues = new bool[_tin.TriangleCount];
		for (int i = 0; i < boolValues.Length; i++)
		{
			boolValues[i] = true;
		}
		HashSet<EdgeDefinition> perimeterEdges = GetPerimeterEdges(out var _, errorOnInvalidBoundaries);
		FloodFillHelper floodfiller = DelaunayTinFactory.GetFloodfiller(_tin, perimeterEdges);
		foreach (EdgeDefinition perimeterEdge in perimeterEdges)
		{
			int bTriangleIndex = perimeterEdge.BTriangleIndex;
			if (!boolValues[bTriangleIndex])
			{
				continue;
			}
			foreach (int start in floodfiller.Start(new int[1] { bTriangleIndex }, emitSeeds: true))
			{
				boolValues[start] = false;
			}
		}
		for (int j = 0; j < boolValues.Length; j++)
		{
			if (boolValues[j])
			{
				MarkDeleted(j);
			}
		}
	}

	public double Clearance(Segment seg, ref int tri)
	{
		Tin.FindTrianglesContainingSegment(seg, ref tri, out var triangles);
		double maxValue = double.MaxValue;
		foreach (int triangle in triangles)
		{
			TriangleDefinition triangleDefinition = Tin.TriangleDefinition[triangle];
			for (int i = 0; i < 3; i++)
			{
				maxValue = Math.Min(maxValue, seg.DistanceToPointSquared(Tin.Points[triangleDefinition.Point(i)]));
				if (TriFaceIsConstrained(triangle, i))
				{
					Segment edgeSegment = Tin.GetEdgeSegment(triangle, i);
					maxValue = Math.Min(maxValue, edgeSegment.DistanceToPointSquared(seg.A));
					maxValue = Math.Min(maxValue, edgeSegment.DistanceToPointSquared(seg.B));
				}
			}
		}
		return Math.Sqrt(maxValue);
	}

	public bool CheckDelaunay(List<string> errors = null)
	{
		bool result = true;
		for (int i = 0; i < Tin.TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = Tin.TriangleDefinition[i];
			for (int j = 0; j < 3; j++)
			{
				int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(j);
				if (adjacentTriangleIdx < 0)
				{
					continue;
				}
				TriangleDefinition triangleDefinition2 = Tin.TriangleDefinition[adjacentTriangleIdx];
				int point = triangleDefinition.Point(j);
				int point2 = triangleDefinition.Point((j + 1) % 3);
				if (!IsConstrained(point, point2))
				{
					int index = triangleDefinition.Point((j + 2) % 3);
					int thirdPoint = triangleDefinition2.ThirdPoint(point, point2);
					if (ApproxInCircle(Tin.Points[point], Tin.Points[point2], Tin.Points[index], Tin.Points[thirdPoint]) > 0.0)
					{
						result = false;
						errors?.Add($"Node {thirdPoint} inside CC for triangle {i}");
					}
				}
			}
		}
		return result;
		static double ApproxInCircle(Point a, Point b, Point c, Point p)
		{
			double tolerance = 1E-10;
			Circle circle = Triangle.ComputeCircumCircle(a, b, c);
			double distanceToOverRadius = circle.Center.DistanceTo(p) / circle.Radius;
			if (distanceToOverRadius < 1.0 - tolerance)
			{
				return 1.0;
			}
			if (distanceToOverRadius > 1.0 + tolerance)
			{
				return -1.0;
			}
			return 0.0;
		}
	}

	public void DumpTin(string cmdFile = "C:\\Temp\\cmd.json")
	{
		DumpTin(_tin, cmdFile);
	}

	public static void DumpTin(Tin tin, string cmdFile = "C:\\Temp\\cmd.json", List<object> extra = null)
	{
		using FileStream utf8Json = File.Create(cmdFile);
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		List<object> objectList = new List<object>
		{
			new List<object> { "SET", "dt_raw", tin },
			new List<object> { "PYTHON", "\r\ndt=json_to_mesh(dt_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ndt.plot_edges(ax=ax,color='k',lw=0.5)\r\nax.axis('equal')\r\n" }
		};
		if (extra != null)
		{
			objectList.AddRange(extra);
		}
		JsonSerializer.Serialize(utf8Json, objectList, options);
	}
}

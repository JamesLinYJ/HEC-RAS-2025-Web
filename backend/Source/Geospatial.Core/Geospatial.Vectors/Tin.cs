using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.DataStructures;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors.Rasterizers;
using Utility;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Progress;
using Utility.Reflection;
using Utility.Systems;

namespace Geospatial.Vectors;

[DebuggerDisplay("{TriangleCount} Triangles, {Count} Points")]
public class Tin : PointCollection, IDataHash, IStreamableObject
{
	private ref struct RasterizePayload<T>
	{
		public Span<T> ZMSpan;

		public Span<float> OutSpan;

		public TriangleDefinition TriDef;
	}

	private List<TriangleDefinition> _triangleDefinition;

	private readonly Versioned<double> _areaCache = new Versioned<double>();

	private readonly Versioned<SpatialIndex<int>> _triSpIdxCache = new Versioned<SpatialIndex<int>>();

	public List<TriangleDefinition> TriangleDefinition => _triangleDefinition;

	public IReadOnlyList<Triangle> Triangles { get; private set; }

	public IReadOnlyList<Triangle3D> Triangle3D { get; private set; }

	public IReadOnlyList<Triangle4D> Triangle4D { get; private set; }

	public bool IsCleaned { get; private set; }

	public int EdgeCount
	{
		get
		{
			throw new NotImplementedException();
		}
	}

	public int TriangleCount => TriangleDefinition.Count;

	public double Area => _areaCache.Get(VersionXY, ComputeArea);

	public SpatialIndex<int> TriangleSpatialIndex => _triSpIdxCache.Get(VersionXY, ComputeTriangleSpatialIndex);

	public Tin(IEnumerable<Point> points, IEnumerable<TriangleDefinition> tris, bool isCleaned)
		: base(points)
	{
		_triangleDefinition = tris.ToList();
		IsCleaned = isCleaned;
		Initialize();
	}

	public Tin(PointCollection points, IEnumerable<TriangleDefinition> tris, bool isCleaned)
		: base(points)
	{
		_triangleDefinition = tris.ToList();
		IsCleaned = isCleaned;
		Initialize();
	}

	public Tin()
	{
		_triangleDefinition = new List<TriangleDefinition>();
		Initialize();
	}

	public Tin(IList<Point> points, int[,] connectivity)
		: base(points)
	{
		if (connectivity == null)
		{
			throw new ArgumentNullException("connectivity");
		}
		if (connectivity.GetLength(1) != 3)
		{
			throw new ArgumentException("Connectivity array needs exactly 3 columns.");
		}
		_triangleDefinition = new List<TriangleDefinition>(connectivity.GetLength(0));
		for (int i = 0; i < connectivity.GetLength(0); i++)
		{
			int p = connectivity[i, 0];
			int p2 = connectivity[i, 1];
			int p3 = connectivity[i, 2];
			_triangleDefinition.Add(new TriangleDefinition(p, p2, p3, -1, -1, -1));
		}
		RebuildAdjacencyInformationInternal(_triangleDefinition, points.Count, out var _);
		IsCleaned = true;
		Initialize();
	}

	public Tin(Memory<Point> points, int[,] connectivity, Memory<double> z, Memory<double> m)
		: base(points, z, m)
	{
		if (connectivity == null)
		{
			throw new ArgumentNullException("connectivity");
		}
		if (connectivity.GetLength(1) != 3)
		{
			throw new ArgumentException("Connectivity array needs exactly 3 columns.");
		}
		_triangleDefinition = new List<TriangleDefinition>(connectivity.GetLength(0));
		for (int i = 0; i < connectivity.GetLength(0); i++)
		{
			int p = connectivity[i, 0];
			int p2 = connectivity[i, 1];
			int p3 = connectivity[i, 2];
			_triangleDefinition.Add(new TriangleDefinition(p, p2, p3, -1, -1, -1));
		}
		RebuildAdjacencyInformationInternal(_triangleDefinition, points.Length, out var _);
		IsCleaned = true;
		Initialize();
	}

	public Tin(IList<Point> points, int[,] connectivity, int ptStart, int ptCount, int triStart, int triCount)
		: base(points.Skip(ptStart).Take(ptCount))
	{
		if (connectivity == null)
		{
			throw new ArgumentNullException("connectivity");
		}
		if (connectivity.GetLength(1) != 3)
		{
			throw new ArgumentException("Connectivity array needs exactly 3 columns.");
		}
		_triangleDefinition = new List<TriangleDefinition>(triCount);
		for (int i = triStart; i < triStart + triCount; i++)
		{
			int p = connectivity[i, 0];
			int p2 = connectivity[i, 1];
			int p3 = connectivity[i, 2];
			_triangleDefinition.Add(new TriangleDefinition(p, p2, p3, -1, -1, -1));
		}
		RebuildAdjacencyInformationInternal(_triangleDefinition, ptCount, out var _);
		IsCleaned = true;
		Initialize();
	}

	public static Tin Empty()
	{
		return new Tin();
	}

	public static Tin FromRaster(RasterDefinition rdef)
	{
		List<Point> points = new List<Point>();
		double left = rdef.Left;
		double top = rdef.Top;
		double cellSize = rdef.CellSize;
		for (int i = 0; i <= rdef.Rows; i++)
		{
			for (int j = 0; j <= rdef.Cols; j++)
			{
				double x = left + (double)j * cellSize;
				double y = top - (double)i * cellSize;
				points.Add(new Point(x, y));
			}
		}
		TriangleDefinition[] triangleDefinitions = new TriangleDefinition[rdef.CellCount * 2];
		for (int k = 0; k < rdef.Rows; k++)
		{
			for (int l = 0; l < rdef.Cols; l++)
			{
				int colsRdefTerm = k * (rdef.Cols + 1) + l;
				int p = colsRdefTerm + 1;
				int colsRdefTerm2 = colsRdefTerm + (rdef.Cols + 1);
				int p2 = colsRdefTerm2 + 1;
				int colsRdefTerm3 = 2 * (k * rdef.Cols + l);
				triangleDefinitions[colsRdefTerm3] = new TriangleDefinition(p, colsRdefTerm, colsRdefTerm2, -1, -1, -1);
				triangleDefinitions[colsRdefTerm3 + 1] = new TriangleDefinition(p, colsRdefTerm2, p2, -1, -1, -1);
			}
		}
		return RebuildAdjacencyInformation(points, triangleDefinitions);
	}

	public static Tin CreateWithBuffer(Point[] points, int[,] connectivity, double[] z, double[] m)
	{
		Tin tin = new Tin();
		tin.AssignBuffers(points, z, m);
		tin._triangleDefinition = new List<TriangleDefinition>(connectivity.GetLength(0));
		for (int i = 0; i < connectivity.GetLength(0); i++)
		{
			int p = connectivity[i, 0];
			int p2 = connectivity[i, 1];
			int p3 = connectivity[i, 2];
			tin._triangleDefinition.Add(new TriangleDefinition(p, p2, p3, -1, -1, -1));
		}
		RebuildAdjacencyInformationInternal(tin._triangleDefinition, points.Length, out var _);
		tin.IsCleaned = true;
		return tin;
	}

	public Tin GetSubsetTin(IList<int> triangleIndices)
	{
		int[] triangleIndexMap;
		int[] pointIndexMap;
		return GetSubsetTin(triangleIndices, out triangleIndexMap, out pointIndexMap);
	}

	public Tin GetSubsetTin(IList<int> triangleIndices, out int[] triangleIndexMap, out int[] pointIndexMap)
	{
		if (triangleIndices == null)
		{
			throw new ArgumentNullException("triangleIndices");
		}
		if (triangleIndices.Count == 0)
		{
			triangleIndexMap = Array.Empty<int>();
			pointIndexMap = Array.Empty<int>();
			return Empty();
		}
		int[] intValues = new int[TriangleCount];
		Array.Fill(intValues, -1);
		for (int i = 0; i < triangleIndices.Count; i++)
		{
			intValues[triangleIndices[i]] = i;
		}
		int[] intValuesArray = new int[PointCount];
		Array.Fill(intValuesArray, -1);
		int count = 0;
		for (int j = 0; j < triangleIndices.Count; j++)
		{
			TriangleDefinition triangleDefinition = _triangleDefinition[triangleIndices[j]];
			if (intValuesArray[triangleDefinition.p0] == -1)
			{
				intValuesArray[triangleDefinition.p0] = count++;
			}
			if (intValuesArray[triangleDefinition.p1] == -1)
			{
				intValuesArray[triangleDefinition.p1] = count++;
			}
			if (intValuesArray[triangleDefinition.p2] == -1)
			{
				intValuesArray[triangleDefinition.p2] = count++;
			}
		}
		triangleIndexMap = new int[triangleIndices.Count];
		for (int k = 0; k < triangleIndices.Count; k++)
		{
			triangleIndexMap[k] = triangleIndices[k];
		}
		pointIndexMap = new int[count];
		for (int l = 0; l < PointCount; l++)
		{
			int ordinal = intValuesArray[l];
			if (ordinal != -1)
			{
				pointIndexMap[ordinal] = l;
			}
		}
		Point[] points = new Point[count];
		double[] doubleValues = (HasZ ? new double[count] : null);
		double[] doubleValuesArray = (HasM ? new double[count] : null);
		for (int m = 0; m < PointCount; m++)
		{
			int ordinal2 = intValuesArray[m];
			if (ordinal2 != -1)
			{
				points[ordinal2] = _points[m];
				if (doubleValues != null)
				{
					doubleValues[ordinal2] = Z[m];
				}
				if (doubleValuesArray != null)
				{
					doubleValuesArray[ordinal2] = M[m];
				}
			}
		}
		List<TriangleDefinition> triangleDefinitionList = new List<TriangleDefinition>(triangleIndices.Count);
		for (int n = 0; n < triangleIndices.Count; n++)
		{
			TriangleDefinition triangleDefinition2 = _triangleDefinition[triangleIndices[n]];
			int p = intValuesArray[triangleDefinition2.p0];
			int p2 = intValuesArray[triangleDefinition2.p1];
			int p3 = intValuesArray[triangleDefinition2.p2];
			int at = ((triangleDefinition2.at0 >= 0 && triangleDefinition2.at0 < intValues.Length) ? intValues[triangleDefinition2.at0] : (-1));
			int at2 = ((triangleDefinition2.at1 >= 0 && triangleDefinition2.at1 < intValues.Length) ? intValues[triangleDefinition2.at1] : (-1));
			int at3 = ((triangleDefinition2.at2 >= 0 && triangleDefinition2.at2 < intValues.Length) ? intValues[triangleDefinition2.at2] : (-1));
			triangleDefinitionList.Add(new TriangleDefinition(p, p2, p3, at, at2, at3, triangleDefinition2.IsDeleted));
		}
		Tin tin = new Tin();
		tin.AddRange(points);
		if (doubleValues != null)
		{
			tin.TrackZ(doubleValues);
		}
		if (doubleValuesArray != null)
		{
			tin.TrackM(doubleValuesArray);
		}
		tin._triangleDefinition = triangleDefinitionList;
		tin.IsCleaned = triangleDefinitionList.All((TriangleDefinition td) => !td.IsDeleted);
		tin.Initialize();
		return tin;
	}

	public Tin GetSubsetTin(int triangleIndex, int radius)
	{
		if (triangleIndex < 0 || triangleIndex >= TriangleCount)
		{
			throw new ArgumentOutOfRangeException("triangleIndex");
		}
		if (radius < 0)
		{
			throw new ArgumentOutOfRangeException("radius");
		}
		HashSet<int> intSet = new HashSet<int> { triangleIndex };
		List<int> intList = new List<int> { triangleIndex };
		for (int i = 0; i < radius; i++)
		{
			List<int> intListList = new List<int>();
			foreach (int ordinal in intList)
			{
				TriangleDefinition triangleDefinition = _triangleDefinition[ordinal];
				TryAddNeighbor(triangleDefinition.at0, intSet, intListList);
				TryAddNeighbor(triangleDefinition.at1, intSet, intListList);
				TryAddNeighbor(triangleDefinition.at2, intSet, intListList);
			}
			if (intListList.Count == 0)
			{
				break;
			}
			intList = intListList;
		}
		List<int> intSetList = intSet.ToList();
		intSetList.Sort();
		return GetSubsetTin(intSetList);
	}

	public Tin GetSubsetTin(Extent extent)
	{
		List<int> elements = TriangleSpatialIndex.GetElements(extent);
		List<int> intList = new List<int>(elements.Count);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (!_triangleDefinition[element].IsDeleted && Triangles[element].Extent.Overlaps(in extent))
			{
				intList.Add(element);
			}
		}
		intList.Sort();
		return GetSubsetTin(intList);
	}

	public Tin GetSubsetTin(Extent extent, out int[] triangleIndexMap, out int[] pointIndexMap)
	{
		List<int> elements = TriangleSpatialIndex.GetElements(extent);
		List<int> intList = new List<int>(elements.Count);
		for (int i = 0; i < elements.Count; i++)
		{
			int element = elements[i];
			if (!_triangleDefinition[element].IsDeleted && Triangles[element].Extent.Overlaps(in extent))
			{
				intList.Add(element);
			}
		}
		intList.Sort();
		return GetSubsetTin(intList, out triangleIndexMap, out pointIndexMap);
	}

	private static void TryAddNeighbor(int adjIdx, HashSet<int> visited, List<int> nextFrontier)
	{
		if (adjIdx >= 0 && visited.Add(adjIdx))
		{
			nextFrontier.Add(adjIdx);
		}
	}

	private void Initialize()
	{
		Triangles = new VirtualTriangleCollection(this);
		Triangle3D = new VirtualTriangle3DCollection(this);
		Triangle4D = new VirtualTriangle4DCollection(this);
	}

	public void RemoveDeletedTriangles()
	{
		int ordinal = 0;
		for (int i = 0; i < _triangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition = _triangleDefinition[i];
			if (triangleDefinition.IsDeleted)
			{
				if (triangleDefinition.at0 != -1)
				{
					int at = triangleDefinition.at0;
					TriangleDefinition triangleDefinition2 = _triangleDefinition[at];
					triangleDefinition2.ChangeAdjacentTriangle(i, -1);
					_triangleDefinition[at] = triangleDefinition2;
				}
				if (triangleDefinition.at1 != -1)
				{
					int at2 = triangleDefinition.at1;
					TriangleDefinition triangleDefinition3 = _triangleDefinition[at2];
					triangleDefinition3.ChangeAdjacentTriangle(i, -1);
					_triangleDefinition[at2] = triangleDefinition3;
				}
				if (triangleDefinition.at2 != -1)
				{
					int at3 = triangleDefinition.at2;
					TriangleDefinition triangleDefinition4 = _triangleDefinition[at3];
					triangleDefinition4.ChangeAdjacentTriangle(i, -1);
					_triangleDefinition[at3] = triangleDefinition4;
				}
				continue;
			}
			if (triangleDefinition.at0 != -1)
			{
				int at4 = triangleDefinition.at0;
				TriangleDefinition triangleDefinition5 = _triangleDefinition[at4];
				triangleDefinition5.ChangeAdjacentTriangle(i, ordinal);
				_triangleDefinition[at4] = triangleDefinition5;
			}
			if (triangleDefinition.at1 != -1)
			{
				int at5 = triangleDefinition.at1;
				TriangleDefinition triangleDefinition6 = _triangleDefinition[at5];
				triangleDefinition6.ChangeAdjacentTriangle(i, ordinal);
				_triangleDefinition[at5] = triangleDefinition6;
			}
			if (triangleDefinition.at2 != -1)
			{
				int at6 = triangleDefinition.at2;
				TriangleDefinition triangleDefinition7 = _triangleDefinition[at6];
				triangleDefinition7.ChangeAdjacentTriangle(i, ordinal);
				_triangleDefinition[at6] = triangleDefinition7;
			}
			_triangleDefinition[ordinal] = triangleDefinition;
			ordinal++;
		}
		int triangleDefinitionTerm = _triangleDefinition.Count - ordinal;
		if (triangleDefinitionTerm > 0)
		{
			_triangleDefinition.RemoveRange(ordinal, triangleDefinitionTerm);
		}
		SqueezePoints();
		PointDataModified();
		IsCleaned = true;
	}

	private void SqueezePoints()
	{
		bool[] usedPointArray = GetUsedPointArray();
		int[] remappedPoints = GetRemappedPoints(usedPointArray, out var pointCount);
		for (int i = 0; i < _points.Count; i++)
		{
			int remappedPoint = remappedPoints[i];
			if (remappedPoint != -1)
			{
				_points[remappedPoint] = _points[i];
				if (HasZ)
				{
					_z[remappedPoint] = _z[i];
				}
				if (HasM)
				{
					_m[remappedPoint] = _m[i];
				}
			}
		}
		_points.RemoveRange(pointCount, _points.Count - pointCount);
		if (HasZ)
		{
			_z.RemoveRange(pointCount, _z.Count - pointCount);
		}
		if (HasM)
		{
			_m.RemoveRange(pointCount, _m.Count - pointCount);
		}
		for (int j = 0; j < _triangleDefinition.Count; j++)
		{
			TriangleDefinition triangleDefinition = _triangleDefinition[j];
			TriangleDefinition triangleDefinition2 = new TriangleDefinition(remappedPoints[triangleDefinition.p0], remappedPoints[triangleDefinition.p1], remappedPoints[triangleDefinition.p2], triangleDefinition.at0, triangleDefinition.at1, triangleDefinition.at2, triangleDefinition.IsDeleted);
			_triangleDefinition[j] = triangleDefinition2;
		}
	}

	private bool[] GetUsedPointArray()
	{
		bool[] usedPointArray = new bool[PointCount];
		for (int i = 0; i < _triangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition = _triangleDefinition[i];
			if (!triangleDefinition.IsDeleted)
			{
				usedPointArray[triangleDefinition.p0] = true;
				usedPointArray[triangleDefinition.p1] = true;
				usedPointArray[triangleDefinition.p2] = true;
			}
		}
		return usedPointArray;
	}

	private int[] GetRemappedPoints(bool[] pointsUsed, out int pointCount)
	{
		int[] remappedPoints = new int[PointCount];
		pointCount = 0;
		for (int i = 0; i < PointCount; i++)
		{
			if (pointsUsed[i])
			{
				remappedPoints[i] = pointCount;
				pointCount++;
			}
			else
			{
				remappedPoints[i] = -1;
			}
		}
		return remappedPoints;
	}

	public bool Validate(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		bool success = true;
		Action<int, int> action = (int curTriIdx, int oppTriIdx) =>
		{
			TriangleDefinition triangleDefinition2 = TriangleDefinition[curTriIdx];
			if (IsCleaned && triangleDefinition2.IsDeleted)
			{
				prog.ReportMessage("Triangle " + curTriIdx + " is deleted, but the TIN claims it's been cleaned.");
				success = false;
			}
			if (oppTriIdx != -1)
			{
				TriangleDefinition triangleDefinition3 = TriangleDefinition[oppTriIdx];
				_ = triangleDefinition2.IsDeleted;
				_ = triangleDefinition3.IsDeleted;
				if (triangleDefinition3.at0 != curTriIdx && triangleDefinition3.at1 != curTriIdx && triangleDefinition3.at2 != curTriIdx)
				{
					success = false;
					prog.ReportMessage("Error with bidirectional pointers: " + curTriIdx + " <-> " + oppTriIdx);
				}
			}
		};
		for (int intervalIndex = 0; intervalIndex < TriangleCount; intervalIndex++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[intervalIndex];
			action(intervalIndex, triangleDefinition.at0);
			action(intervalIndex, triangleDefinition.at1);
			action(intervalIndex, triangleDefinition.at2);
			Triangle triangle = Triangles[intervalIndex];
			if (triangle.IsColinear())
			{
				prog.ReportMessage("Triangle points are colinear: " + intervalIndex);
				success = false;
			}
			if (Vector.CrossProduct(triangle.PointA.X, triangle.PointA.Y, triangle.PointB.X, triangle.PointB.Y, triangle.PointC.X, triangle.PointC.Y) <= 0.0)
			{
				prog.ReportMessage("Triangle orientation is reversed: " + intervalIndex);
				success = false;
			}
		}
		return success;
	}

	protected static bool Validate(PointCollection points, List<TriangleDefinition> triangles)
	{
		for (int i = 0; i < triangles.Count; i++)
		{
			TriangleDefinition triangleDefinition = triangles[i];
			if (points[triangleDefinition.p0].X == points[triangleDefinition.p1].X && points[triangleDefinition.p0].X == points[triangleDefinition.p2].X)
			{
				return false;
			}
			if (points[triangleDefinition.p0].Y == points[triangleDefinition.p1].Y && points[triangleDefinition.p0].Y == points[triangleDefinition.p2].Y)
			{
				return false;
			}
			if (triangleDefinition.at0 == -1 && triangleDefinition.at1 == -1 && triangleDefinition.at2 == -1)
			{
				return false;
			}
			if (triangleDefinition.at0 != -1)
			{
				TriangleDefinition triangleDefinition2 = triangles[triangleDefinition.at0];
				if (triangleDefinition2.at0 != i && triangleDefinition2.at1 != i && triangleDefinition2.at2 != i)
				{
					return false;
				}
				if (triangleDefinition.at0 == triangleDefinition.at1 || triangleDefinition.at0 == triangleDefinition.at2)
				{
					return false;
				}
			}
			if (triangleDefinition.at1 != -1)
			{
				TriangleDefinition triangleDefinition3 = triangles[triangleDefinition.at1];
				if (triangleDefinition3.at0 != i && triangleDefinition3.at1 != i && triangleDefinition3.at2 != i)
				{
					return false;
				}
				if (triangleDefinition.at1 == triangleDefinition.at0 || triangleDefinition.at1 == triangleDefinition.at2)
				{
					return false;
				}
			}
			if (triangleDefinition.at2 != -1)
			{
				TriangleDefinition triangleDefinition4 = triangles[triangleDefinition.at2];
				if (triangleDefinition4.at0 != i && triangleDefinition4.at1 != i && triangleDefinition4.at2 != i)
				{
					return false;
				}
				if (triangleDefinition.at2 == triangleDefinition.at0 || triangleDefinition.at2 == triangleDefinition.at1)
				{
					return false;
				}
			}
		}
		return true;
	}

	public static Tin RebuildAdjacencyInformation(IList<Point> Points, IList<TriangleDefinition> triangleDefinitions)
	{
		RebuildAdjacencyInformationInternal(triangleDefinitions, Points.Count, out var hasDeleted);
		return new Tin(Points, triangleDefinitions, !hasDeleted);
	}

	private static void RebuildAdjacencyInformationInternal(IList<TriangleDefinition> triangleDefinitions, int ptCt, out bool hasDeleted)
	{
		if (triangleDefinitions.Count == 0)
		{
			hasDeleted = false;
			return;
		}
		List<int>[] intListArray = new List<int>[ptCt];
		List<int>[] intListArrayList = new List<int>[ptCt];
		for (int i = 0; i < ptCt; i++)
		{
			intListArray[i] = new List<int>();
			intListArrayList[i] = new List<int>();
		}
		hasDeleted = false;
		for (int j = 0; j < triangleDefinitions.Count; j++)
		{
			TriangleDefinition triangleDefinition = triangleDefinitions[j];
			if (triangleDefinition.IsDeleted)
			{
				hasDeleted = true;
			}
			triangleDefinition.at0 = -1;
			triangleDefinition.at1 = -1;
			triangleDefinition.at2 = -1;
			triangleDefinitions[j] = triangleDefinition;
		}
		for (int k = 0; k < triangleDefinitions.Count; k++)
		{
			for (int l = 0; l <= 2; l++)
			{
				TriangleDefinition triangleDefinition2 = triangleDefinitions[k];
				triangleDefinition2.EdgePoints(l, out var ptA, out var ptB);
				int indexOf = intListArray[ptA].IndexOf(ptB);
				if (indexOf == -1)
				{
					intListArray[ptA].Add(ptB);
					intListArray[ptB].Add(ptA);
					intListArrayList[ptA].Add(k);
					intListArrayList[ptB].Add(k);
				}
				else
				{
					int ptAIndexOfTerm = intListArrayList[ptA][indexOf];
					triangleDefinition2.SetAdjacentTriangle(ptA, ptB, ptAIndexOfTerm);
					TriangleDefinition triangleDefinition3 = triangleDefinitions[ptAIndexOfTerm];
					triangleDefinition3.SetAdjacentTriangle(ptA, ptB, k);
					triangleDefinitions[k] = triangleDefinition2;
					triangleDefinitions[ptAIndexOfTerm] = triangleDefinition3;
				}
			}
		}
	}

	public void FindExit(int startPointIdx, int anyConnectedTriIdx, Point target, double tolerance, out EdgeDefinition exitEdge, out int exitTriIdx)
	{
		List<int> trianglesAroundPointCCW = TrianglesAroundPointCCW(startPointIdx, anyConnectedTriIdx);
		Point ptA = Points[startPointIdx];
		Vector vector = ptA.To(target);
		Segment segment = new Segment(ptA, target);
		foreach (int trianglesAroundPointCCW2 in trianglesAroundPointCCW)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[trianglesAroundPointCCW2];
			int clockwisePointWithinTriangle = triangleDefinition.GetClockwisePointWithinTriangle(startPointIdx);
			if (segment.IsColinear(Points[clockwisePointWithinTriangle], tolerance, out var sameDirection) && sameDirection)
			{
				exitTriIdx = -1;
				exitEdge = GetEdgeDefinition(trianglesAroundPointCCW2, triangleDefinition.GetEdge012(startPointIdx, clockwisePointWithinTriangle));
				return;
			}
		}
		foreach (int trianglesAroundPointCCW3 in trianglesAroundPointCCW)
		{
			TriangleDefinition triangleDefinition2 = TriangleDefinition[trianglesAroundPointCCW3];
			int clockwisePointWithinTriangle2 = triangleDefinition2.GetClockwisePointWithinTriangle(startPointIdx);
			Point ptB = Points[clockwisePointWithinTriangle2];
			Vector other = new Segment(ptA, ptB).AsVector();
			if (!(vector.CrossProduct(other) < 0.0))
			{
				int counterClockwisePointWithinTriangle = triangleDefinition2.GetCounterClockwisePointWithinTriangle(startPointIdx);
				Point ptB2 = Points[counterClockwisePointWithinTriangle];
				Vector other2 = new Segment(ptA, ptB2).AsVector();
				if (!(vector.CrossProduct(other2) > 0.0))
				{
					exitTriIdx = trianglesAroundPointCCW3;
					exitEdge = EdgeDefinition.NullEdgeDefinition();
					return;
				}
			}
		}
		throw new Exception("Likely invalid triangulation");
	}

	public void UpdatePointZ(int p, Point3D point)
	{
		UpdatePointZ(p, point.Z);
	}

	public void UpdatePointZ(int p, double z)
	{
		if (HasZ)
		{
			_z[p] = z;
		}
	}

	public void UpdatePointZM(int p, Point4D point)
	{
		UpdatePointZM(p, point.Z, point.M);
	}

	public void UpdatePointZM(int p, double z, double m)
	{
		if (HasZ)
		{
			_z[p] = z;
		}
		if (HasM)
		{
			_m[p] = m;
		}
	}

	public override void Clear()
	{
		base.Clear();
		_triangleDefinition.Clear();
	}

	public Segment Edge(EdgeDefinition ed)
	{
		return new Segment(_points[ed.APointIndex], _points[ed.BPointIndex]);
	}

	public Segment Edge(int t, int e012)
	{
		TriangleDefinition[t].EdgePoints(e012, out var ptA, out var ptB);
		return new Segment(_points[ptA], _points[ptB]);
	}

	public Segment3D Edge3D(int t, int e012)
	{
		if (!HasZ)
		{
			throw new NotSupportedException();
		}
		int index = TriangleDefinition[t].EdgePointA(e012);
		int index2 = TriangleDefinition[t].EdgePointB(e012);
		return new Segment3D(new Point3D(Points[index], _z[index]), new Point3D(Points[index2], _z[index2]));
	}

	public Segment4D Edge4D(int t, int e012)
	{
		if (!HasZ)
		{
			throw new NotSupportedException();
		}
		if (!HasM)
		{
			throw new NotSupportedException();
		}
		int index = TriangleDefinition[t].EdgePointA(e012);
		int index2 = TriangleDefinition[t].EdgePointB(e012);
		return new Segment4D(Point4D[index], Point4D[index2]);
	}

	public Segment Edge(int eIdx)
	{
		throw new NotImplementedException("Edges are not independently indexed yet.");
	}

	public void NearestPerimeterEdge(Point pt, out EdgeDefinition edgeDef, out double fractionAlong)
	{
		NearestEdge(pt, GetAllPerimeterEdges(), out edgeDef, out fractionAlong);
	}

	public void NearestExternalEdge(Point pt, out EdgeDefinition edgeDef, out double fractionAlong)
	{
		NearestEdge(pt, GetAllExternalEdges(), out edgeDef, out fractionAlong);
	}

	private void NearestEdge(Point pt, List<EdgeDefinition> edges, out EdgeDefinition edgeDef, out double fractionAlong)
	{
		double maxValue = double.MaxValue;
		if (edges.IsNullOrEmpty())
		{
			edgeDef = EdgeDefinition.NullEdgeDefinition();
			fractionAlong = double.NaN;
			return;
		}
		fractionAlong = 0.0;
		edgeDef = default;
		for (int i = 0; i < edges.Count; i++)
		{
			EdgeDefinition edgeDefinition = edges[i];
			Segment segment = Edge(edgeDefinition.ATriangleIndex, edgeDefinition.AEdge012);
			double distanceToPoint = segment.DistanceToPoint(pt);
			if (distanceToPoint < maxValue)
			{
				maxValue = distanceToPoint;
				fractionAlong = segment.FractionAlong(pt);
				edgeDef = edgeDefinition;
				fractionAlong = Math.Min(Math.Max(fractionAlong, 0.0), 1.0);
			}
		}
		if (maxValue != double.MaxValue)
		{
			return;
		}
		throw new Exception("No valid edges found - possible invalid TIN");
	}

	public int FindTriangleContainingPoint(Point point)
	{
		int t = 0;
		FindTriangleContainingPoint(point, ref t);
		return t;
	}

	public void FindTriangleContainingPoint(Point point, ref int t)
	{
		int count = _triangleDefinition.Count;
		if (t < 0 || t > count - 1)
		{
			t = 0;
		}
		int ordinal = 0;
		int ordinal2 = -1;
		while (t != -1 && ordinal != count)
		{
			TriangleDefinition triangleDefinition = _triangleDefinition[t];
			Point a = _points[triangleDefinition.p0];
			Point b = _points[triangleDefinition.p1];
			Point b2 = _points[triangleDefinition.p2];
			double d = Vector.CrossProduct(in a, in b, in point);
			double d2 = Vector.CrossProduct(in b, in b2, in point);
			double d3 = Vector.CrossProduct(in b2, in a, in point);
			MathHelpers.Min3Index(d, d2, d3, out var idx, out var localValue);
			if (localValue >= 0.0)
			{
				return;
			}
			int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(idx);
			if (ordinal2 == adjacentTriangleIdx)
			{
				t = Math.Min(adjacentTriangleIdx, t);
				return;
			}
			ordinal2 = t;
			t = adjacentTriangleIdx;
			ordinal++;
		}
		t = -1;
	}

	public void FindTriangleContainingZMPoint(Point zmPoint, ref int t)
	{
		if (t < 0 || _triangleDefinition[t].IsDeleted)
		{
			t = 0;
			while (t < _triangleDefinition.Count && _triangleDefinition[t].IsDeleted)
			{
				t++;
			}
		}
		int ordinal = 0;
		int ordinal2 = -1;
		while (t != -1 && ordinal != _triangleDefinition.Count)
		{
			TriangleDefinition triangleDefinition = _triangleDefinition[t];
			Point a = new Point(Z[triangleDefinition.p0], M[triangleDefinition.p0]);
			Point b = new Point(Z[triangleDefinition.p1], M[triangleDefinition.p1]);
			Point b2 = new Point(Z[triangleDefinition.p2], M[triangleDefinition.p2]);
			double crossProduct = Vector.CrossProduct(in a, in b, in zmPoint);
			double crossProduct2 = Vector.CrossProduct(in b, in b2, in zmPoint);
			double crossProduct3 = Vector.CrossProduct(in b2, in a, in zmPoint);
			MathHelpers.Min3Index(crossProduct, crossProduct2, crossProduct3, out var idx, out var localValue);
			int intervalIndex;
			if (localValue >= 0.0)
			{
				if (Vector.CrossProduct(in a, in b, in b2) != 0.0)
				{
					return;
				}
				Triangle4D triangle4D = Triangle4D[t];
				triangle4D.ComputeXY(zmPoint, out var xyPoint);
				if (xyPoint.IsValid())
				{
					return;
				}
				int unity = -1;
				double distanceSquaredTo = zmPoint.DistanceSquaredTo(new Point(triangle4D.PointA.Z, triangle4D.PointA.M));
				double distanceSquaredTo2 = zmPoint.DistanceSquaredTo(new Point(triangle4D.PointB.Z, triangle4D.PointB.M));
				double distanceSquaredTo3 = zmPoint.DistanceSquaredTo(new Point(triangle4D.PointC.Z, triangle4D.PointC.M));
				if (distanceSquaredTo < distanceSquaredTo2 && distanceSquaredTo < distanceSquaredTo3)
				{
					unity = TriangleDefinition[t].p0;
				}
				else
				{
					unity = ((!(distanceSquaredTo2 < distanceSquaredTo) || !(distanceSquaredTo2 < distanceSquaredTo3)) ? TriangleDefinition[t].p2 : TriangleDefinition[t].p1);
				}
				int getCounterClockwiseTriangleAroundPointTerm = t;
				do
				{
					getCounterClockwiseTriangleAroundPointTerm = TriangleDefinition[getCounterClockwiseTriangleAroundPointTerm].GetCounterClockwiseTriangleAroundPoint(unity);
				}
				while (TriangleDefinition[getCounterClockwiseTriangleAroundPointTerm].IsDeleted && getCounterClockwiseTriangleAroundPointTerm != t && getCounterClockwiseTriangleAroundPointTerm != -1);
				if (t == getCounterClockwiseTriangleAroundPointTerm || getCounterClockwiseTriangleAroundPointTerm == -1)
				{
					throw new Exception("Likely invalid triangulation");
				}
				intervalIndex = getCounterClockwiseTriangleAroundPointTerm;
			}
			else
			{
				intervalIndex = triangleDefinition.AdjacentTriangleIdx(idx);
			}
			if (_triangleDefinition[intervalIndex].IsDeleted)
			{
				if (crossProduct < 0.0 && idx != 0)
				{
					idx = 0;
				}
				else if (crossProduct2 < 0.0 && idx != 1)
				{
					idx = 1;
				}
				else
				{
					if (!(crossProduct3 < 0.0) || idx == 2)
					{
						return;
					}
					idx = 2;
				}
				intervalIndex = triangleDefinition.AdjacentTriangleIdx(idx);
				if (_triangleDefinition[intervalIndex].IsDeleted)
				{
					return;
				}
			}
			if (ordinal2 == intervalIndex)
			{
				t = Math.Min(intervalIndex, t);
				return;
			}
			ordinal2 = t;
			t = intervalIndex;
			ordinal++;
		}
		t = -1;
	}

	public void FindTrianglesContainingSegment(Segment seg, ref int t, out List<int> triangles)
	{
		triangles = new List<int>();
		if (!seg.ClipToExtent(Extent))
		{
			return;
		}
		FindTriangleContainingPoint(seg.A, ref t);
		int t2 = t;
		FindTriangleContainingPoint(seg.B, ref t2);
		if (t == -1 && t2 == -1)
		{
			throw new Exception("Numeric error in FindTrianglesContainingSegment");
		}
		if (t >= 0)
		{
			triangles.Add(t);
			if (t == t2)
			{
				return;
			}
		}
		if (t2 >= 0)
		{
			triangles.Add(t2);
		}
		Queue<int> triangles2 = new Queue<int>(triangles);
		HashSet<int> trianglesSet = new HashSet<int>(triangles);
		int triangleCount = TriangleCount;
		while (triangleCount >= 0 && triangles2.TryDequeue(out t))
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[t];
			for (int i = 0; i < 3; i++)
			{
				int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(i);
				if (adjacentTriangleIdx >= 0 && !trianglesSet.Contains(adjacentTriangleIdx))
				{
					trianglesSet.Add(adjacentTriangleIdx);
					Triangle tri = Triangles[adjacentTriangleIdx];
					if (SegmentIntersectsTriangle(seg, tri))
					{
						triangles.Add(adjacentTriangleIdx);
						triangles2.Enqueue(adjacentTriangleIdx);
					}
				}
			}
			triangleCount--;
		}
		if (triangleCount >= 0)
		{
			return;
		}
		throw new Exception("Numeric error in FindTrianglesContainingSegment");
		static bool SegmentIntersectsSegment(Segment segment, Segment segment2)
		{
			int counterClockwise = FilteredOrientation.CounterClockwise(segment.A, segment.B, segment2.A);
			int counterClockwise2 = FilteredOrientation.CounterClockwise(segment.A, segment.B, segment2.B);
			int counterClockwise3 = FilteredOrientation.CounterClockwise(segment2.A, segment2.B, segment.A);
			int counterClockwise4 = FilteredOrientation.CounterClockwise(segment2.A, segment2.B, segment.B);
			if (counterClockwise * counterClockwise2 * counterClockwise3 * counterClockwise4 != 0)
			{
				if (counterClockwise * counterClockwise2 < 0)
				{
					return counterClockwise3 * counterClockwise4 < 0;
				}
				return false;
			}
			if (counterClockwise * counterClockwise2 == 0 && counterClockwise3 * counterClockwise4 <= 0)
			{
				return true;
			}
			if (counterClockwise3 * counterClockwise4 == 0 && counterClockwise * counterClockwise2 <= 0)
			{
				return true;
			}
			return false;
		}
		static bool SegmentIntersectsTriangle(Segment seg2, Triangle triangle)
		{
			if (!SegmentIntersectsSegment(seg2, triangle.Edge0) && !SegmentIntersectsSegment(seg2, triangle.Edge1))
			{
				return SegmentIntersectsSegment(seg2, triangle.Edge2);
			}
			return true;
		}
	}

	public void FindTriangleCheckNearbyCorners(Point point, double nearTolerance, ref int t, out int p)
	{
		FindTriangleContainingPoint(point, ref t);
		if (t == -1)
		{
			p = -1;
			return;
		}
		TriangleDefinition triangleDefinition = TriangleDefinition[t];
		if (point.CloseToXY(Points[triangleDefinition.p0], nearTolerance) || point.CloseToXY(Points[triangleDefinition.p1], nearTolerance) || point.CloseToXY(Points[triangleDefinition.p2], nearTolerance))
		{
			p = ClosestPoint(point, triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2);
		}
		else
		{
			p = -1;
		}
	}

	public void FindTriangleCheckNearbyCornersAndEdges(Point point, double nearTolerance, ref int t, out int p, out EdgeDefinition ed)
	{
		FindTriangleContainingPoint(point, ref t);
		ed = EdgeDefinition.NullEdgeDefinition();
		if (t == -1)
		{
			p = -1;
			return;
		}
		TriangleDefinition triangleDefinition = TriangleDefinition[t];
		if (point.CloseToXY(Points[triangleDefinition.p0], nearTolerance) || point.CloseToXY(Points[triangleDefinition.p1], nearTolerance) || point.CloseToXY(Points[triangleDefinition.p2], nearTolerance))
		{
			p = ClosestPoint(point, triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2);
		}
		else
		{
			p = -1;
		}
		if (p == -1)
		{
			if (new Segment(Points[triangleDefinition.p0], Points[triangleDefinition.p1]).DistanceToPoint(point) < nearTolerance)
			{
				ed = GetEdgeDefinition(t, 0);
			}
			else if (new Segment(Points[triangleDefinition.p1], Points[triangleDefinition.p2]).DistanceToPoint(point) < nearTolerance)
			{
				ed = GetEdgeDefinition(t, 1);
			}
			else if (new Segment(Points[triangleDefinition.p2], Points[triangleDefinition.p0]).DistanceToPoint(point) < nearTolerance)
			{
				ed = GetEdgeDefinition(t, 2);
			}
		}
	}

	public void FindTriangleAndNearestPointIndex(Point point, ref int t, out int p)
	{
		FindTriangleContainingPoint(point, ref t);
		if (t == -1)
		{
			p = -1;
			return;
		}
		TriangleDefinition triangleDefinition = TriangleDefinition[t];
		p = ClosestPoint(point, triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2);
	}

	private int ClosestPoint(Point point, int p0, int p1, int p2)
	{
		double distanceTo = point.DistanceTo(Points[p0]);
		double distanceTo2 = point.DistanceTo(Points[p1]);
		double distanceTo3 = point.DistanceTo(Points[p2]);
		if (distanceTo < distanceTo2)
		{
			if (distanceTo < distanceTo3)
			{
				return p0;
			}
			return p2;
		}
		if (distanceTo2 < distanceTo3)
		{
			return p1;
		}
		return p2;
	}

	public List<Tuple<int, int, int>> TriEdgeTriCCW(int ptIdx, int triIdx)
	{
		List<int> trianglesAroundPointCCW = TrianglesAroundPointCCW(ptIdx, triIdx);
		List<Tuple<int, int, int>> tupleList = new List<Tuple<int, int, int>>();
		TriangleDefinition triangleDefinition;
		int counterClockwisePointWithinTriangle;
		for (int i = 0; i < trianglesAroundPointCCW.Count; i++)
		{
			triangleDefinition = _triangleDefinition[trianglesAroundPointCCW[i]];
			int clockwiseTriangleAroundPoint = triangleDefinition.GetClockwiseTriangleAroundPoint(ptIdx);
			counterClockwisePointWithinTriangle = triangleDefinition.GetCounterClockwisePointWithinTriangle(ptIdx);
			int trianglesAroundPointCCW2 = trianglesAroundPointCCW[i];
			tupleList.Add(new Tuple<int, int, int>(clockwiseTriangleAroundPoint, counterClockwisePointWithinTriangle, trianglesAroundPointCCW2));
		}
		triangleDefinition = _triangleDefinition[trianglesAroundPointCCW.Last()];
		counterClockwisePointWithinTriangle = triangleDefinition.GetClockwisePointWithinTriangle(ptIdx);
		if (counterClockwisePointWithinTriangle != tupleList[0].Item2)
		{
			int clockwiseTriangleAroundPoint = trianglesAroundPointCCW.Last();
			int counterClockwiseTriangleAroundPoint = triangleDefinition.GetCounterClockwiseTriangleAroundPoint(ptIdx);
			tupleList.Add(new Tuple<int, int, int>(clockwiseTriangleAroundPoint, counterClockwisePointWithinTriangle, counterClockwiseTriangleAroundPoint));
		}
		return tupleList;
	}

	public EdgeDefinition FindEdge(int pA, int pB, bool strictOrder = false)
	{
		int tIdx = 0;
		return FindEdge(pA, pB, ref tIdx, strictOrder);
	}

	public EdgeDefinition FindEdge(int pA, int pB, ref int tIdx, bool strictOrder = false)
	{
		Point point = Points[pA];
		FindTriangleAndNearestPointIndex(point, ref tIdx, out var _);
		foreach (EdgeDefinition edgesAroundPointCCW in EdgesAroundPointCCW(pA, tIdx))
		{
			if (edgesAroundPointCCW.APointIndex == pA && edgesAroundPointCCW.BPointIndex == pB)
			{
				return edgesAroundPointCCW;
			}
			if (!strictOrder && edgesAroundPointCCW.APointIndex == pB && edgesAroundPointCCW.BPointIndex == pA)
			{
				return edgesAroundPointCCW;
			}
		}
		return EdgeDefinition.NullEdgeDefinition();
	}

	public List<EdgeDefinition> EdgesAroundPointCCW(int ptIdx, int triIdx, List<EdgeDefinition> edgeScratchSpace = null, List<int> fidScratchSpace = null)
	{
		List<int> trianglesAroundPointCCW = TrianglesAroundPointCCW(ptIdx, triIdx, fidScratchSpace);
		edgeScratchSpace?.Clear();
		List<EdgeDefinition> edgeScratchSpaceList = edgeScratchSpace ?? new List<EdgeDefinition>();
		int count = trianglesAroundPointCCW.Count;
		for (int i = 0; i < count; i++)
		{
			int trianglesAroundPointCCW2 = trianglesAroundPointCCW[i];
			TriangleDefinition triangleDefinition = _triangleDefinition[trianglesAroundPointCCW2];
			int counterClockwisePointWithinTriangle = triangleDefinition.GetCounterClockwisePointWithinTriangle(ptIdx);
			int clockwiseTriangleAroundPoint = triangleDefinition.GetClockwiseTriangleAroundPoint(ptIdx);
			int ccwTriIdx = trianglesAroundPointCCW2;
			edgeScratchSpaceList.Add(AddEdge(TriangleDefinition, ptIdx, clockwiseTriangleAroundPoint, counterClockwisePointWithinTriangle, ccwTriIdx));
		}
		TriangleDefinition triangleDefinition2 = _triangleDefinition[trianglesAroundPointCCW[trianglesAroundPointCCW.Count - 1]];
		int clockwisePointWithinTriangle = triangleDefinition2.GetClockwisePointWithinTriangle(ptIdx);
		if (clockwisePointWithinTriangle != edgeScratchSpaceList[0].APointIndex)
		{
			int cwTriIdx = trianglesAroundPointCCW[trianglesAroundPointCCW.Count - 1];
			int counterClockwiseTriangleAroundPoint = triangleDefinition2.GetCounterClockwiseTriangleAroundPoint(ptIdx);
			edgeScratchSpaceList.Add(AddEdge(TriangleDefinition, ptIdx, cwTriIdx, clockwisePointWithinTriangle, counterClockwiseTriangleAroundPoint));
		}
		return edgeScratchSpaceList;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static EdgeDefinition AddEdge(List<TriangleDefinition> triangleDefinitions, int ordinal, int ordinal2, int oppPtIdx, int ordinal3)
		{
			int e012A = -1;
			int e012B = -1;
			if (ordinal2 != -1)
			{
				e012A = triangleDefinitions[ordinal2].GetEdge012(ordinal, oppPtIdx);
			}
			if (ordinal3 != -1)
			{
				e012B = triangleDefinitions[ordinal3].GetEdge012(ordinal, oppPtIdx);
			}
			return new EdgeDefinition(ordinal2, e012A, ordinal3, e012B, oppPtIdx, ordinal);
		}
	}

	public List<int> TrianglesAroundPointCCW(int point, int startingTriangle, List<int> scratchSpace = null)
	{
		scratchSpace?.Clear();
		List<int> scratchSpaceList = scratchSpace ?? new List<int>(8);
		int startingTriangle2 = startingTriangle;
		do
		{
			scratchSpaceList.Add(startingTriangle2);
			startingTriangle2 = _triangleDefinition[startingTriangle2].GetCounterClockwiseTriangleAroundPoint(point);
		}
		while (startingTriangle2 != startingTriangle && startingTriangle2 != -1);
		if (startingTriangle2 == startingTriangle)
		{
			return scratchSpaceList;
		}
		startingTriangle2 = startingTriangle;
		scratchSpaceList.RemoveAt(0);
		do
		{
			scratchSpaceList.Insert(0, startingTriangle2);
			startingTriangle2 = _triangleDefinition[startingTriangle2].GetClockwiseTriangleAroundPoint(point);
		}
		while (startingTriangle2 != -1);
		return scratchSpaceList;
	}

	public List<int> BoundaryTrianglesAroundPointCCW(int n0, out bool isBoundary, int tri)
	{
		List<int> intList = new List<int>();
		int tri2 = tri;
		int ordinal = -1;
		bool condition = false;
		for (int ordinal2 = 1000; ordinal2 > 0; ordinal2--)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[tri2];
			if (!triangleDefinition.IsDeleted)
			{
				if (condition)
				{
					ordinal = intList.Count;
					condition = false;
				}
				intList.Add(tri2);
			}
			else
			{
				condition = true;
			}
			tri2 = triangleDefinition.GetCounterClockwiseTriangleAroundPoint(n0);
			if (tri2 == tri)
			{
				break;
			}
		}
		isBoundary = (ordinal >= 0) | condition;
		if (ordinal > 0)
		{
			intList.RotateBackward(ordinal);
		}
		return intList;
	}

	public List<Tuple<int, bool>> PointsAroundPointCCW(int ptIdx, int startingTriangle)
	{
		List<int> trianglesAroundPointCCW = TrianglesAroundPointCCW(ptIdx, startingTriangle);
		List<Tuple<int, bool>> valueTupleList = new List<Tuple<int, bool>>(trianglesAroundPointCCW.Count);
		int getClockwisePointWithinTriangleTerm = -1;
		for (int i = 0; i < trianglesAroundPointCCW.Count; i++)
		{
			getClockwisePointWithinTriangleTerm = _triangleDefinition[trianglesAroundPointCCW[i]].GetClockwisePointWithinTriangle(ptIdx);
			bool isDeletedAndIsDeleted = _triangleDefinition[trianglesAroundPointCCW[i]].IsDeleted && _triangleDefinition[trianglesAroundPointCCW[(i + 1) % trianglesAroundPointCCW.Count]].IsDeleted;
			valueTupleList.Add(new Tuple<int, bool>(getClockwisePointWithinTriangleTerm, isDeletedAndIsDeleted));
		}
		int counterClockwisePointWithinTriangle = _triangleDefinition[trianglesAroundPointCCW[0]].GetCounterClockwisePointWithinTriangle(ptIdx);
		if (counterClockwisePointWithinTriangle != getClockwisePointWithinTriangleTerm)
		{
			bool isDeleted = _triangleDefinition[trianglesAroundPointCCW[0]].IsDeleted;
			valueTupleList.Add(new Tuple<int, bool>(counterClockwisePointWithinTriangle, isDeleted));
		}
		return valueTupleList;
	}

	public EdgeDefinition GetEdgeDefinition(int triIdx, int e012)
	{
		TriangleDefinition triangleDefinition = TriangleDefinition[triIdx];
		int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(e012);
		int e012B = -1;
		if (adjacentTriangleIdx != -1)
		{
			e012B = TriangleDefinition[adjacentTriangleIdx].GetEdge012(triIdx);
		}
		int aPointIdx = triangleDefinition.EdgePointA(e012);
		int bPointIdx = triangleDefinition.EdgePointB(e012);
		return new EdgeDefinition(triIdx, e012, adjacentTriangleIdx, e012B, aPointIdx, bPointIdx);
	}

	public EdgeDefinition GetEdgeDefinition(int triIdx, int pA, int pB)
	{
		int edge = TriangleDefinition[triIdx].GetEdge012(pA, pB);
		return GetEdgeDefinition(triIdx, edge);
	}

	public EdgeDefinition TryGetEdgeDefinition(int triIdx, int pA, int pB)
	{
		int edge012 = TriangleDefinition[triIdx].TryGetEdge012(pA, pB);
		if (edge012 == -1)
		{
			return EdgeDefinition.NullEdgeDefinition();
		}
		return GetEdgeDefinition(triIdx, edge012);
	}

	public EdgeDefinition TryGetEdgeDefinition(int pA, int pB, int[] triangleCandidates)
	{
		foreach (int triangleCandidate in triangleCandidates)
		{
			if (triangleCandidate >= 0 && triangleCandidate < TriangleCount)
			{
				EdgeDefinition result = TryGetEdgeDefinition(triangleCandidate, pA, pB);
				if (!result.IsNull())
				{
					return result;
				}
			}
		}
		return EdgeDefinition.NullEdgeDefinition();
	}

	public bool EdgeIsPerimeter(EdgeDefinition edge)
	{
		if (edge.ATriangleIndex == -1 && edge.BTriangleIndex == -1)
		{
			throw new Exception("Invalid edge");
		}
		if (edge.ATriangleIndex == -1)
		{
			return !TriangleDefinition[edge.BTriangleIndex].IsDeleted;
		}
		if (edge.BTriangleIndex == -1)
		{
			return !TriangleDefinition[edge.ATriangleIndex].IsDeleted;
		}
		return TriangleDefinition[edge.ATriangleIndex].IsDeleted != TriangleDefinition[edge.BTriangleIndex].IsDeleted;
	}

	public Segment GetEdgeSegment(EdgeDefinition ed)
	{
		return new Segment(Points[ed.APointIndex], Points[ed.BPointIndex]);
	}

	public Segment GetEdgeSegment(int triIdx, int e012)
	{
		TriangleDefinition triangleDefinition = TriangleDefinition[triIdx];
		int index = triangleDefinition.EdgePointA(e012);
		int index2 = triangleDefinition.EdgePointB(e012);
		return new Segment(Points[index], Points[index2]);
	}

	private EdgeDefinition NextPerimeterEdge(EdgeDefinition perimEdge)
	{
		List<EdgeDefinition> edgesAroundPointCCW = EdgesAroundPointCCW(perimEdge.BPointIndex, perimEdge.ATriangleIndex);
		for (int i = 0; i < edgesAroundPointCCW.Count; i++)
		{
			EdgeDefinition edgeDefinition = edgesAroundPointCCW[i];
			if (!(edgeDefinition == perimEdge) && EdgeIsPerimeter(edgeDefinition))
			{
				return edgeDefinition.Flip();
			}
		}
		throw new ArgumentException("Error finding perimeter edges - is the TIN definition correct?");
	}

	private EdgeDefinition NextExternalEdge(EdgeDefinition externalEdge)
	{
		List<EdgeDefinition> edgesAroundPointCCW = EdgesAroundPointCCW(externalEdge.BPointIndex, externalEdge.ATriangleIndex);
		for (int i = 0; i < edgesAroundPointCCW.Count; i++)
		{
			EdgeDefinition edgeDefinition = edgesAroundPointCCW[i];
			if (!(edgeDefinition == externalEdge) && edgeDefinition.IsExternal)
			{
				return edgeDefinition;
			}
		}
		throw new ArgumentException("External edge not found - TIN error.");
	}

	public void ComputePointElevationsIfNoData(IList<Point> points, double[] z)
	{
		if (points == null || TriangleCount == 0)
		{
			return;
		}
		SpatialIndex<int> triangleSpatialIndex = TriangleSpatialIndex;
		PointCollection subPts = new PointCollection();
		List<int> intList = new List<int>();
		for (int i = 0; i < points.Count; i++)
		{
			if (z[i] == -9999.0)
			{
				subPts.Add(points[i]);
				intList.Add(i);
			}
		}
		if (subPts.Count == 0)
		{
			return;
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(subPts.Extent, SpatialIndex.ReasonableBinCount(subPts.Count));
		spatialIndex.AddRange(subPts);
		for (int j = 0; j < spatialIndex.Bins.Length; j++)
		{
			List<int> bin = spatialIndex.Bins[j];
			if (bin.Count == 0)
			{
				continue;
			}
			PointCollection pts = new PointCollection(bin.Select((int idx) => subPts[idx]));
			List<int> elementsAtPoints = triangleSpatialIndex.GetElementsAtPoints(pts);
			bool[] boolValues = new bool[bin.Count];
			foreach (int elementsAtPoint in elementsAtPoints)
			{
				if (_triangleDefinition[elementsAtPoint].IsDeleted)
				{
					continue;
				}
				for (int intervalIndex = 0; intervalIndex < bin.Count; intervalIndex++)
				{
					if (!boolValues[intervalIndex])
					{
						int index = bin[intervalIndex];
						Extent extent = Triangles[elementsAtPoint].Extent;
						Point pt = subPts[index];
						if (extent.Contains(pt) && Triangle3D[elementsAtPoint].ComputeZ(pt, out var z2))
						{
							z[intList[index]] = z2;
							boolValues[intervalIndex] = true;
						}
					}
				}
			}
		}
	}

	public void ComputeProfiles(IList<Polyline> polylines, StationElevationProfile[] profiles, double tolerance = 0.0)
	{
		if (polylines == null || TriangleCount == 0)
		{
			return;
		}
		SpatialIndex<int> triSpIdx = TriangleSpatialIndex;
		Parallel.SmartFor(polylines.Count, (int start, int fin) =>
		{
			for (int i = start; i <= fin; i++)
			{
				Polyline polyline = polylines[i];
				if (Polyline.IsValidPolyline(polyline) && polyline.MBROverlaps(Extent))
				{
					StationElevationProfile segmentProfile = new StationElevationProfile();
					double segStartStation = 0.0;
					for (int j = 0; j < polyline.SegmentCount; j++)
					{
						Segment segment = polyline.Segments[j];
						if (segment.MBROverlaps(Extent))
						{
							segment.Rasterize(triSpIdx.Raster, (int n) =>
							{
								List<int> bin = triSpIdx.Bins[n];
								if (bin == null)
								{
									return;
								}
								foreach (int bin2 in bin)
								{
									if (!_triangleDefinition[bin2].IsDeleted)
									{
										Triangle3D triangle3D = Triangle3D[bin2];
										if (triangle3D.ComputeZ(segment.A, out var z))
										{
											segmentProfile.Add(segStartStation, z);
										}
										if (triangle3D.ComputeZ(segment.B, out var z2))
										{
											segmentProfile.Add(segStartStation + segment.Length, z2);
										}
										for (int k = 0; k <= 2; k++)
										{
											Segment3D segment3D = Edge3D(bin2, k);
											if (segment.Intersects(segment3D.AsSegment2D(), out var x, out var y, out var _, out var t2))
											{
												double station = segStartStation + segment.A.DistanceTo(x, y);
												double z3 = segment3D.PointAtFraction(t2).Z;
												segmentProfile.Add(station, z3);
											}
										}
									}
								}
							});
						}
						segStartStation += segment.Length;
					}
					if (segmentProfile.Count > 0)
					{
						segmentProfile.SortByStation();
						StationElevationProfile stationElevationProfile = segmentProfile.CloneFiltered(tolerance);
						if (stationElevationProfile.Count > 0)
						{
							if (profiles[i] == null)
							{
								profiles[i] = stationElevationProfile;
							}
							else
							{
								profiles[i].AddRange(stationElevationProfile);
								profiles[i].SortByStation();
							}
						}
					}
				}
			}
		}, 256);
	}

	public void ComputeProfileSubsections(IList<Polyline> polylines, StationElevationProfile[] profiles, List<Tuple<double, double>> subsections, double tolerance = 0.0)
	{
		if (polylines == null || TriangleCount == 0)
		{
			return;
		}
		SpatialIndex<int> triSpIdx = TriangleSpatialIndex;
		Parallel.SmartFor(polylines.Count, (int start, int fin) =>
		{
			for (int i = start; i <= fin; i++)
			{
				Polyline polyline = polylines[i];
				if (Polyline.IsValidPolyline(polyline) && polyline.MBROverlaps(Extent))
				{
					StationElevationProfile segmentProfile = new StationElevationProfile();
					double segStartStation = 0.0;
					for (int j = 0; j < polyline.SegmentCount; j++)
					{
						Segment segment = polyline.Segments[j];
						if (segment.MBROverlaps(Extent))
						{
							segment.Rasterize(triSpIdx.Raster, (int n) =>
							{
								List<int> bin = triSpIdx.Bins[n];
								if (bin == null)
								{
									return;
								}
								foreach (int bin2 in bin)
								{
									if (!_triangleDefinition[bin2].IsDeleted)
									{
										Triangle3D triangle3D = Triangle3D[bin2];
										if (triangle3D.ComputeZ(segment.A, out var z))
										{
											segmentProfile.Add(segStartStation, z);
										}
										if (triangle3D.ComputeZ(segment.B, out var z2))
										{
											segmentProfile.Add(segStartStation + segment.Length, z2);
										}
										for (int k = 0; k <= 2; k++)
										{
											Segment3D segment3D = Edge3D(bin2, k);
											if (segment.Intersects(segment3D.AsSegment2D(), out var x, out var y, out var _, out var t2))
											{
												double station = segStartStation + segment.A.DistanceTo(x, y);
												double z3 = segment3D.PointAtFraction(t2).Z;
												segmentProfile.Add(station, z3);
											}
										}
									}
								}
							});
						}
						segStartStation += segment.Length;
					}
					if (segmentProfile.Count > 0)
					{
						segmentProfile.SortByStation();
						StationElevationProfile stationElevationProfile = segmentProfile.CloneFiltered(tolerance);
						if (stationElevationProfile.Count > 0)
						{
							if (profiles[i] == null)
							{
								profiles[i] = stationElevationProfile;
							}
							else
							{
								profiles[i].AddRange(stationElevationProfile);
								profiles[i].SortByStation();
							}
						}
					}
				}
			}
		}, 256);
	}

	public Point4D SamplePoint(Point pt)
	{
		int t = 0;
		FindTriangleContainingPoint(pt, ref t);
		if (t == -1 || _triangleDefinition[t].IsDeleted)
		{
			return new Point4D(pt, double.NaN, double.NaN);
		}
		Triangle4D[t].ComputeZM(pt, out var Z, out var M);
		return new Point4D(pt, Z, M);
	}

	public void SamplePoints(Memory<Point> points, Memory<float> pointData, Memory<float> outBuffer, float noDataValue = -9999f)
	{
		SamplePoints_BruteForce(points.Span, pointData.Span, outBuffer.Span, noDataValue);
	}

	public void SamplePoints(Memory<Point> points, Memory<double> pointData, Memory<float> outBuffer, float noDataValue = -9999f)
	{
		SamplePoints_BruteForce(points.Span, pointData.Span, outBuffer.Span, noDataValue);
	}

	private void SamplePoints_BruteForce(Span<Point> points, Span<float> pointData, Span<float> outBuffer, float noDataValue = -9999f)
	{
		int t = -1;
		for (int i = 0; i < points.Length; i++)
		{
			Point point = points[i];
			FindTriangleContainingPoint(point, ref t);
			if (t == -1 || TriangleDefinition[t].IsDeleted)
			{
				outBuffer[i] = noDataValue;
				continue;
			}
			TriangleDefinition triangleDefinition = TriangleDefinition[t];
			float pointData2 = pointData[triangleDefinition.p0];
			float pointData3 = pointData[triangleDefinition.p1];
			float pointData4 = pointData[triangleDefinition.p2];
			if (pointData2 == noDataValue || pointData3 == noDataValue || pointData4 == noDataValue)
			{
				outBuffer[i] = noDataValue;
				continue;
			}
			Triangle triangle = Triangles[t];
			outBuffer[i] = triangle.SamplePoint(point, pointData2, pointData3, pointData4);
		}
	}

	private void SamplePoints_BruteForce(Span<Point> points, Span<double> pointData, Span<float> outBuffer, float noDataValue = -9999f)
	{
		int t = -1;
		for (int i = 0; i < points.Length; i++)
		{
			Point point = points[i];
			FindTriangleContainingPoint(point, ref t);
			if (t == -1 || TriangleDefinition[t].IsDeleted)
			{
				outBuffer[i] = noDataValue;
				continue;
			}
			TriangleDefinition triangleDefinition = TriangleDefinition[t];
			double pointData2 = pointData[triangleDefinition.p0];
			double pointData3 = pointData[triangleDefinition.p1];
			double pointData4 = pointData[triangleDefinition.p2];
			if (pointData2 == (double)noDataValue || pointData3 == (double)noDataValue || pointData4 == (double)noDataValue)
			{
				outBuffer[i] = noDataValue;
				continue;
			}
			Triangle triangle = Triangles[t];
			outBuffer[i] = (float)triangle.SamplePoint(point, pointData2, pointData3, pointData4);
		}
	}

	public void Rasterize(RasterDefinition raster, Action<int, int, BarycentricWeights> onPixelHit)
	{
		if (!Extent.Overlaps(raster.Extent))
		{
			return;
		}
		int t;
		for (t = 0; t < TriangleCount; t++)
		{
			if (!TriangleDefinition[t].IsDeleted)
			{
				Triangles[t].Rasterize(raster, (int px, BarycentricWeights wts) =>
				{
					onPixelHit(px, t, wts);
				});
			}
		}
	}

	private void RasterizeByRow(RasterDefinition raster, Action<int, Memory<PointRange>> rowRanges)
	{
		if (!Extent.Overlaps(raster.Extent))
		{
			return;
		}
		if (rowRanges == null)
		{
			throw new ArgumentNullException("rowRanges");
		}
		throw new NotImplementedException();
	}

	public void RasterizeZ(RasterDefinition raster, Memory<float> outBuffer)
	{
		if (!HasZ)
		{
			throw new NotSupportedException("TIN not tracking Z values.");
		}
		Rasterize(raster, GetZBufferUnsafe(), outBuffer);
	}

	public void RasterizeM(RasterDefinition raster, Memory<float> outBuffer)
	{
		if (!HasM)
		{
			throw new NotSupportedException("TIN not tracking M values.");
		}
		Rasterize(raster, GetMBufferUnsafe(), outBuffer);
	}

	public void Rasterize(RasterDefinition raster, Memory<double> pointData, Memory<float> outBuffer)
	{
		if (!Extent.Overlaps(raster.Extent))
		{
			return;
		}
		List<int> elements = TriangleSpatialIndex.GetElements(raster.Extent);
		RasterizePayload<double> payload = new RasterizePayload<double>
		{
			ZMSpan = pointData.Span,
			OutSpan = outBuffer.Span
		};
		for (int i = 0; i < elements.Count; i++)
		{
			int index = elements[i];
			Triangle triangle = Triangles[index];
			payload.TriDef = TriangleDefinition[index];
			if (!payload.TriDef.IsDeleted)
			{
				triangle.Rasterize<RasterizePayload<double>>(raster, payload, (Action<RasterizePayload<double>, int, BarycentricWeights>)((RasterizePayload<double> pl, int px, BarycentricWeights wts) =>
				{
					Span<double> zMSpan = pl.ZMSpan;
					TriangleDefinition triDef = pl.TriDef;
					pl.OutSpan[px] = (float)wts.Interpolate(zMSpan[triDef.p0], zMSpan[triDef.p1], zMSpan[triDef.p2]);
				}));
			}
		}
	}

	public void Rasterize(RasterDefinition raster, Memory<float> pointData, Memory<float> outBuffer)
	{
		if (!Extent.Overlaps(raster.Extent))
		{
			return;
		}
		List<int> elements = TriangleSpatialIndex.GetElements(raster.Extent);
		RasterizePayload<float> payload = new RasterizePayload<float>
		{
			ZMSpan = pointData.Span,
			OutSpan = outBuffer.Span
		};
		for (int i = 0; i < elements.Count; i++)
		{
			int index = elements[i];
			Triangle triangle = Triangles[index];
			payload.TriDef = TriangleDefinition[index];
			if (!payload.TriDef.IsDeleted)
			{
				triangle.Rasterize<RasterizePayload<float>>(raster, payload, (Action<RasterizePayload<float>, int, BarycentricWeights>)((RasterizePayload<float> pl, int px, BarycentricWeights wts) =>
				{
					Span<float> zMSpan = pl.ZMSpan;
					TriangleDefinition triDef = pl.TriDef;
					pl.OutSpan[px] = wts.Interpolate(zMSpan[triDef.p0], zMSpan[triDef.p1], zMSpan[triDef.p2]);
				}));
			}
		}
	}

	public void RasterizePartialPixel(RasterDefinition raster, Action<Pixel> pixelAction)
	{
		if (Extent.Overlaps(raster.Extent))
		{
			for (int i = 0; i < TriangleCount; i++)
			{
				Triangles[i].RasterizePartialPixel(raster, pixelAction);
			}
		}
	}

	public double TriangleContourArea(int t, double maxZ, double minZ, out int ptsCount, Point[] pts)
	{
		ptsCount = 0;
		if (!HasZ)
		{
			throw new NotSupportedException("This TIN doesn't have Z values to contour.");
		}
		Segment segment = default;
		for (int i = 0; i <= 2; i++)
		{
			int index = TriangleDefinition[t].EdgePointA(i);
			int index2 = TriangleDefinition[t].EdgePointB(i);
			if (MathHelpers.ParametricFractions(Z[index], Z[index2], maxZ, minZ, out var t2, out var t3))
			{
				segment.A.X = Points[index].X;
				segment.A.Y = Points[index].Y;
				segment.B.X = Points[index2].X;
				segment.B.Y = Points[index2].Y;
				pts[ptsCount] = segment.PointAtFraction(t2);
				ptsCount++;
				pts[ptsCount] = segment.PointAtFraction(t3);
				ptsCount++;
			}
		}
		double computed = 0.0;
		double scaledMinY = Extent.MinY * 2.0;
		for (int j = 0; j <= ptsCount - 1; j++)
		{
			int ptsCountOffset = ((j != 0) ? (j - 1) : (ptsCount - 1));
			double ptsTerm = pts[j].X - pts[ptsCountOffset].X;
			if (ptsTerm != 0.0)
			{
				computed += (pts[j].Y + pts[ptsCountOffset].Y - scaledMinY) * ptsTerm;
			}
		}
		return Math.Abs(computed * 0.5);
	}

	public PointCollection TriangleContourZ(int t, double minZ, double maxZ)
	{
		if (!HasZ)
		{
			throw new NotSupportedException();
		}
		return TriangleContour(t, minZ, maxZ, (int idx) => _z[idx]);
	}

	public PointCollection TriangleContourM(int t, double minM, double maxM)
	{
		if (!HasM)
		{
			throw new NotSupportedException();
		}
		return TriangleContour(t, minM, maxM, (int idx) => _m[idx]);
	}

	private PointCollection TriangleContour(int t, double minV, double maxV, Func<int, double> mod)
	{
		PointCollection pointCollection = new PointCollection(HasZ, HasM);
		Segment segment = default;
		for (int i = 0; i <= 2; i++)
		{
			int edgePointA = TriangleDefinition[t].EdgePointA(i);
			int edgePointB = TriangleDefinition[t].EdgePointB(i);
			if (MathHelpers.ParametricFractions(mod(edgePointA), mod(edgePointB), maxV, minV, out var t2, out var t3))
			{
				segment.A.X = Points[edgePointA].X;
				segment.A.Y = Points[edgePointA].Y;
				segment.B.X = Points[edgePointB].X;
				segment.B.Y = Points[edgePointB].Y;
				pointCollection.AddNew(segment.PointAtFraction(t2));
				pointCollection.AddNew(segment.PointAtFraction(t3));
			}
		}
		return pointCollection;
	}

	public List<Segment> GetAllSegments()
	{
		return (from ed in GetAllEdgeDefinitions()
			select new Segment(Points[ed.APointIndex], Points[ed.BPointIndex])).ToList();
	}

	public List<EdgeDefinition> GetAllEdgeDefinitions()
	{
		List<EdgeDefinition> edgeDefinitions = new List<EdgeDefinition>();
		for (int i = 0; i < TriangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[i];
			for (int j = 0; j <= 2; j++)
			{
				int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(j);
				triangleDefinition.EdgePoints(j, out var ptA, out var ptB);
				if (i < adjacentTriangleIdx)
				{
					edgeDefinitions.Add(new EdgeDefinition(i, j, adjacentTriangleIdx, TriangleDefinition[adjacentTriangleIdx].GetEdge012(i), ptA, ptB));
				}
				else if (i == -1)
				{
					edgeDefinitions.Add(new EdgeDefinition(i, j, -1, -1, ptA, ptB));
				}
			}
		}
		return edgeDefinitions;
	}

	public List<ComplexPolygon> GetPolygonHull()
	{
		List<EdgeDefinition> allPerimeterEdges = GetAllPerimeterEdges();
		if (allPerimeterEdges.IsNullOrEmpty())
		{
			return new List<ComplexPolygon>();
		}
		Dictionary<int, Tuple<int, int>> valueTupleByInt = new Dictionary<int, Tuple<int, int>>();
		for (int i = 0; i < allPerimeterEdges.Count; i++)
		{
			EdgeDefinition edgeDefinition = allPerimeterEdges[i];
			if (valueTupleByInt.TryGetValue(edgeDefinition.APointIndex, out var localValue))
			{
				if (localValue.Item2 != -1)
				{
					throw new Exception("Bad triangulation in GetPolygonHull");
				}
				localValue = new Tuple<int, int>(localValue.Item1, i);
				valueTupleByInt[edgeDefinition.APointIndex] = localValue;
			}
			else
			{
				localValue = new Tuple<int, int>(i, -1);
				valueTupleByInt[edgeDefinition.APointIndex] = localValue;
			}
			if (valueTupleByInt.TryGetValue(edgeDefinition.BPointIndex, out localValue))
			{
				if (localValue.Item2 != -1)
				{
					throw new Exception("Bad triangulation in GetPolygonHull");
				}
				localValue = new Tuple<int, int>(localValue.Item1, i);
				valueTupleByInt[edgeDefinition.BPointIndex] = localValue;
			}
			else
			{
				localValue = new Tuple<int, int>(i, -1);
				valueTupleByInt[edgeDefinition.BPointIndex] = localValue;
			}
		}
		HashSet<int> intSet = new HashSet<int>();
		List<Polygon> polygons = new List<Polygon>();
		while (intSet.Count != allPerimeterEdges.Count)
		{
			Polygon polygon = new Polygon();
			int ordinal = -1;
			for (int j = 0; j < allPerimeterEdges.Count; j++)
			{
				if (!intSet.Contains(j))
				{
					ordinal = j;
					break;
				}
			}
			if (ordinal == -1)
			{
				throw new Exception("Logic error in GetPolygonHull");
			}
			EdgeDefinition edgeDefinition2 = allPerimeterEdges[ordinal];
			int unity = -1;
			int unity2 = -1;
			if (edgeDefinition2.ATriangleIndex == -1 || TriangleDefinition[edgeDefinition2.ATriangleIndex].IsDeleted)
			{
				unity = edgeDefinition2.BPointIndex;
				unity2 = edgeDefinition2.APointIndex;
			}
			else
			{
				unity = edgeDefinition2.APointIndex;
				unity2 = edgeDefinition2.BPointIndex;
			}
			polygon.Add(Points[unity2]);
			polygon.Add(Points[unity]);
			intSet.Add(ordinal);
			int otherPointAllPerimeterEdgesTerm = unity;
			int ordinal2 = ordinal;
			int ordinal3 = 0;
			while (ordinal3++ < PointCount)
			{
				if (!valueTupleByInt.TryGetValue(otherPointAllPerimeterEdgesTerm, out var localValue2))
				{
					throw new Exception("Triangulation or Logic error in GetPolygonHull");
				}
				if (localValue2.Item1 == ordinal2)
				{
					ordinal2 = localValue2.Item2;
				}
				else
				{
					if (localValue2.Item2 != ordinal2)
					{
						throw new Exception("Triangulation or Logic error in GetPolygonHull");
					}
					ordinal2 = localValue2.Item1;
				}
				otherPointAllPerimeterEdgesTerm = allPerimeterEdges[ordinal2].OtherPoint(otherPointAllPerimeterEdgesTerm);
				intSet.Add(ordinal2);
				if (otherPointAllPerimeterEdgesTerm == unity2)
				{
					break;
				}
				polygon.Add(Points[otherPointAllPerimeterEdgesTerm]);
			}
			if (ordinal3 >= PointCount)
			{
				throw new Exception("Triangulation or Logic error in GetPolygonHull");
			}
			polygons.Add(polygon);
		}
		if (!ComplexPolygon.TryCreateComplexPolygons(polygons, out var complexPolys))
		{
			throw new Exception("Triangulation or Logic error in GetPolygonHull");
		}
		return complexPolys;
	}

	public List<EdgeDefinition> GetAllPerimeterEdges()
	{
		List<EdgeDefinition> edgeDefinitions = new List<EdgeDefinition>();
		for (int i = 0; i < _triangleDefinition.Count; i++)
		{
			for (int j = 0; j <= 2; j++)
			{
				EdgeDefinition edgeDefinition = GetEdgeDefinition(i, j);
				int otherTriangle = edgeDefinition.OtherTriangle(i);
				if ((otherTriangle == -1 || i < otherTriangle) && EdgeIsPerimeter(edgeDefinition))
				{
					edgeDefinitions.Add(edgeDefinition);
				}
			}
		}
		return edgeDefinitions;
	}

	public List<EdgeDefinition> GetAllExternalEdges()
	{
		int unity = -1;
		int e = -1;
		for (int i = 0; i < _triangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[i];
			if (triangleDefinition.at0 == -1)
			{
				unity = i;
				e = 0;
				break;
			}
			if (triangleDefinition.at1 == -1)
			{
				unity = i;
				e = 1;
				break;
			}
			if (triangleDefinition.at2 == -1)
			{
				unity = i;
				e = 2;
				break;
			}
		}
		if (unity == -1)
		{
			throw new Exception("Bad TIN definition - perimeter/external information incorrect or incomplete.");
		}
		List<EdgeDefinition> edgeDefinitions = new List<EdgeDefinition>();
		_ = TriangleDefinition[unity];
		EdgeDefinition edgeDefinition = GetEdgeDefinition(unity, e);
		edgeDefinitions.Add(edgeDefinition);
		EdgeDefinition externalEdge = edgeDefinition;
		while (true)
		{
			EdgeDefinition edgeDefinition2 = NextExternalEdge(externalEdge);
			if (edgeDefinition2 == edgeDefinition)
			{
				break;
			}
			edgeDefinitions.Add(edgeDefinition2);
			externalEdge = edgeDefinition2.Flip();
		}
		return edgeDefinitions;
	}

	public List<EdgeDefinition> FindEdgesExactlyFollowing(Polyline pline)
	{
		return FindEdgesExactlyFollowingInternal(pline);
	}

	public List<EdgeDefinition> FindEdgesExactlyFollowing(Polygon pgon)
	{
		Polyline polyline = new Polyline(pgon.Points);
		polyline.Add(pgon.First());
		return FindEdgesExactlyFollowingInternal(polyline);
	}

	private List<EdgeDefinition> FindEdgesExactlyFollowingInternal(Polyline pline)
	{
		List<EdgeDefinition> edgeDefinitions = new List<EdgeDefinition>();
		Point point = pline.Points[0];
		int t = 0;
		FindTriangleAndNearestPointIndex(point, ref t, out var p);
		if (t == -1)
		{
			return edgeDefinitions;
		}
		int i = 0;
		int ordinal = 0;
		while (ordinal++ < Points.Count)
		{
			Point c;
			for (c = pline.Points[i + 1]; i + 2 < pline.PointCount && c == pline.Points[i + 2]; i++)
			{
			}
			List<EdgeDefinition> edgesAroundPointCCW = EdgesAroundPointCCW(p, t);
			Point a = Points[p];
			double maxValue = double.MaxValue;
			int ordinal2 = -1;
			bool condition = c.X - a.X > 0.0;
			bool condition2 = c.Y - a.Y > 0.0;
			for (int j = 0; j < edgesAroundPointCCW.Count; j++)
			{
				int index = edgesAroundPointCCW[j].OtherPoint(p);
				Point b = Points[index];
				double absoluteCrossProduct = Math.Abs(Vector.CrossProduct(in a, in b, in c));
				if (!(absoluteCrossProduct < maxValue))
				{
					continue;
				}
				double computed = b.X - a.X;
				double computed2 = b.Y - a.Y;
				if (Math.Abs(computed) > Math.Abs(computed2))
				{
					if (computed > 0.0 != condition)
					{
						continue;
					}
				}
				else if (computed2 > 0.0 != condition2)
				{
					continue;
				}
				maxValue = absoluteCrossProduct;
				ordinal2 = j;
			}
			if (ordinal2 == -1)
			{
				throw new Exception("TIN did not sufficiently follow polyline.");
			}
			EdgeDefinition edgesAroundPointCCW2 = edgesAroundPointCCW[ordinal2];
			if (edgesAroundPointCCW2.APointIndex == p)
			{
				edgeDefinitions.Add(edgesAroundPointCCW2);
			}
			else
			{
				edgeDefinitions.Add(edgesAroundPointCCW2.Flip());
			}
			int otherPoint = edgesAroundPointCCW2.OtherPoint(p);
			Point other = Points[otherPoint];
			double distanceTo = a.DistanceTo(other);
			if (other.CloseToXY(c, distanceTo * 0.01))
			{
				i++;
				if (i == pline.Count - 1)
				{
					return edgeDefinitions;
				}
			}
			p = otherPoint;
			t = edgesAroundPointCCW2.EitherDefinedTriangleIndex();
		}
		throw new Exception("Tripped infinite loop breaker.");
	}

	public void Floodfill(int startingTri, Func<EdgeDefinition, bool> canCrossEdge, Action<int> triangleHit)
	{
		int[] triAts = new int[3];
		new FloodFillHelper((int tIdx) =>
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[tIdx];
			triAts[0] = triangleDefinition.at0;
			triAts[1] = triangleDefinition.at1;
			triAts[2] = triangleDefinition.at2;
			return triAts;
		}, (int tFrom, int tTo) =>
		{
			if (tTo == -1)
			{
				return false;
			}
			TriangleDefinition triangleDefinition = TriangleDefinition[tFrom];
			TriangleDefinition triangleDefinition2 = TriangleDefinition[tTo];
			int edge = triangleDefinition.GetEdge012(tTo);
			triangleDefinition.EdgePoints(edge, out var ptA, out var ptB);
			EdgeDefinition edgeDefinition = new EdgeDefinition(tFrom, edge, tTo, triangleDefinition2.GetEdge012(tFrom), ptA, ptB);
			return canCrossEdge(edgeDefinition);
		}).Start(new int[1] { startingTri }, out var hs);
		foreach (int hs2 in hs)
		{
			triangleHit(hs2);
		}
	}

	public int[] PaintUniqueAreas(Func<EdgeDefinition, bool> canCrossEdge, out int uniqueAreaCount)
	{
		int[] retn = null;
		Buffers.ArraySetAll(ref retn, -1, TriangleCount);
		int ct = 0;
		while (true)
		{
			int ordinal = -1;
			for (int i = 0; i < retn.Length; i++)
			{
				if (retn[i] == -1)
				{
					ordinal = i;
					break;
				}
			}
			if (ordinal == -1)
			{
				break;
			}
			Floodfill(ordinal, canCrossEdge, (int idx) =>
			{
				retn[idx] = ct;
			});
			ct++;
		}
		uniqueAreaCount = ct;
		return retn;
	}

	public List<int> FindBadTriangleAreas()
	{
		List<int> badTriangleAreas = new List<int>();
		for (int i = 0; i < TriangleCount; i++)
		{
			if (double.IsNaN(Triangles[i].Area))
			{
				badTriangleAreas.Add(i);
			}
		}
		return badTriangleAreas;
	}

	public bool ContainsAll(IList<Point> points)
	{
		for (int i = 0; i < points.Count; i++)
		{
			if (!ContainsPoint(points[i]))
			{
				return false;
			}
		}
		return true;
	}

	public bool ContainsAll(IReadOnlyList<Point> points)
	{
		for (int i = 0; i < points.Count; i++)
		{
			if (!ContainsPoint(points[i]))
			{
				return false;
			}
		}
		return true;
	}

	public bool ContainsPoint(Point point)
	{
		int t = 0;
		return ContainsPoint(point, ref t);
	}

	public bool ContainsPoint(Point point, ref int t)
	{
		if (!Extent.Contains(point))
		{
			return false;
		}
		FindTriangleContainingPoint(point, ref t);
		if (t == -1)
		{
			return false;
		}
		return !_triangleDefinition[t].IsDeleted;
	}

	public bool ContainsPointBufferedFavorOutOfBounds(Point point, double distanceTolerance, ref int t)
	{
		if (!Extent.Contains(point))
		{
			return false;
		}
		FindTriangleContainingPoint(point, ref t);
		if (t == -1)
		{
			return false;
		}
		TriangleDefinition triangleDefinition = _triangleDefinition[t];
		if (triangleDefinition.IsDeleted)
		{
			return false;
		}
		for (int i = 0; i <= 2; i++)
		{
			int point2 = triangleDefinition.AdjacentPointIdx(i);
			foreach (int trianglesAroundPointCCW in TrianglesAroundPointCCW(point2, t))
			{
				if (!_triangleDefinition[trianglesAroundPointCCW].IsDeleted)
				{
					continue;
				}
				for (int j = 0; j <= 2; j++)
				{
					if (Edge(trianglesAroundPointCCW, j).DistanceToPoint(point) < distanceTolerance)
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	public bool ContainsPointBufferedFavorInBounds(Point point, double distanceTolerance, ref int t)
	{
		if (!Extent.Extend(distanceTolerance).Contains(point))
		{
			return false;
		}
		FindTriangleContainingPoint(point, ref t);
		if (t == -1)
		{
			return false;
		}
		TriangleDefinition triangleDefinition = _triangleDefinition[t];
		if (!triangleDefinition.IsDeleted)
		{
			return true;
		}
		for (int i = 0; i <= 2; i++)
		{
			int point2 = triangleDefinition.AdjacentPointIdx(i);
			foreach (int trianglesAroundPointCCW in TrianglesAroundPointCCW(point2, t))
			{
				if (_triangleDefinition[trianglesAroundPointCCW].IsDeleted)
				{
					continue;
				}
				for (int j = 0; j <= 2; j++)
				{
					if (Edge(trianglesAroundPointCCW, j).DistanceToPoint(point) < distanceTolerance)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	public bool ContainsPolyline(Polyline line)
	{
		throw new NotImplementedException();
	}

	public SpatialCoverage ExtentCoverage(Extent ext)
	{
		if (!Extent.Overlaps(in ext))
		{
			return SpatialCoverage.NoCoverage;
		}
		if (ext.Contains(Extent))
		{
			return SpatialCoverage.PartialCoverage;
		}
		HashSet<int> intSet = new HashSet<int>();
		int t = 0;
		FindTrianglesContainingSegment(ext.TopWall, ref t, out var triangles);
		intSet.AddRange(triangles);
		FindTrianglesContainingSegment(ext.RightWall, ref t, out triangles);
		intSet.AddRange(triangles);
		FindTrianglesContainingSegment(ext.BottomWall, ref t, out triangles);
		intSet.AddRange(triangles);
		FindTrianglesContainingSegment(ext.LeftWall, ref t, out triangles);
		intSet.AddRange(triangles);
		if (intSet.Count == 0)
		{
			throw new Exception("Rare Extent Coverage case.");
		}
		bool isDeletedTriangleDefinitionTerm = intSet.Any((int ordinal) => ordinal != -1 && !_triangleDefinition[ordinal].IsDeleted);
		bool isDeletedTriangleDefinitionTerm2 = intSet.Any((int ordinal2) => ordinal2 == -1 || _triangleDefinition[ordinal2].IsDeleted);
		if (isDeletedTriangleDefinitionTerm & isDeletedTriangleDefinitionTerm2)
		{
			return SpatialCoverage.PartialCoverage;
		}
		new FloodFillHelper((int ordinal3) =>
		{
			if (ordinal3 == -1)
			{
				return new int[0];
			}
			TriangleDefinition triangleDefinition = TriangleDefinition[ordinal3];
			int count = 0;
			if (triangleDefinition.at0 != -1)
			{
				count++;
			}
			if (triangleDefinition.at1 != -1)
			{
				count++;
			}
			if (triangleDefinition.at2 != -1)
			{
				count++;
			}
			int[] intValues = new int[count];
			int ordinal4 = 0;
			if (triangleDefinition.at0 != -1)
			{
				intValues[ordinal4++] = triangleDefinition.at0;
			}
			if (triangleDefinition.at1 != -1)
			{
				intValues[ordinal4++] = triangleDefinition.at1;
			}
			if (triangleDefinition.at2 != -1)
			{
				intValues[ordinal4++] = triangleDefinition.at2;
			}
			return intValues;
		}, (int ordinal5) =>
		{
			if (ordinal5 == -1)
			{
				return false;
			}
			TriangleDefinition triangleDefinition = TriangleDefinition[ordinal5];
			Point pt = Points[triangleDefinition.p0];
			return ext.Contains(pt);
		}).Start(intSet, out var hs);
		isDeletedTriangleDefinitionTerm |= hs.Any((int ordinal6) => ordinal6 != -1 && !_triangleDefinition[ordinal6].IsDeleted);
		isDeletedTriangleDefinitionTerm2 |= hs.Any((int ordinal7) => ordinal7 == -1 || _triangleDefinition[ordinal7].IsDeleted);
		if (isDeletedTriangleDefinitionTerm & isDeletedTriangleDefinitionTerm2)
		{
			return SpatialCoverage.PartialCoverage;
		}
		if (isDeletedTriangleDefinitionTerm)
		{
			return SpatialCoverage.CompleteCoverage;
		}
		if (isDeletedTriangleDefinitionTerm2)
		{
			return SpatialCoverage.NoCoverage;
		}
		throw new Exception();
	}

	private bool ClipTriangle(int t, IExtent extent, ref int nc, ref double[] cx, ref double[] cy)
	{
		Extent extent2 = extent.Extent;
		nc = 0;
		for (int i = 0; i <= 2; i++)
		{
			double x;
			double y;
			double x2;
			double y2;
			switch (i)
			{
			case 0:
				x = Points[TriangleDefinition[t].p0].X;
				y = Points[TriangleDefinition[t].p0].Y;
				x2 = Points[TriangleDefinition[t].p1].X;
				y2 = Points[TriangleDefinition[t].p1].Y;
				break;
			case 1:
				x = Points[TriangleDefinition[t].p1].X;
				y = Points[TriangleDefinition[t].p1].Y;
				x2 = Points[TriangleDefinition[t].p2].X;
				y2 = Points[TriangleDefinition[t].p2].Y;
				break;
			default:
				x = Points[TriangleDefinition[t].p2].X;
				y = Points[TriangleDefinition[t].p2].Y;
				x2 = Points[TriangleDefinition[t].p0].X;
				y2 = Points[TriangleDefinition[t].p0].Y;
				break;
			}
			double x2Term = x2 - x;
			double y2Term = y2 - y;
			double minXExtent2Term;
			double maxXExtent2Term;
			if ((x2Term > 0.0) | ((x2Term == 0.0) & (x > extent2.MaxX)))
			{
				minXExtent2Term = extent2.MinX;
				maxXExtent2Term = extent2.MaxX;
			}
			else
			{
				minXExtent2Term = extent2.MaxX;
				maxXExtent2Term = extent2.MinX;
			}
			double minYExtent2Term;
			double maxYExtent2Term;
			if ((y2Term > 0.0) | ((y2Term == 0.0) & (y > extent2.MaxY)))
			{
				minYExtent2Term = extent2.MinY;
				maxYExtent2Term = extent2.MaxY;
			}
			else
			{
				minYExtent2Term = extent2.MaxY;
				maxYExtent2Term = extent2.MinY;
			}
			double minValue = ((x2Term == 0.0) ? double.MinValue : ((minXExtent2Term - x) / x2Term));
			double minValue2 = ((y2Term == 0.0) ? double.MinValue : ((minYExtent2Term - y) / y2Term));
			double minValue3;
			double minValue4;
			if (minValue < minValue2)
			{
				minValue3 = minValue;
				minValue4 = minValue2;
			}
			else
			{
				minValue3 = minValue2;
				minValue4 = minValue;
			}
			if (!(1.0 >= minValue3))
			{
				continue;
			}
			if (0.0 < minValue3)
			{
				ClipPolygonAdd(minXExtent2Term, minYExtent2Term, ref nc, ref cx, ref cy);
			}
			if (!(1.0 >= minValue4))
			{
				continue;
			}
			double computed;
			if (x2Term != 0.0)
			{
				computed = (maxXExtent2Term - x) / x2Term;
			}
			else
			{
				computed = ((!((extent2.MinX <= x) & (x <= extent2.MaxX))) ? double.MinValue : double.MaxValue);
			}
			double computed2;
			if (y2Term != 0.0)
			{
				computed2 = (maxYExtent2Term - y) / y2Term;
			}
			else
			{
				computed2 = ((!((extent2.MinY <= y) & (y <= extent2.MaxY))) ? double.MinValue : double.MaxValue);
			}
			double computed3 = ((!(computed < computed2)) ? computed2 : computed);
			if (!(0.0 < minValue4) && !(0.0 < computed3))
			{
				continue;
			}
			if (minValue4 <= computed3)
			{
				if (0.0 < minValue4)
				{
					if (minValue > minValue2)
					{
						ClipPolygonAdd(minXExtent2Term, y + minValue * y2Term, ref nc, ref cx, ref cy);
					}
					else
					{
						ClipPolygonAdd(x + minValue2 * x2Term, minYExtent2Term, ref nc, ref cx, ref cy);
					}
				}
				if (1.0 > computed3)
				{
					if (computed < computed2)
					{
						ClipPolygonAdd(maxXExtent2Term, y + computed * y2Term, ref nc, ref cx, ref cy);
					}
					else
					{
						ClipPolygonAdd(x + computed2 * x2Term, maxYExtent2Term, ref nc, ref cx, ref cy);
					}
				}
				else
				{
					ClipPolygonAdd(x2, y2, ref nc, ref cx, ref cy);
				}
			}
			else if (minValue > minValue2)
			{
				ClipPolygonAdd(minXExtent2Term, maxYExtent2Term, ref nc, ref cx, ref cy);
			}
			else
			{
				ClipPolygonAdd(maxXExtent2Term, minYExtent2Term, ref nc, ref cx, ref cy);
			}
		}
		return nc > 0;
	}

	private void ClipPolygonAdd(double x, double y, ref int nc, ref double[] cx, ref double[] cy)
	{
		cx[nc] = x;
		cy[nc] = y;
		nc++;
	}

	private SpatialIndex<int> ComputeTriangleSpatialIndex()
	{
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(Extent, SpatialIndex.ReasonableBinCount(TriangleCount));
		for (int i = 0; i < TriangleDefinition.Count; i++)
		{
			spatialIndex.Add(TriangleExtent(i), i);
		}
		return spatialIndex;
	}

	public Extent TriangleExtent(int t)
	{
		return Triangles[t].Extent;
	}

	public void ComputeSurfaceStatisticsZ()
	{
		throw new NotImplementedException();
	}

	public void ComputeSurfaceStatisticsM()
	{
		throw new NotImplementedException();
	}

	private double ComputeArea()
	{
		double areaSum = 0.0;
		for (int i = 0; i < TriangleDefinition.Count; i++)
		{
			if (!TriangleDefinition[i].IsDeleted)
			{
				areaSum += Triangles[i].Area;
			}
		}
		return areaSum;
	}

	protected override Extent ComputeExtent()
	{
		return base.ComputeExtent();
	}

	private Extent ComputeExtentForNonDeletedTriangles()
	{
		double maxValue = double.MaxValue;
		double minValue = double.MinValue;
		double maxValue2 = double.MaxValue;
		double minValue2 = double.MinValue;
		for (int i = 0; i < TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[i];
			if (!triangleDefinition.IsDeleted)
			{
				Point point = Points[triangleDefinition.p0];
				if (point.X < maxValue)
				{
					maxValue = point.X;
				}
				if (point.X > minValue)
				{
					minValue = point.X;
				}
				if (point.Y < maxValue2)
				{
					maxValue2 = point.Y;
				}
				if (point.Y > minValue2)
				{
					minValue2 = point.Y;
				}
				point = Points[triangleDefinition.p1];
				if (point.X < maxValue)
				{
					maxValue = point.X;
				}
				if (point.X > minValue)
				{
					minValue = point.X;
				}
				if (point.Y < maxValue2)
				{
					maxValue2 = point.Y;
				}
				if (point.Y > minValue2)
				{
					minValue2 = point.Y;
				}
				point = Points[triangleDefinition.p2];
				if (point.X < maxValue)
				{
					maxValue = point.X;
				}
				if (point.X > minValue)
				{
					minValue = point.X;
				}
				if (point.Y < maxValue2)
				{
					maxValue2 = point.Y;
				}
				if (point.Y > minValue2)
				{
					minValue2 = point.Y;
				}
			}
		}
		return new Extent(maxValue, minValue, maxValue2, minValue2);
	}

	public new Tin Clone()
	{
		return new Tin(this, TriangleDefinition, IsCleaned);
	}

	public new static Tin Stream(BinaryReader br)
	{
		Tin tin = new Tin();
		tin.StreamIn(br);
		return tin;
	}

	public override void StreamOut(BinaryWriter bw)
	{
		base.StreamOut(bw);
		int[,] connectivityArray = GetConnectivityArray();
		bw.WriteArray(connectivityArray);
	}

	public override void StreamIn(BinaryReader br)
	{
		Clear();
		base.StreamIn(br);
		br.ReadBuffer<int>(out var arr);
		_triangleDefinition = new List<TriangleDefinition>(arr.GetLength(0));
		for (int i = 0; i < arr.GetLength(0); i++)
		{
			int p = arr[i, 0];
			int p2 = arr[i, 1];
			int p3 = arr[i, 2];
			_triangleDefinition.Add(new TriangleDefinition(p, p2, p3, -1, -1, -1));
		}
		RebuildAdjacencyInformationInternal(_triangleDefinition, PointCount, out var _);
		IsCleaned = true;
	}

	public int[,] GetConnectivityArray()
	{
		int[,] connectivityArray = new int[TriangleCount, 3];
		for (int i = 0; i < TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[i];
			connectivityArray[i, 0] = triangleDefinition.p0;
			connectivityArray[i, 1] = triangleDefinition.p1;
			connectivityArray[i, 2] = triangleDefinition.p2;
		}
		return connectivityArray;
	}

	public Mesh ExportAsMesh()
	{
		throw new NotImplementedException();
	}

	public Tin CloneTranslated(Vector shiftVector)
	{
		return new Tin(Points.Select((Point p) => p.Translate(shiftVector)), TriangleDefinition, isCleaned: false);
	}

	public override int GetDataHash()
	{
		int dataHash = base.GetDataHash();
		for (int i = 0; i < TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[i];
			dataHash = HashCode.Combine(dataHash, triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2, triangleDefinition.at0, triangleDefinition.at1, triangleDefinition.at2);
		}
		return dataHash;
	}

	public bool TinEquals(Tin other)
	{
		if (!EqualsXYZM(other))
		{
			return false;
		}
		if (TriangleCount != other.TriangleCount)
		{
			return false;
		}
		for (int i = 0; i < TriangleCount; i++)
		{
			TriangleDefinition triangleDefinition = TriangleDefinition[i];
			TriangleDefinition triangleDefinition2 = other.TriangleDefinition[i];
			if (triangleDefinition.p0 != triangleDefinition2.p0)
			{
				return false;
			}
			if (triangleDefinition.p1 != triangleDefinition2.p1)
			{
				return false;
			}
			if (triangleDefinition.p2 != triangleDefinition2.p2)
			{
				return false;
			}
			if (triangleDefinition.at0 != triangleDefinition2.at0)
			{
				return false;
			}
			if (triangleDefinition.at1 != triangleDefinition2.at1)
			{
				return false;
			}
			if (triangleDefinition.at2 != triangleDefinition2.at2)
			{
				return false;
			}
		}
		return true;
	}

	public void DebugWriteTin()
	{
		ReflectionHelpers.InvokeStatic("ShapefileIO", "DebugWriteTin", this);
	}

	public void DebugWriteSubsetTin(Extent ext)
	{
		ReflectionHelpers.InvokeStatic("ShapefileIO", "DebugWriteSubsetTin", new object[2] { this, ext });
	}
}

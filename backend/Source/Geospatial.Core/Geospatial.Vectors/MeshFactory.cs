using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Geospatial.DataStructures;
using Geospatial.Vectors.MeshTopology;
using Geospatial.Vectors.Topology;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors;

public class MeshFactory
{
	public static bool EnableTestMagnetizationChanges = true;

	private static readonly ILogger _logger = LogHelper.GetLogger("MeshFactory");

	public bool TrackDiagnostics;

	public MeshDiagnosticInfo DiagnosticInfo;

	public MeshGenerationParams MeshGenParams;

	private double _filterToleranceTIN;

	private MeshError _error = new MeshError();

	private Memory<int>[] CellFaceArrays;

	public MeshError Error => _error;

	private List<FacePoint> FacePoints { get; set; } = new List<FacePoint>();

	private List<Face> Faces { get; set; } = new List<Face>();

	private List<List<Point>> FaceInternalPoints { get; set; } = new List<List<Point>>();

	private Cell[] Cells { get; set; }

	public Point[] CellCenters { get; set; }

	private Polygon Perimeter { get; set; }

	public int CellCount => Cells.Length;

	public int FaceCount => Faces.Count;

	public int FacePointCount => FacePoints.Count;

	private Mesh CompositeMeshInputs()
	{
		return new Mesh(Perimeter, Cells, Faces.ToArray(), FacePoints.ToArray(), CellCenters, ConvertInternalPoints(FaceInternalPoints));
	}

	private static Memory<Point>[] ConvertInternalPoints(List<List<Point>> pts)
	{
		Memory<Point>[] pointSpanArray = new Memory<Point>[pts.Count];
		int sum = pts.Sum((List<Point> lst) => lst?.Count ?? 0);
		if (sum == 0)
		{
			return pointSpanArray;
		}
		Memory<Point> points = new Point[sum];
		for (int intervalIndex = 0; intervalIndex < pts.Count; intervalIndex++)
		{
			List<Point> ptsList = pts[intervalIndex];
			if (ptsList != null && ptsList.Count > 0)
			{
				pointSpanArray[intervalIndex] = points.Slice(0, ptsList.Count);
				Span<Point> pointsSpan = pointSpanArray[intervalIndex].Span;
				for (int intervalIndex2 = 0; intervalIndex2 < ptsList.Count; intervalIndex2++)
				{
					pointsSpan[intervalIndex2] = ptsList[intervalIndex2];
				}
				points = points.Slice(ptsList.Count);
			}
		}
		return pointSpanArray;
	}

	public static bool TryCreateMesh(Polygon perimeter, IList<Point> cellCenters, IList<Polyline> breaklines, out Mesh mesh, out MeshError error, MeshGenerationParams meshParams = null, ProgressReporter reporter = null)
	{
		if (perimeter == null || perimeter.Points.Count < 3)
		{
			mesh = null;
			error = new MeshError
			{
				Status = MeshStatus.FewerThanMinPerimeterPoints,
				StatusMessage = "The perimeter needs 3 or more points"
			};
			return false;
		}
		MeshFactory meshFactory = new MeshFactory();
		if (meshParams == null)
		{
			meshFactory.MeshGenParams = MeshGenerationParams.Default();
		}
		else
		{
			meshFactory.MeshGenParams = meshParams;
		}
		bool meshInternal = meshFactory.CreateMeshInternal(perimeter, cellCenters, breaklines, reporter);
		error = meshFactory.Error;
		if (meshInternal)
		{
			mesh = meshFactory.CompositeMeshInputs();
			return true;
		}
		mesh = null;
		return false;
	}

	public static bool TryCreateMeshWithDiagnostics(Polygon perimeter, IList<Point> cellCenters, IList<Polyline> breaklines, out Mesh mesh, out MeshError error, out MeshDiagnosticInfo diagnostics, MeshGenerationParams meshParams = null, ProgressReporter reporter = null)
	{
		MeshFactory meshFactory = new MeshFactory();
		meshFactory.TrackDiagnostics = true;
		bool meshInternal = meshFactory.CreateMeshInternal(perimeter, cellCenters, breaklines, reporter);
		diagnostics = meshFactory.DiagnosticInfo;
		if (!meshInternal)
		{
			mesh = null;
			error = meshFactory.Error;
			return false;
		}
		mesh = meshFactory.CompositeMeshInputs();
		error = null;
		return true;
	}

	private bool CreateMeshInternal(Polygon perimeter, IList<Point> cellCenters, IList<Polyline> breaklines, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			if (TrackDiagnostics)
			{
				DiagnosticInfo = new MeshDiagnosticInfo();
			}
			reporter.ReportMessage("Validating input data...");
			if (!ValidateInput(perimeter, cellCenters))
			{
				return false;
			}
			Perimeter = GetCCWPerimeter(perimeter);
			_filterToleranceTIN = TinFactory.ComputeNearPointTolerance(Perimeter.Extent);
			double sqrtAbsoluteAreaOverCount = Math.Sqrt(Math.Abs(Perimeter.Area) / (double)cellCenters.Count);
			double minAllowedFaceLength = MeshFactoryOperations.MinAllowedFaceLength();
			if (sqrtAbsoluteAreaOverCount <= minAllowedFaceLength)
			{
				throw new Exception($"Expected face length ({sqrtAbsoluteAreaOverCount}) is beneath the minimum allowable ({minAllowedFaceLength}).");
			}
			if (sqrtAbsoluteAreaOverCount < minAllowedFaceLength * 5.0)
			{
				_logger.LogWarning("Expected face length ({MIN}) very close to minimum allowable ({MIN_ALLOWED}).", sqrtAbsoluteAreaOverCount, minAllowedFaceLength);
			}
			Tin perimeterTin = GetPerimeterTin(Perimeter);
			if (perimeterTin == null)
			{
				throw new Exception("Triangulation error (Perimeter Dev Tin)");
			}
			if (TrackDiagnostics)
			{
				DiagnosticInfo.PerimeterDevTin = perimeterTin;
			}
			if (!ValidatePointsInsidePerim(cellCenters, perimeterTin))
			{
				return false;
			}
			InitInternalStructures(cellCenters.Count);
			reporter.ReportProgress(2);
			Polygon polygon = null;
			Tin tin = null;
			tin = ((polygon == null) ? perimeterTin : GetPerimeterTin(polygon));
			reporter.ReportMessage("Creating the Mesh TIN...");
			ProgressReporter reporter2 = reporter.WithProgressModifier(2, 0.6f);
			if (!GetValidateMeshTIN(cellCenters, out var meshTin, Perimeter.Extent, reporter2))
			{
				return false;
			}
			reporter.ReportProgress(62);
			Cells = new Cell[meshTin.Points.Count - 8];
			CellCenters = new Point[meshTin.Points.Count - 8];
			FacePoints = new List<FacePoint>((int)Math.Pow(2.0, Math.Floor(Math.Log(Cells.Length, 2.0))));
			reporter.ReportMessage("Setting Cell/Face connectivity...");
			SpatialIndex<int> perimeterSpatialIndex = SpatialIndex.OLD_ComputeSegmentSpatialIndex(Perimeter);
			SetCellsAndFaces(meshTin, perimeterTin, tin, perimeterSpatialIndex, reporter2);
			reporter.ReportProgress(89);
			meshTin = null;
			reporter.ReportMessage("Generating Facepoints...");
			if (!SetFacepoints(perimeterSpatialIndex, perimeterTin))
			{
				return false;
			}
			reporter.ReportProgress(95);
			if (MeshGenParams.SplitExternalFaces)
			{
				InternalFacepointInsertions();
			}
			reporter.ReportMessage("Validating Mesh connectivity...");
			if (!ValidateDCSubgraphs())
			{
				return false;
			}
			reporter.ReportMessage("Applying Breaklines...");
			if (!ApplyBreaklines(breaklines))
			{
				return false;
			}
			reporter.ReportProgress(98);
			bool needsAnotherPass = true;
			int ordinal = 0;
			while (needsAnotherPass && ordinal < 8)
			{
				List<int> allCollapsibleFaces = GetAllCollapsibleFaces(ref needsAnotherPass);
				if (allCollapsibleFaces.Count == 0)
				{
					break;
				}
				if (!CollapseFaces(allCollapsibleFaces))
				{
					return false;
				}
				ordinal++;
			}
			reporter.ReportMessage("Validating Facepoints and Cells...");
			if (!ValidateAreaCheck())
			{
				return false;
			}
			if (MeshGenParams.CreateVirtualCells)
			{
				AddVirtualCells();
			}
			if (!ValidateAllFacepointsSet())
			{
				return false;
			}
			if (!ValidateMinFaceLength())
			{
				return false;
			}
			if (!ValidateNumFacesPerCell())
			{
				return false;
			}
			CoalesceCellArrays(Cells);
			reporter.ReportProgress(100);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error generating mesh.");
			throw new NotImplementedException();
		}
		stopwatch.Stop();
		_logger.LogDebug("Created Mesh with {nCells} in {nSecs} seconds.", CellCount, (double)stopwatch.ElapsedMilliseconds / 1000.0);
		return true;
	}

	public static Mesh FromExtent(Extent baseExt, int cellsW, int cellsH, bool generateHexCells = false)
	{
		Polygon perimeter = Polygon.FromExtent(baseExt);
		List<Point> cellCenters = PointGenerator.FromExtent(baseExt, cellsW, cellsH, generateHexCells);
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		if (!TryCreateMesh(perimeter, cellCenters, null, out var mesh, out var _, meshGenerationParams))
		{
			throw new Exception("Error generating mesh - check returned MeshError.");
		}
		return mesh;
	}

	public static Mesh CircularMesh(Extent baseExt, double cellSize)
	{
		double scaledWidth = baseExt.Width / 2.0;
		double cellSize2 = cellSize;
		List<Point> points = new List<Point>();
		List<Point> pointsList = new List<Point>();
		points.Add(new Point(0.0, 0.0));
		int count = (int)Math.Round(scaledWidth / cellSize2 + 0.49999);
		cellSize2 = scaledWidth / ((double)count - 0.5);
		int count2 = 1;
		int count2Term = 1;
		double piOverCount2Sum = 0.0;
		for (int i = 1; i < count; i++)
		{
			double cellSize2Term = cellSize2 * (double)i;
			count2 = (int)Math.Pow(2.0, Math.Ceiling(Math.Log2(Math.PI * 2.0 * cellSize2Term / cellSize2)));
			if (count2 != count2Term)
			{
				piOverCount2Sum += Math.PI / (double)count2;
			}
			for (int j = 0; j < count2; j++)
			{
				double piCount2Term = piOverCount2Sum + Math.PI * 2.0 * (double)j / (double)count2;
				points.Add(new Point(cellSize2Term * Math.Cos(piCount2Term), cellSize2Term * Math.Sin(piCount2Term)));
			}
			count2Term = count2;
		}
		piOverCount2Sum += Math.PI / (double)count2;
		scaledWidth = ((double)count - 0.5) * cellSize2;
		for (int k = 0; k < count2; k++)
		{
			double piCount2Term2 = piOverCount2Sum + Math.PI * 2.0 * (double)k / (double)count2;
			pointsList.Add(new Point(scaledWidth * Math.Cos(piCount2Term2), scaledWidth * Math.Sin(piCount2Term2)));
		}
		Polygon perimeter = new Polygon(pointsList);
		MeshGenerationParams meshGenerationParams = MeshGenerationParams.Default();
		meshGenerationParams.CreateVirtualCells = false;
		TryCreateMesh(perimeter, points, null, out var mesh, out var _, meshGenerationParams);
		return mesh;
	}

	public static int SymmetricRingCount(double radius, double cellSize)
	{
		return 4 * Math.Max(2, (int)Math.Round(Math.PI * 2.0 * radius / cellSize / 4.0));
	}

	public static Mesh GenCircularTriangleMesh(Extent ext, Func<double, double> radialSizeFunc)
	{
		SlidingMesh.DefaultCircumcenterClearance = 0.0;
		Geospatial.Vectors.Topology.Topology topology = MakeCircularTriangleTopology(ext, radialSizeFunc);
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out var finalMesh);
		return finalMesh;
	}

	public static Geospatial.Vectors.Topology.Topology MakeCircularTriangleTopology(Extent ext, Func<double, double> radialSizeFunc)
	{
		double radialSizeFunc22 = ext.Width / 2.0;
		Point center = ext.Center;
		List<double> doubleList = new List<double>();
		double radialSizeFuncSum = 0.0;
		while (doubleList.Count < 99)
		{
			double radialSizeFunc2 = radialSizeFunc(radialSizeFuncSum);
			if (radialSizeFunc2 <= 0.0)
			{
				radialSizeFunc2 = radialSizeFunc22;
			}
			if (radialSizeFunc22 - radialSizeFuncSum <= radialSizeFunc2)
			{
				break;
			}
			radialSizeFuncSum += radialSizeFunc2;
			doubleList.Add(radialSizeFuncSum);
		}
		doubleList.Add(radialSizeFunc22);
		Geospatial.Vectors.Topology.Topology topology = new Geospatial.Vectors.Topology.Topology();
		int[] intValues = new int[doubleList.Count];
		for (int i = 0; i < doubleList.Count; i++)
		{
			double computed = doubleList[i];
			double cellSize = radialSizeFunc(computed);
			int symmetricRingCount = SymmetricRingCount(computed, cellSize);
			topology.AddNode(new Point(center.X + computed, center.Y), out var fid);
			List<Point> points = new List<Point>();
			for (int j = 1; j < symmetricRingCount; j++)
			{
				double piSymmetricRingCountTerm = Math.PI * 2.0 * (double)j / (double)symmetricRingCount;
				points.Add(new Point(center.X + computed * Math.Cos(piSymmetricRingCountTerm), center.Y + computed * Math.Sin(piSymmetricRingCountTerm)));
			}
			topology.AddArc(fid, fid, points, out var fid2);
			intValues[i] = fid2;
		}
		for (int k = 0; k < doubleList.Count; k++)
		{
			double computed2 = ((k == 0) ? 0.0 : doubleList[k - 1]);
			double computed3 = 0.5 * (computed2 + doubleList[k]);
			topology.AddRegionControlPoint(new Point(center.X + computed3, center.Y));
		}
		topology.EnsureMeshMetadataColumns();
		for (int l = 0; l < doubleList.Count; l++)
		{
			double computed4 = ((l == 0) ? 0.0 : doubleList[l - 1]);
			double trianglePaverIds = radialSizeFunc(0.5 * (computed4 + doubleList[l]));
			MeshRegionMetadata md = new MeshRegionMetadata(PatchType.TrianglePaver, SizeFunction.Constant, (float)trianglePaverIds, 0.1f);
			TopologyRecipe.TryParse("# None", out md.RegionRecipe, false, null);
			topology.SetControlPointData(l, md);
		}
		for (int m = 0; m < doubleList.Count; m++)
		{
			topology.SetArcData(intValues[m], LinearSizeFunction.Constant, (float)radialSizeFunc(doubleList[m]), 100, 1f);
		}
		return topology;
	}

	private bool GetValidateMeshTIN(IList<Point> cellCenters, out Tin meshTin, Extent MBR, ProgressReporter reporter)
	{
		List<int> duplicatePoints = new List<int>();
		MBR = MeshFactoryOperations.RatioMBR(cellCenters, MBR);
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory(new TriangulationParameters
		{
			Tolerance = _filterToleranceTIN,
			EnforcedMBR = MBR
		})
		{
			MarkDuplicatePoints = (int pt) =>
			{
				duplicatePoints.Add(pt);
			}
		};
		delaunayTinFactory.AddMassPoints(cellCenters);
		meshTin = delaunayTinFactory.ComputeTIN(reporter);
		if (TrackDiagnostics)
		{
			DiagnosticInfo.MeshTin = meshTin;
		}
		return ValidateDuplicatePoints(meshTin, cellCenters, duplicatePoints);
	}

	private void SetCellsAndFaces(Tin meshTIN, Tin perimeterDevTIN, Tin bufferedPerimeterDevTIN, SpatialIndex<int> perimeterSpatialIndex, ProgressReporter reporter)
	{
		Func<Segment, bool> func = (Segment segment2) =>
		{
			foreach (int element in perimeterSpatialIndex.GetElements(segment2))
			{
				if (Perimeter.Segments[element].Intersects(segment2))
				{
					return true;
				}
			}
			return false;
		};
		for (int intervalIndex = 0; intervalIndex < meshTIN.Triangles.Count; intervalIndex++)
		{
			if (intervalIndex % 1000 == 0)
			{
				reporter.ReportProgress(100 * (int)((double)intervalIndex / (double)meshTIN.Triangles.Count));
			}
			TriangleDefinition triangleDefinition = meshTIN.TriangleDefinition[intervalIndex];
			int[] intValues = new int[3] { triangleDefinition.p0, triangleDefinition.p1, triangleDefinition.p2 };
			for (int intervalIndex2 = 0; intervalIndex2 <= 2; intervalIndex2++)
			{
				int cellA = intValues[intervalIndex2];
				cellA -= 8;
				if (cellA < 0 || Cells[cellA].IsInitialized)
				{
					continue;
				}
				List<Tuple<int, int, int>> triEdgeTriCCW = meshTIN.TriEdgeTriCCW(cellA + 8, intervalIndex);
				Point point = meshTIN.Points[cellA + 8];
				Cell c = new Cell();
				for (int intervalIndex3 = 0; intervalIndex3 < triEdgeTriCCW.Count; intervalIndex3++)
				{
					int triEdgeTriCCWTerm = triEdgeTriCCW[intervalIndex3].Item1;
					int cellB = triEdgeTriCCW[intervalIndex3].Item2 - 8;
					int triEdgeTriCCWTerm2 = triEdgeTriCCW[intervalIndex3].Item3;
					bool condition = false;
					bool condition2 = false;
					if (cellB < 0)
					{
						continue;
					}
					Cell cell = Cells[cellB];
					if (cell.IsInitialized)
					{
						Span<int> faces = cell.Faces.Span;
						for (int intervalIndex4 = 0; intervalIndex4 < faces.Length; intervalIndex4++)
						{
							int faceIndex = faces[intervalIndex4];
							if (Faces[faceIndex].IsEitherCell(cellA))
							{
								ADDFACETOCELL(cellA, ref c, faceIndex);
								int index = cell.NextCWFaceByGlobalIndex(faceIndex);
								if (Faces[index].OtherCell(cellB) == -1)
								{
									Face f = new Face();
									f.cellA = cellA;
									ADDFACE(f);
									ADDFACETOCELL(cellA, ref c, Faces.Count - 1);
								}
								break;
							}
						}
						continue;
					}
					Face face = new Face();
					face.cellA = cellA;
					face.cellB = cellB;
					Face f2 = face;
					Segment segment = new Segment(meshTIN.Points[cellA + 8], meshTIN.Points[cellB + 8]);
					Point point2 = meshTIN.Triangles[triEdgeTriCCWTerm].CircumCircleCenter();
					Point point3 = meshTIN.Triangles[triEdgeTriCCWTerm2].CircumCircleCenter();
					if (!bufferedPerimeterDevTIN.ContainsPoint(point2) || meshTIN.TriangleDefinition[triEdgeTriCCWTerm].IsDeleted)
					{
						condition = true;
					}
					if (!bufferedPerimeterDevTIN.ContainsPoint(point3) || meshTIN.TriangleDefinition[triEdgeTriCCWTerm2].IsDeleted)
					{
						condition2 = true;
					}
					double scaledLength = segment.Length * 0.05;
					if (point2.DistanceTo(point3) < scaledLength && !condition && !condition2)
					{
						continue;
					}
					Segment segment3 = new Segment(point2, point3);
					if (!(condition & condition2) || func(segment3))
					{
						ADDFACE(f2);
						ADDFACETOCELL(cellA, ref c, Faces.Count - 1);
						if (condition2)
						{
							Face f3 = new Face();
							f3.cellA = cellA;
							ADDFACE(f3);
							ADDFACETOCELL(cellA, ref c, Faces.Count - 1);
						}
					}
				}
				COMPLETECELLFACES(c);
				Cells[cellA] = c;
				CellCenters[cellA] = point;
			}
		}
	}

	private bool SetFacepoints(SpatialIndex<int> perimeterSpatialIndex, Tin perimDevTin)
	{
		Dictionary<int, PointOnPolyline> pointOnPolylineByInt = new Dictionary<int, PointOnPolyline>();
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		double minAllowedFaceLength = MeshFactoryOperations.MinAllowedFaceLength();
		double scaledLength = Perimeter.Length * 0.6;
		bool condition = false;
		int ordinal = 0;
		while (true)
		{
			if (ordinal < Cells.Length)
			{
				Cell cell = Cells[ordinal];
				Span<int> faces = cell.Faces.Span;
				for (int i = 0; i < faces.Length; i++)
				{
					if (Faces[faces[i]].GetFPPrev(ordinal) != -1)
					{
						continue;
					}
					Point pt = ComputeFPPrev(ordinal, i, Perimeter, out var ptOnPoly, out var isPoP, intListList);
					if (pt.IsUndefined())
					{
						continue;
					}
					FacePoint pt2 = new FacePoint(pt);
					List<int> intList2 = (List<int>)(pt2.Faces = new List<int>());
					FacePoints.Add(pt2);
					int countOffset = FacePoints.Count - 1;
					int ordinal2 = ordinal;
					Cell cell2 = Cells[ordinal];
					int faceIndex = faces[i];
					Face face2 = Faces[faceIndex];
					face2.SetFPPrev(ordinal2, countOffset);
					Faces[faceIndex] = face2;
					intList2.Add(faceIndex);
					bool condition2 = false;
					while (face2.OtherCell(ordinal2) != -1)
					{
						ordinal2 = face2.OtherCell(ordinal2);
						cell2 = Cells[ordinal2];
						Faces[faceIndex] = face2;
						faceIndex = cell2.NextCCWFaceByGlobalIndex(faceIndex);
						face2 = Faces[faceIndex];
						int fPPrev = face2.GetFPPrev(ordinal2);
						if (fPPrev != countOffset)
						{
							if (fPPrev != -1)
							{
								throw new Exception("Something went wrong here.");
							}
							face2.SetFPPrev(ordinal2, countOffset);
							AddBeginning(intList2, faceIndex);
							Faces[faceIndex] = face2;
							continue;
						}
						goto IL_02c8;
					}
					if (!isPoP)
					{
						intListList.Add(ordinal);
					}
					if (pointOnPolylineByInt.ContainsKey(faceIndex))
					{
						PointOnPolyline endPoint = pointOnPolylineByInt[faceIndex];
						if (ptOnPoly.IsNull() || endPoint.IsNull())
						{
							goto IL_018a;
						}
						Polyline polyline = Perimeter.PointsBetweenExclusive(ptOnPoly, endPoint);
						pointOnPolylineByInt.Remove(faceIndex);
						FaceInternalPoints[faceIndex] = polyline.Points.ToList();
						double faceSimpleLength = FaceSimpleLength(faceIndex);
						if (faceSimpleLength < minAllowedFaceLength)
						{
							intList.Add(faceIndex);
						}
						else if (faceSimpleLength > scaledLength)
						{
							intList.Add(faceIndex);
						}
						face2.SetFPPrev(ordinal2, countOffset);
					}
					else
					{
						pointOnPolylineByInt.Add(faceIndex, ptOnPoly);
						face2.SetFPPrev(ordinal2, countOffset);
					}
					condition2 = true;
					if (!intList2.Contains(faceIndex))
					{
						AddBeginning(intList2, faceIndex);
					}
					Faces[faceIndex] = face2;
					goto IL_02c8;
					IL_02c8:
					if (!condition2)
					{
						continue;
					}
					ordinal2 = ordinal;
					cell2 = Cells[ordinal];
					faceIndex = cell.NextCWFaceByLocalIndex(i);
					face2 = Faces[faceIndex];
					face2.SetFPNext(ordinal2, countOffset);
					intList2.Add(faceIndex);
					Faces[faceIndex] = face2;
					while (face2.OtherCell(ordinal2) != -1)
					{
						ordinal2 = face2.OtherCell(ordinal2);
						cell2 = Cells[ordinal2];
						faceIndex = cell2.NextCWFaceByGlobalIndex(faceIndex);
						face2 = Faces[faceIndex];
						if (face2.GetFPNext(ordinal2) != countOffset)
						{
							if (face2.GetFPNext(ordinal2) != -1)
							{
								throw new Exception("Something went wrong here.");
							}
							face2.SetFPNext(ordinal2, countOffset);
							Faces[faceIndex] = face2;
							intList2.Add(faceIndex);
							continue;
						}
						goto IL_0490;
					}
					if (!isPoP)
					{
						intListList.Add(ordinal);
					}
					if (pointOnPolylineByInt.ContainsKey(faceIndex))
					{
						PointOnPolyline startPoint = pointOnPolylineByInt[faceIndex];
						if (ptOnPoly.IsNull() || startPoint.IsNull())
						{
							goto IL_0363;
						}
						Polyline polyline2 = Perimeter.PointsBetweenExclusive(startPoint, ptOnPoly);
						pointOnPolylineByInt.Remove(faceIndex);
						FaceInternalPoints[faceIndex] = polyline2.Points.ToList();
						double faceSimpleLength2 = FaceSimpleLength(faceIndex);
						if (faceSimpleLength2 < minAllowedFaceLength)
						{
							intList.Add(faceIndex);
						}
						else if (faceSimpleLength2 > scaledLength)
						{
							intList.Add(faceIndex);
						}
						face2.SetFPNext(ordinal2, countOffset);
					}
					else
					{
						pointOnPolylineByInt.Add(faceIndex, ptOnPoly);
						face2.SetFPNext(ordinal2, countOffset);
					}
					if (!intList2.Contains(faceIndex))
					{
						intList2.Add(faceIndex);
					}
					Faces[faceIndex] = face2;
					IL_0490:;
				}
				ordinal++;
				continue;
			}
			for (int j = 0; j < FacePointCount; j++)
			{
				if (!FacePointIsPerimeter(j))
				{
					continue;
				}
				FacePoint facePoint = FacePoints[j];
				if (facePoint.Faces.Count == 3)
				{
					continue;
				}
				foreach (int face in facePoint.Faces)
				{
					intListList.Add(Faces[face].cellA);
					intListList.Add(Faces[face].cellB);
				}
			}
			if (intListList.Count == 0 && !CollapseFaces(intList))
			{
				return false;
			}
			Func<Segment, bool> func = (Segment segment2) =>
			{
				double length = segment2.Length;
				double tB = (length - _filterToleranceTIN) / length;
				segment2 = segment2.Scale(_filterToleranceTIN / length, tB);
				foreach (int element in perimeterSpatialIndex.GetElements(segment2))
				{
					if (Perimeter.Segments[element].Intersects(segment2))
					{
						return true;
					}
				}
				return false;
			};
			for (int intervalIndex = 0; intervalIndex < Faces.Count; intervalIndex++)
			{
				if (FaceIsPerimeter(intervalIndex))
				{
					continue;
				}
				Segment segment = FaceSegment(intervalIndex);
				if (!func(segment))
				{
					continue;
				}
				List<LineIntersection> lineIntersections = new List<LineIntersection>();
				foreach (LineIntersection allIntersection in Perimeter.GetAllIntersections(segment))
				{
					if (allIntersection.Point != segment.A && allIntersection.Point != segment.B)
					{
						lineIntersections.Add(allIntersection);
					}
				}
				if (lineIntersections.Count == 0 || lineIntersections.Count == 1)
				{
					continue;
				}
				int getTravelDirectionPerimeterTerm;
				if (lineIntersections.Count > 2)
				{
					getTravelDirectionPerimeterTerm = Perimeter.GetTravelDirection(lineIntersections[0].AIntersection, lineIntersections[1].AIntersection, lineIntersections[2].AIntersection);
				}
				else
				{
					if (lineIntersections.Count != 2)
					{
						continue;
					}
					getTravelDirectionPerimeterTerm = Perimeter.GetTravelDirectionShortestArc(lineIntersections[0].AIntersection, lineIntersections[1].AIntersection);
				}
				List<Point> points = new List<Point>();
				for (int intervalIndex2 = 0; intervalIndex2 < lineIntersections.Count - 1; intervalIndex2++)
				{
					LineIntersection lineIntersection = lineIntersections[intervalIndex2];
					LineIntersection lineIntersection2 = lineIntersections[intervalIndex2 + 1];
					Point point = new Segment(lineIntersection.Point, lineIntersection2.Point).Midpoint();
					if (perimDevTin.ContainsPoint(point))
					{
						continue;
					}
					points.Add(lineIntersection.Point);
					int segmentIndex = lineIntersection.AIntersection.SegmentIndex;
					int count = lineIntersection2.AIntersection.SegmentIndex - getTravelDirectionPerimeterTerm;
					for (int segmentIndex2 = segmentIndex; segmentIndex2 <= count; segmentIndex2 += getTravelDirectionPerimeterTerm)
					{
						if (getTravelDirectionPerimeterTerm == 1)
						{
							points.Add(Perimeter.Segments[segmentIndex2].B);
						}
						else
						{
							points.Add(Perimeter.Segments[segmentIndex2].A);
						}
					}
					points.Add(lineIntersection2.Point);
					_ = Faces[intervalIndex];
					List<Point> faceInternalPoint = FaceInternalPoints[intervalIndex];
					if (faceInternalPoint == null)
					{
						FaceInternalPoints[intervalIndex] = points.ToList();
					}
					else
					{
						faceInternalPoint.AddRange(points);
					}
				}
			}
			for (int intervalIndex3 = 0; intervalIndex3 < Cells.Length; intervalIndex3++)
			{
				Cell cell3 = Cells[intervalIndex3];
				Span<int> facesSpan = cell3.Faces.Span;
				HashSet<Point> pointsSet = null;
				for (int intervalIndex4 = 0; intervalIndex4 < facesSpan.Length; intervalIndex4++)
				{
					int index = facesSpan[intervalIndex4];
					_ = Faces[index];
					List<Point> faceInternalPointList = FaceInternalPoints[index];
					if (faceInternalPointList.IsNullOrEmpty())
					{
						continue;
					}
					if (pointsSet == null)
					{
						pointsSet = new HashSet<Point>();
					}
					bool condition3 = false;
					for (int intervalIndex5 = 0; intervalIndex5 < faceInternalPointList.Count; intervalIndex5++)
					{
						Point faceInternalPoint2 = faceInternalPointList[intervalIndex5];
						if (pointsSet.Contains(faceInternalPoint2))
						{
							condition3 = true;
							break;
						}
						pointsSet.Add(faceInternalPoint2);
					}
					if (condition3)
					{
						intListList.Add(intervalIndex3);
						break;
					}
				}
			}
			break;
			IL_018a:
			condition = true;
			break;
			IL_0363:
			condition = true;
			break;
		}
		if (condition)
		{
			_error.HasFatalPerimeterError = true;
			_logger.LogWarning("Fatal perimeter error generating mesh.");
		}
		return ValidatePerimeterErrors(intListList);
	}

	private void InternalFacepointInsertions()
	{
		for (int i = 0; i < Cells.Length && !CellIsVirtual(i); i++)
		{
			if (CellIsPerimeter(i))
			{
				double cellTrueArea = CellPolygonArea(i);
				while (SplitExternalCellMaintainIndexing(i, cellTrueArea))
				{
				}
			}
		}
	}

	private bool SplitExternalCellMaintainIndexing(int cIdx, double cellTrueArea)
	{
		List<int> cellFacePoints = CellFacePoints(cIdx);
		if (cellFacePoints.Count >= 8)
		{
			return false;
		}
		List<Point> points = cellFacePoints.Select((int fpIdx) => FacePoints[fpIdx].Point).ToList();
		double area = new Polygon(points).Area;
		if (area < 0.0)
		{
			area *= -1.0;
		}
		if (area == 0.0)
		{
			area = double.Epsilon;
		}
		if (cellTrueArea / area < 1.2)
		{
			return false;
		}
		Cell cell = Cells[cIdx];
		int faceIndexTerm = -1;
		int faceInternalPtIdx = -1;
		double minValue = double.MinValue;
		Span<int> faces = cell.Faces.Span;
		for (int intervalIndex = 0; intervalIndex < faces.Length; intervalIndex++)
		{
			int faceIndex = faces[intervalIndex];
			if (!FaceIsPerimeter(faceIndex))
			{
				continue;
			}
			Face face = Faces[faceIndex];
			List<Point> faceInternalPoint = FaceInternalPoints[faceIndex];
			if (faceInternalPoint.IsNullOrEmpty())
			{
				continue;
			}
			int index = cellFacePoints.IndexOf(face.GetFPNext(cIdx));
			points.Insert(index, default);
			for (int faceInternalPtIdx2 = 0; faceInternalPtIdx2 < faceInternalPoint.Count; faceInternalPtIdx2++)
			{
				points[index] = faceInternalPoint[faceInternalPtIdx2];
				double absoluteArea = Math.Abs(new Polygon(points).Area);
				if (absoluteArea > minValue)
				{
					faceIndexTerm = faceIndex;
					faceInternalPtIdx = faceInternalPtIdx2;
					minValue = absoluteArea;
				}
			}
		}
		if (1.2 > minValue / area)
		{
			return false;
		}
		if (faceIndexTerm != -1)
		{
			InsertBoundaryFacePointaintainIndexing(faceIndexTerm, cIdx, faceInternalPtIdx);
			return true;
		}
		return false;
	}

	private void InsertBoundaryFacePointaintainIndexing(int fIdx, int cIdx, int faceInternalPtIdx)
	{
		Face face = Faces[fIdx];
		Cell c = Cells[cIdx];
		List<Point> faceInternalPoint = FaceInternalPoints[fIdx];
		Face f = new Face(face.cellA, face.cellB);
		f.SetFPPrev(cIdx, FacePoints.Count);
		int fPNext = face.GetFPNext(cIdx);
		f.SetFPNext(cIdx, fPNext);
		FacePoint facePoint = FacePoints[fPNext];
		int index = facePoint.Faces.IndexOf(fIdx);
		facePoint.Faces[index] = Faces.Count;
		ADDFACE(f);
		if (faceInternalPtIdx < faceInternalPoint.Count - 1)
		{
			List<List<Point>> faceInternalPoints = FaceInternalPoints;
			faceInternalPoints[faceInternalPoints.Count - 1] = faceInternalPoint.GetRange(faceInternalPtIdx + 1, faceInternalPoint.Count - faceInternalPtIdx - 1);
		}
		Point pt = faceInternalPoint[faceInternalPtIdx];
		FacePoint pt2 = new FacePoint(pt);
		pt2.Faces = new int[2]
		{
			Faces.Count - 1,
			fIdx
		};
		FacePoints.Add(pt2);
		face.SetFPNext(cIdx, FacePoints.Count - 1);
		if (faceInternalPtIdx > 0)
		{
			FaceInternalPoints[fIdx] = faceInternalPoint.GetRange(0, faceInternalPtIdx);
		}
		else
		{
			FaceInternalPoints[fIdx] = null;
		}
		Faces[fIdx] = face;
		INSERTFACEAFTER(cIdx, ref c, fIdx, Faces.Count - 1);
		Cells[cIdx] = c;
	}

	private void AddVirtualCells()
	{
		List<Cell> cellsList = new List<Cell>();
		List<Point> points = new List<Point>();
		for (int i = 0; i < Faces.Count; i++)
		{
			Face face = Faces[i];
			if (face.cellB == -1)
			{
				List<Point> faceInternalPoint = FaceInternalPoints[i];
				Point point;
				if (faceInternalPoint == null || faceInternalPoint.Count == 0)
				{
					point = FacePoints[face.fpA].Point.Midpoint(FacePoints[face.fpB].Point);
				}
				else
				{
					Polyline polyline = new Polyline();
					polyline.Add(FacePoints[face.fpA].Point);
					polyline.AddRange(faceInternalPoint);
					polyline.Add(FacePoints[face.fpB].Point);
					point = polyline.PointAtFraction(0.5).Point;
				}
				Cell cell = new Cell();
				cell.Faces = new int[1] { i };
				cellsList.Add(cell);
				points.Add(point);
			}
		}
		int cellCount = CellCount;
		Cell[] cells = Cells;
		Cells = new Cell[cellCount + cellsList.Count];
		Point[] cellCenters = CellCenters;
		CellCenters = new Point[cellCount + cellsList.Count];
		if (cells != null)
		{
			Array.Copy(cells, Cells, Math.Min(cellCount + cellsList.Count, cells.Length));
			Array.Copy(cellCenters, CellCenters, Math.Min(cellCount + cellsList.Count, cells.Length));
		}
		for (int j = cellCount; j < Cells.Length; j++)
		{
			Cells[j] = cellsList[j - cellCount];
			CellCenters[j] = points[j - cellCount];
			Face face2 = Faces[Cells[j].Faces.Span[0]];
			face2.cellB = j;
			Faces[Cells[j].Faces.Span[0]] = face2;
		}
	}

	private void AddBeginning(List<int> intList, int newElement)
	{
		List<int> intListList = new List<int>();
		intListList.AddRange(intList);
		intList.Clear();
		intList.Add(newElement);
		intList.AddRange(intListList);
	}

	private Point ComputeFPPrev(int cellIdx, int localFaceIdx, Polygon ccwPerimeter, out PointOnPolyline ptOnPoly, out bool isPoP, List<int> badPoints)
	{
		Cell cell = Cells[cellIdx];
		Span<int> faces = cell.Faces.Span;
		int otherCell = Faces[faces[localFaceIdx]].OtherCell(cellIdx);
		int index = cell.NextCWFaceByLocalIndex(localFaceIdx);
		int otherCell2 = Faces[index].OtherCell(cellIdx);
		double snapTolerance = 1E-10;
		ptOnPoly = PointOnPolyline.NullPointOnPolyline;
		if (otherCell == -1)
		{
			if (otherCell2 == -1)
			{
				throw new NotImplementedException();
			}
			_ = ref Cells[otherCell2];
			Segment seg = CellToCellSegment(cellIdx, otherCell2);
			ptOnPoly = Project(seg, ccwPerimeter, useRHR: true, snapTolerance);
			isPoP = true;
			return new Point(ptOnPoly.Point);
		}
		_ = ref Cells[otherCell];
		if (otherCell2 == -1)
		{
			Segment seg2 = CellToCellSegment(cellIdx, otherCell);
			ptOnPoly = Project(seg2, ccwPerimeter, useRHR: false, snapTolerance);
			isPoP = true;
			return new Point(ptOnPoly.Point);
		}
		if (otherCell == otherCell2)
		{
			isPoP = false;
			badPoints.Add(otherCell);
			return Point.NullPoint;
		}
		isPoP = false;
		Point a = CellCenters[cellIdx];
		Point b = CellCenters[otherCell];
		Point c = CellCenters[otherCell2];
		return Circle.ComputeCenter(a, b, c);
	}

	private PointOnPolyline Project(Segment seg, Polygon ccwperimeter, bool useRHR, double snapTolerance)
	{
		PointOnPolyline result = ccwperimeter.NearestPointOnPolyline(vec: (!useRHR) ? seg.PerpendicularUnitVector().Negate() : seg.PerpendicularUnitVector(), pt: seg.Midpoint());
		if (result.Fraction < snapTolerance)
		{
			result.Fraction = 0.0;
			result.Point = ccwperimeter.Points[result.SegmentIndex];
		}
		else if (result.Fraction > 1.0 - snapTolerance)
		{
			result.Fraction = 1.0;
			result.Point = ccwperimeter.Segments[result.SegmentIndex].B;
		}
		return result;
	}

	private void PopulateCellsWithInputPoints(IList<Point> cellCenters)
	{
		new Cell();
		Cells = new Cell[cellCenters.Count];
		CellCenters = cellCenters.ToArray();
	}

	private List<int> GetPointsOutsidePerimeter(IList<Point> points, Tin perimeterTIN)
	{
		List<int> pointsOutsidePerimeter = new List<int>();
		int t = 0;
		for (int i = 0; i < points.Count; i++)
		{
			if (!perimeterTIN.ContainsPointBufferedFavorOutOfBounds(points[i], _filterToleranceTIN, ref t))
			{
				pointsOutsidePerimeter.Add(i);
			}
		}
		return pointsOutsidePerimeter;
	}

	private Tin GetPerimeterTin(Polygon perimeter)
	{
		double tolerance = 1E-06;
		DelaunayTinFactory delaunayTinFactory = new DelaunayTinFactory(new TriangulationParameters
		{
			Tolerance = tolerance
		});
		delaunayTinFactory.AddPerimeter(perimeter);
		return delaunayTinFactory.ComputeTIN();
	}

	private Polygon GetCCWPerimeter(Polygon perimeter)
	{
		if (perimeter.Area > 0.0)
		{
			return perimeter.CloneReversed();
		}
		return perimeter.Clone();
	}

	private void InitInternalStructures(int numCells)
	{
		CellFaceArrays = new Memory<int>[numCells];
	}

	private void ADDFACE(Face f)
	{
		Faces.Add(f);
		FaceInternalPoints.Add(null);
	}

	private void ADDFACETOCELL(int ID, ref Cell c, int newFaceIdx)
	{
		ADDFACESPOTINCELL(ID, ref c);
		Span<int> faces = c.Faces.Span;
		faces[faces.Length - 1] = newFaceIdx;
	}

	private void ADDFACESPOTINCELL(int ID, ref Cell c)
	{
		Memory<int> cellFaceArray = CellFaceArrays[ID];
		int length = c.Faces.Length;
		if (length >= cellFaceArray.Length)
		{
			if (cellFaceArray.IsEmpty)
			{
				cellFaceArray = new int[4];
			}
			else
			{
				int[] cellFaceArrayArray = new int[cellFaceArray.Length + 2];
				cellFaceArray.CopyTo(cellFaceArrayArray);
				cellFaceArray = cellFaceArrayArray;
			}
			CellFaceArrays[ID] = cellFaceArray;
		}
		c.Faces = cellFaceArray.Slice(0, length + 1);
	}

	private void COMPLETECELLFACES(Cell c)
	{
	}

	private void ASSIGNFACE(Cell c, int index, int newFaceIdx)
	{
		c.Faces.Span[index] = newFaceIdx;
	}

	private void REMOVEATFACE(Cell c, int indexToRemove)
	{
		throw new NotImplementedException();
	}

	private void INSERTFACEAFTER(int ID, ref Cell c, int afterThisGlobalFIdx, int faceToInsert)
	{
		ADDFACESPOTINCELL(ID, ref c);
		Span<int> faces = c.Faces.Span;
		int indexOf = ((ReadOnlySpan<int>)faces).IndexOf(afterThisGlobalFIdx);
		if (indexOf == faces.Length - 2)
		{
			faces[faces.Length - 1] = faceToInsert;
			return;
		}
		faces.Slice(indexOf + 1, faces.Length - indexOf - 2).CopyTo(faces.Slice(indexOf + 2));
		faces[indexOf + 1] = faceToInsert;
	}

	private void REMOVEFACE(int ID, ref Cell c, int faceToRemove)
	{
		Span<int> faces = c.Faces.Span;
		int length = faces.Length;
		for (int i = ((ReadOnlySpan<int>)faces).IndexOf(faceToRemove) + 1; i < length; i++)
		{
			faces[i - 1] = faces[i];
		}
		Memory<int> cellFaceArray = CellFaceArrays[ID];
		c.Faces = cellFaceArray.Slice(0, length - 1);
		Cells[ID] = c;
	}

	internal static void CoalesceCellArrays(Cell[] cells)
	{
		int[] intValues = new int[cells.Sum((Cell c) => c.Faces.Length)];
		int faceLengthSum = 0;
		for (int intervalIndex = 0; intervalIndex < cells.Length; intervalIndex++)
		{
			Cell cell = cells[intervalIndex];
			Memory<int> faces = cell.Faces;
			cell.Faces = intValues.AsMemory().Slice(faceLengthSum, faces.Length);
			faces.CopyTo(cell.Faces);
			faceLengthSum += faces.Length;
			cells[intervalIndex] = cell;
		}
	}

	private bool ValidateInput(Polygon perim, IList<Point> cells)
	{
		if (perim.Count < 3)
		{
			_error.StatusMessage = "Need at least three points on perimeter of the 2D-area.";
			_error.Status = MeshStatus.FewerThanMinPerimeterPoints;
			return false;
		}
		if (perim.HasConsecutiveIdenticalPoints())
		{
			_error.StatusMessage = "Polygon error - please inspect/filter your perimeter for duplicate points.";
			_error.Status = MeshStatus.PerimeterPolygonError;
			return false;
		}
		if (cells.Count <= 1)
		{
			_error.StatusMessage = "Need at least two cells in the 2D-area.";
			_error.Status = MeshStatus.FewerThanMinNumCells;
			return false;
		}
		return true;
	}

	private bool ValidatePointsInsidePerim(IList<Point> cellCenters, Tin perimeterDevTin)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		List<int> pointsOutsidePerimeter = GetPointsOutsidePerimeter(cellCenters, perimeterDevTin);
		stopwatch.Stop();
		if (pointsOutsidePerimeter.Count > 0)
		{
			_error.StatusMessage = pointsOutsidePerimeter.Count + " point(s) detected outside the perimeter of the 2D-area.";
			_error.Status = MeshStatus.PointsOutsidePerimeter;
			_error.BadCells = pointsOutsidePerimeter;
			PopulateCellsWithInputPoints(cellCenters);
			return false;
		}
		return true;
	}

	private bool ValidateDuplicatePoints(Tin meshTIN, IList<Point> cellCenters, List<int> duplicatePoints)
	{
		if (meshTIN == null)
		{
			_error.StatusMessage = "Error creating mesh.";
			_error.Status = MeshStatus.UnknownError;
			PopulateCellsWithInputPoints(cellCenters);
			return false;
		}
		if (duplicatePoints.Count > 0)
		{
			_error.StatusMessage = duplicatePoints.Count + " near-duplicate point(s) detected.";
			_error.Status = MeshStatus.DuplicatePoints;
			_error.BadCells = duplicatePoints;
			PopulateCellsWithInputPoints(cellCenters);
			return false;
		}
		return true;
	}

	private bool ValidatePerimeterErrors(List<int> pointOnPerimeterErrors)
	{
		if (pointOnPerimeterErrors.Count != 0)
		{
			pointOnPerimeterErrors = pointOnPerimeterErrors.Distinct().ToList();
			if (pointOnPerimeterErrors.Contains(-1))
			{
				pointOnPerimeterErrors.Remove(-1);
			}
			_error.Status = MeshStatus.FacePerimeterConnectionError;
			_error.BadCells = pointOnPerimeterErrors;
			_error.StatusMessage = pointOnPerimeterErrors.Count + " face errors detected on the perimeter of the mesh.";
			return false;
		}
		return true;
	}

	private bool ValidateSqueezeErrors(List<int> cellErrs)
	{
		if (cellErrs.Count != 0)
		{
			cellErrs = cellErrs.Distinct().ToList();
			if (cellErrs.Contains(-1))
			{
				cellErrs.Remove(-1);
			}
			_error.Status = MeshStatus.CellAreaTotalError;
			_error.BadCells = cellErrs;
			_error.StatusMessage = cellErrs.Count + " cell errors detected in the mesh.";
			return false;
		}
		return true;
	}

	private bool ValidateDCSubgraphs()
	{
		BitArray hasPushed = new BitArray(Cells.Length);
		byte[] graph = new byte[Cells.Length];
		byte curIsland = 0;
		if (TrackDiagnostics)
		{
			DiagnosticInfo.CellConnectivityGraph = graph;
		}
		Action<int> PushNeighbors = (int c) =>
		{
			Span<int> faces = Cells[c].Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int index = faces[i];
				int otherCell = Faces[index].OtherCell(c);
				if (otherCell != -1)
				{
					graph[otherCell] = graph[c];
				}
			}
		};
		Func<int> func = () =>
		{
			for (int i = 0; i < Cells.Length; i++)
			{
				if (graph[i] == 0)
				{
					return i;
				}
			}
			return -1;
		};
		Action<int> action = (int startcell) =>
		{
			curIsland++;
			graph[startcell] = curIsland;
			bool condition = false;
			bool condition2;
			do
			{
				condition2 = false;
				condition = !condition;
				int lengthOffset;
				int lengthOffset2;
				int sign;
				if (condition)
				{
					lengthOffset = 0;
					lengthOffset2 = Cells.Length - 1;
					sign = 1;
				}
				else
				{
					lengthOffset = Cells.Length - 1;
					lengthOffset2 = 0;
					sign = -1;
				}
				for (int i = lengthOffset; i != lengthOffset2; i += sign)
				{
					if (!hasPushed[i] && graph[i] != 0)
					{
						PushNeighbors(i);
						hasPushed[i] = true;
						condition2 = true;
					}
				}
			}
			while (condition2);
		};
		int func2 = func();
		while (func2 != -1 && curIsland < byte.MaxValue)
		{
			action(func2);
			func2 = func();
		}
		if (curIsland > 1)
		{
			Dictionary<int, List<int>> intListByInt = new Dictionary<int, List<int>>();
			for (int intervalIndex = 0; intervalIndex < Cells.Length; intervalIndex++)
			{
				int key = graph[intervalIndex];
				if (!intListByInt.ContainsKey(key))
				{
					intListByInt.Add(key, new List<int>());
				}
				intListByInt[key].Add(intervalIndex);
			}
			int key2Term = -1;
			int key2Term2 = -1;
			foreach (int key2 in intListByInt.Keys)
			{
				if (intListByInt[key2].Count > key2Term2)
				{
					key2Term = key2;
					key2Term2 = intListByInt[key2].Count;
				}
			}
			List<int> badCells = new List<int>();
			foreach (int key3 in intListByInt.Keys)
			{
				if (key3 != key2Term)
				{
					badCells.AddRange(intListByInt[key3]);
				}
			}
			_error.BadCells = badCells;
			_error.Status = MeshStatus.DisconnectedGraphs;
			_error.StatusMessage = badCells.Count + " cell(s) are not connected to the mesh.";
			return false;
		}
		return true;
	}

	private List<Tuple<int, double>> CellsSortedByLargestArea(int ct = 10)
	{
		return (from cIdx in Enumerable.Range(0, CellCount)
			select new Tuple<int, double>(cIdx, CellPolygonArea(cIdx)) into tup
			orderby tup.Item2 descending
			select tup).Take(ct).ToList();
	}

	private bool ValidateAreaCheck()
	{
		MeshStatistics meshStatistics = GetMeshStatistics();
		double absoluteArea = Math.Abs(Perimeter.Area);
		double cellsTotalAreaOverAbsoluteArea = meshStatistics.CellsTotalArea / absoluteArea;
		if (Math.Abs(1.0 - cellsTotalAreaOverAbsoluteArea) > 1E-06)
		{
			_error.Status = MeshStatus.CellAreaTotalError;
			if (meshStatistics.CellMaximumArea > 0.01 * absoluteArea)
			{
				_error.BadCells = new List<int>(new int[1] { meshStatistics.CellMaximumAreaIndex });
				_error.StatusMessage = "Total area from all cells does not match the perimeter area. Possible face errors on the perimeter of the mesh. Cell " + meshStatistics.CellMaximumAreaIndex + " is potentially bad.";
			}
			else
			{
				_error.StatusMessage = "Total area from all cells does not match the perimeter area. Possible face errors on the perimeter of the mesh.";
			}
			return false;
		}
		if (meshStatistics.CellMinimumArea < 0.0)
		{
			_error.Status = MeshStatus.CellAreaTotalError;
			_error.BadCells = new List<int>(new int[1] { meshStatistics.CellMinimumAreaIndex });
			_error.StatusMessage = "Some cells have negative area. Possible face errors on the perimeter of the mesh. Cell " + meshStatistics.CellMinimumAreaIndex + " is bad.";
			return false;
		}
		return true;
	}

	private bool ValidateMinFaceLength()
	{
		HashSet<int> intSet = new HashSet<int>();
		Extent extent = Perimeter.Extent;
		double widthExtentTerm = Math.Max(extent.Width, extent.Height);
		float computed = (float)Math.Min(0.001, widthExtentTerm / 1000000.0);
		for (int i = 0; i < Faces.Count; i++)
		{
			if (FaceSegment(i).Length < (double)computed)
			{
				Face face = Faces[i];
				if (face.cellA != -1 && !CellIsVirtual(face.cellA))
				{
					intSet.Add(face.cellA);
				}
				if (face.cellB != -1 && !CellIsVirtual(face.cellB))
				{
					intSet.Add(face.cellB);
				}
			}
		}
		if (intSet.Count > 0)
		{
			_error.Status = MeshStatus.MinFaceLenError;
			_error.StatusMessage = "Some faces were below the minimum-length constraints. (" + computed + ")";
			_error.BadFaces = intSet.ToList();
			return false;
		}
		return true;
	}

	private bool ValidateAllFacepointsSet()
	{
		foreach (Face face in Faces)
		{
			if (face.fpB == -1 || face.fpA == -1)
			{
				_error.Status = MeshStatus.UnknownError;
				_error.StatusMessage = "Unknown Error: Some facepoints were not set correctly.";
				return false;
			}
		}
		return true;
	}

	private bool ValidateNumFacesPerCell()
	{
		List<int> badCells = new List<int>();
		for (int i = 0; i < Cells.Length; i++)
		{
			Cell cell = Cells[i];
			if (cell.Faces.Length > 8)
			{
				badCells.Add(i);
			}
		}
		if (badCells.Count > 0)
		{
			_error.BadCells = badCells;
			_error.Status = MeshStatus.MaxFacesPerCellExceeded;
			_error.StatusMessage = badCells.Count + " cell(s) with more than 8 sides.";
			return false;
		}
		return true;
	}

	private bool ValidateCell(int cidx)
	{
		if (CellPolygonArea(cidx) <= 0.0)
		{
			return false;
		}
		Cell cell = Cells[cidx];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int faceIndex = faces[i];
			Face face = Faces[faceIndex];
			if (face.cellA != cidx && face.cellB != cidx)
			{
				return false;
			}
			FacePoint facePoint = FacePoints[face.fpA];
			FacePoint facePoint2 = FacePoints[face.fpB];
			if (!facePoint.Faces.Contains(faceIndex))
			{
				return false;
			}
			if (!facePoint2.Faces.Contains(faceIndex))
			{
				return false;
			}
		}
		if (CellPolygon(cidx).SelfIntersects())
		{
			return false;
		}
		return true;
	}

	private bool ApplyBreaklines(IList<Polyline> breaklines)
	{
		if (breaklines == null || breaklines.Count == 0)
		{
			return true;
		}
		float unity = 0.5f;
		if (EnableTestMagnetizationChanges)
		{
			unity = 1f;
		}
		Dictionary<int, Tuple<int, int>> magnetizeFacepointsEnsureNonColinear = MagnetizeFacepointsEnsureNonColinear(breaklines, out var collapsedFaceCount, out var intersectionMagnetization, unity);
		if (magnetizeFacepointsEnsureNonColinear.Count == 0)
		{
			return true;
		}
		EnforceInternalPoints(breaklines, magnetizeFacepointsEnsureNonColinear, intersectionMagnetization);
		bool result = true;
		if (collapsedFaceCount > 0)
		{
			result = SqueezeNullFFP();
		}
		return result;
	}

	private Dictionary<int, Tuple<int, int>> MagnetizeFacepointsEnsureNonColinear(IList<Polyline> breaklines, out int collapsedFaceCount, out Dictionary<int, Tuple<int, int>> intersectionMagnetization, double magneticRadiusFactor = 0.5, bool useBackoffMethod = false)
	{
		Dictionary<int, Tuple<int, int>> valueTupleByInt = new Dictionary<int, Tuple<int, int>>();
		intersectionMagnetization = new Dictionary<int, Tuple<int, int>>();
		SpatialIndex<Tuple<int, int>> spatialIndex = new SpatialIndex<Tuple<int, int>>(Perimeter.Extent);
		for (int i = 0; i < breaklines.Count; i++)
		{
			Polyline polyline = breaklines[i];
			for (int j = 0; j < polyline.SegmentCount; j++)
			{
				spatialIndex.Add(polyline.Segments[j], new Tuple<int, int>(i, j));
			}
		}
		List<LineCollectionIntersection> allIntersections = null;
		SpatialIndex<LineCollectionIntersection> spatialIndex2 = null;
		allIntersections = PolylineCollection.GetAllIntersections(breaklines);
		spatialIndex2 = new SpatialIndex<LineCollectionIntersection>(Perimeter.Extent);
		for (int k = 0; k < allIntersections.Count; k++)
		{
			LineCollectionIntersection key = allIntersections[k];
			spatialIndex2.AddInBounds(key.Intersection.Point, key);
		}
		collapsedFaceCount = 0;
		for (int l = 0; l < FacePoints.Count; l++)
		{
			if (FacePoints[l].IsNull() || FacePointIsPerimeter(l) || valueTupleByInt.ContainsKey(l))
			{
				continue;
			}
			int smallestAdjacentFace = SmallestAdjacentFace(l, out var _, out var faceLen);
			double averageFaceLengthAroundFacepoint = AverageFaceLengthAroundFacepoint(l);
			double faceLenAverageFaceLengthAroundFacepointTerm = Math.Min(faceLen, averageFaceLengthAroundFacepoint * 0.5);
			faceLenAverageFaceLengthAroundFacepointTerm *= magneticRadiusFactor;
			FacePoint facePoint = FacePoints[l];
			Extent extent = new Extent(new Point(facePoint.Point), faceLenAverageFaceLengthAroundFacepointTerm * 2.0, faceLenAverageFaceLengthAroundFacepointTerm * 2.0);
			List<Tuple<int, int>> elements = spatialIndex.GetElements(extent);
			if (elements.Count == 0)
			{
				continue;
			}
			double maxValue = double.MaxValue;
			Point point = Point.NullPoint;
			Tuple<int, int> tuple = null;
			foreach (Tuple<int, int> element2 in elements)
			{
				Segment segment = breaklines[element2.Item1].Segments[element2.Item2];
				double distanceToPoint = segment.DistanceToPoint(facePoint.Point);
				if (distanceToPoint < maxValue)
				{
					point = segment.NearestPoint(facePoint.Point);
					maxValue = distanceToPoint;
					tuple = element2;
				}
			}
			if (!(maxValue < faceLenAverageFaceLengthAroundFacepointTerm))
			{
				continue;
			}
			Tuple<int, int> tuple2 = null;
			if (EnableTestMagnetizationChanges)
			{
				double maxValue2 = double.MaxValue;
				Tuple<int, int> tuple3 = null;
				Tuple<int, int> tuple4 = null;
				Point point2 = Point.NullPoint;
				foreach (LineCollectionIntersection element in spatialIndex2.GetElements(extent))
				{
					int aPolylineIndex = element.APolylineIndex;
					int bPolylineIndex = element.BPolylineIndex;
					int segmentIndex = element.Intersection.AIntersection.SegmentIndex;
					int segmentIndex2 = element.Intersection.BIntersection.SegmentIndex;
					if (aPolylineIndex == tuple.Item1 || bPolylineIndex == tuple.Item1)
					{
						double distanceTo = element.Intersection.Point.DistanceTo(point);
						if (distanceTo < maxValue2)
						{
							maxValue2 = distanceTo;
							tuple3 = new Tuple<int, int>(aPolylineIndex, segmentIndex);
							tuple4 = new Tuple<int, int>(bPolylineIndex, segmentIndex2);
							point2 = element.Intersection.Point;
						}
					}
				}
				if (maxValue2 < faceLenAverageFaceLengthAroundFacepointTerm)
				{
					point = point2;
					maxValue = maxValue2;
					tuple = tuple3;
					tuple2 = tuple4;
				}
			}
			Point point3 = new Point(point);
			if (useBackoffMethod)
			{
				Segment segment2 = new Segment(point3, facePoint.Point);
				for (float computed = 0f; (double)computed <= 0.9; computed += 0.1f)
				{
					Point point4 = new Point(segment2.PointAtFraction(computed));
					if (ValidateFacePointMove(l, point4))
					{
						facePoint.Point = point4;
						FacePoints[l] = facePoint;
						if (computed == 0f)
						{
							valueTupleByInt.Add(l, new Tuple<int, int>(tuple.Item1, tuple.Item2));
						}
						break;
					}
				}
			}
			else if (ValidateFacePointMove(l, point3))
			{
				facePoint.Point = point3;
				FacePoints[l] = facePoint;
				valueTupleByInt.Add(l, new Tuple<int, int>(tuple.Item1, tuple.Item2));
				if (tuple2 != null)
				{
					intersectionMagnetization.Add(l, new Tuple<int, int>(tuple2.Item1, tuple2.Item2));
				}
			}
			else if (EnableTestMagnetizationChanges && ValidateFacePointMoveWithFaceCollapse(l, point3, smallestAdjacentFace))
			{
				facePoint.Point = point3;
				FacePoints[l] = facePoint;
				CollapseFace(smallestAdjacentFace, out var newFpIdx, point3);
				collapsedFaceCount++;
				valueTupleByInt[newFpIdx] = new Tuple<int, int>(tuple.Item1, tuple.Item2);
				if (tuple2 != null)
				{
					intersectionMagnetization[newFpIdx] = new Tuple<int, int>(tuple2.Item1, tuple2.Item2);
				}
			}
		}
		return valueTupleByInt;
	}

	private bool ValidateFacePointMove(int fpIdx, Point candidateNewPt)
	{
		foreach (int cellsAroundFacepoint in GetCellsAroundFacepoint(fpIdx))
		{
			Cell cell = Cells[cellsAroundFacepoint];
			List<Point> points = new List<Point>();
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int index = faces[i];
				int fPNext = Faces[index].GetFPNext(cellsAroundFacepoint);
				if (fPNext == fpIdx)
				{
					points.Add(candidateNewPt);
				}
				else
				{
					points.Add(FacePoints[fPNext].Point);
				}
			}
			if (!ArePointsConvexEnough(points))
			{
				return false;
			}
		}
		return true;
	}

	private bool ValidateFacePointMoveWithFaceCollapse(int fpMoveIdx, Point candidateNewPt, int collapseFaceIdx)
	{
		_ = FacePoints[fpMoveIdx];
		Face face = Faces[collapseFaceIdx];
		int otherFacepoint = face.OtherFacepoint(fpMoveIdx);
		_ = FacePoints[otherFacepoint];
		List<int> cellsAroundFacepoint = GetCellsAroundFacepoint(fpMoveIdx);
		List<int> cellsAroundFacepoint2 = GetCellsAroundFacepoint(otherFacepoint);
		cellsAroundFacepoint.AddRange(cellsAroundFacepoint2);
		foreach (int distinct in cellsAroundFacepoint.Distinct().ToList())
		{
			if (distinct == -1)
			{
				return false;
			}
			Cell cell = Cells[distinct];
			List<Point> points = new List<Point>();
			Span<int> faces = cell.Faces.Span;
			for (int i = 0; i < faces.Length; i++)
			{
				int faceIndex = faces[i];
				if (faceIndex != collapseFaceIdx)
				{
					int fPNext = Faces[faceIndex].GetFPNext(distinct);
					if (fPNext == fpMoveIdx || fPNext == otherFacepoint)
					{
						points.Add(candidateNewPt);
					}
					else
					{
						points.Add(FacePoints[fPNext].Point);
					}
				}
			}
			if (!ArePointsConvexEnough(points))
			{
				return false;
			}
		}
		int cellA = face.cellA;
		int cellB = face.cellB;
		if (cellA == -1 || cellB == -1)
		{
			return false;
		}
		Point point = CellCenters[cellA];
		Point other = CellCenters[cellB];
		if (FaceSimpleLength(collapseFaceIdx) < point.DistanceTo(other) * 0.4)
		{
			return true;
		}
		return false;
	}

	private bool ArePointsStrictlyConvex(List<Point> pts)
	{
		if (pts == null || pts.Count <= 2)
		{
			throw new ArgumentException("This isn't a polygon");
		}
		Func<Point, Point, Point, bool> func = (Point pt1, Point pt2, Point pt3) => !(Vector.CrossProduct(in pt1, in pt2, in pt3) <= 9.999999747378752E-05);
		int count = pts.Count;
		for (int intervalIndex = 0; intervalIndex <= count - 3; intervalIndex++)
		{
			if (func(pts[intervalIndex], pts[intervalIndex + 1], pts[intervalIndex + 2]))
			{
				return false;
			}
		}
		if (func(pts[count - 2], pts[count - 1], pts[0]))
		{
			return false;
		}
		if (func(pts[count - 1], pts[count], pts[1]))
		{
			return false;
		}
		return true;
	}

	private bool ArePointsConvexEnough(List<Point> pts)
	{
		if (pts == null || pts.Count <= 2)
		{
			return false;
		}
		Func<Point, Point, Point, bool> func = (Point pt1, Point pt2, Point pt3) =>
		{
			if (Vector.CrossProduct(in pt1, in pt2, in pt3) <= 1E-08)
			{
				return true;
			}
			return Vector.AngleBetween(in pt2, in pt3, in pt1) >= Math.PI * 17.0 / 18.0;
		};
		int count = pts.Count;
		for (int intervalIndex = 0; intervalIndex <= count - 3; intervalIndex++)
		{
			if (func(pts[intervalIndex], pts[intervalIndex + 1], pts[intervalIndex + 2]))
			{
				return false;
			}
		}
		if (func(pts[count - 2], pts[count - 1], pts[0]))
		{
			return false;
		}
		if (func(pts[count - 1], pts[0], pts[1]))
		{
			return false;
		}
		return true;
	}

	private static bool ANY<T>(Memory<T> mem, Func<T, bool> action)
	{
		return ANY(mem.Span, action);
	}

	private static bool ANY<T>(Span<T> sp, Func<T, bool> action)
	{
		for (int i = 0; i < sp.Length; i++)
		{
			if (action(sp[i]))
			{
				return true;
			}
		}
		return false;
	}

	private List<int> GetAllCollapsibleFaces(ref bool needsAnotherPass, double heuristicFactor = 1.0)
	{
		List<int> collapse = new List<int>();
		needsAnotherPass = false;
		for (int i = 0; i <= Faces.Count - 1; i++)
		{
			if (FaceIsPerimeter(i))
			{
				continue;
			}
			Face f = Faces[i];
			FacePoint facePoint = FacePoints[f.fpA];
			FacePoint facePoint2 = FacePoints[f.fpB];
			Cell cell = Cells[f.cellA];
			Cell cell2 = Cells[f.cellB];
			double distanceTo = facePoint.Point.DistanceTo(facePoint2.Point);
			double length = FacePerpendicularSegment(f).Length;
			if (distanceTo < length * 0.05 * heuristicFactor && cell.Faces.Span.Length > 3 && cell2.Faces.Span.Length > 3)
			{
				if (ANY(cell.Faces.Span, (int ff) => collapse.Contains(ff)) || ANY(cell2.Faces.Span, (int ff) => collapse.Contains(ff)))
				{
					needsAnotherPass = true;
				}
				else if (facePoint.Faces.Any((int ff) => collapse.Contains(ff)) || facePoint2.Faces.Any((int ff) => collapse.Contains(ff)))
				{
					needsAnotherPass = true;
				}
				else
				{
					collapse.Add(i);
				}
			}
		}
		return collapse;
	}

	public Segment CellToCellSegment(int ca, int cb)
	{
		return new Segment(CellCenters[ca], CellCenters[cb]);
	}

	public Segment FacePerpendicularSegment(int fIdx)
	{
		Face face = Faces[fIdx];
		return CellToCellSegment(face.cellA, face.cellB);
	}

	public Segment FacePerpendicularSegment(Face f)
	{
		return CellToCellSegment(f.cellA, f.cellB);
	}

	private List<int> GetCollapsibleFaces(List<int> connectedToFacepoints, double heuristicFactor = 1.0)
	{
		List<int> distinct = connectedToFacepoints.SelectMany((int fp) => FacePoints[fp].Faces).Distinct().ToList();
		List<int> collapse = new List<int>();
		foreach (int distinct2 in distinct)
		{
			if (!FaceIsPerimeter(distinct2))
			{
				Face face = Faces[distinct2];
				FacePoint facePoint = FacePoints[face.fpA];
				FacePoint facePoint2 = FacePoints[face.fpB];
				Cell cell = Cells[face.cellA];
				Cell cell2 = Cells[face.cellB];
				double distanceTo = facePoint.Point.DistanceTo(facePoint2.Point);
				double length = FacePerpendicularSegment(distinct2).Length;
				if (distanceTo < length * 0.05 * heuristicFactor && cell.Faces.Length > 3 && cell2.Faces.Length > 3 && !ANY(cell.Faces, (int ff) => collapse.Contains(ff)) && !ANY(cell2.Faces, (int ff) => collapse.Contains(ff)) && !facePoint.Faces.Any((int ff) => collapse.Contains(ff)) && !facePoint2.Faces.Any((int ff) => collapse.Contains(ff)))
				{
					collapse.Add(distinct2);
				}
			}
		}
		return collapse;
	}

	private bool CollapseFaces(List<int> faceList)
	{
		foreach (int face in faceList)
		{
			CollapseFace(face, out var _);
		}
		return SqueezeNullFFP();
	}

	private void CollapseFace(int fCollapseIdx, out int newFpIdx, Point? collapseToPoint = null)
	{
		Face face = Faces[fCollapseIdx];
		Cell c = Cells[face.cellA];
		bool cellBFaceTerm = face.cellB == -1;
		Cell c2 = new Cell();
		if (!cellBFaceTerm)
		{
			c2 = Cells[face.cellB];
		}
		int fpA = face.fpA;
		int fpB = face.fpB;
		FacePoint facePoint = FacePoints[fpA];
		FacePoint facePoint2 = FacePoints[fpB];
		bool facePointIsPerimeterOrFacePointIsPerimeter = FacePointIsPerimeter(fpA) || FacePointIsPerimeter(fpB);
		REMOVEFACE(face.cellA, ref c, fCollapseIdx);
		if (!cellBFaceTerm)
		{
			REMOVEFACE(face.cellB, ref c2, fCollapseIdx);
		}
		FacePoint facePoints;
		if (collapseToPoint.HasValue && collapseToPoint.HasValue)
		{
			facePoints = new FacePoint(collapseToPoint.Value);
		}
		else if (FacePointIsPerimeter(fpA))
		{
			facePoints = new FacePoint(facePoint.Point);
		}
		else
		{
			facePoints = ((!FacePointIsPerimeter(fpB)) ? new FacePoint(facePoint.Point.Midpoint(facePoint2.Point)) : new FacePoint(facePoint2.Point));
		}
		List<int> faces = new List<int>();
		foreach (int face2 in facePoint.Faces)
		{
			if (face2 == fCollapseIdx)
			{
				int fi = facePoint2.Faces.IndexOf(fCollapseIdx);
				for (int i = 0; i < facePoint2.Faces.Count - 1; i++)
				{
					int fi2 = facePoint2.CCWFaceByLocalIndex(fi);
					faces.Add(facePoint2.Faces[fi2]);
					fi = fi2;
				}
			}
			else
			{
				faces.Add(face2);
			}
		}
		facePoints.Faces = faces;
		FacePoints[fpA] = facePoints;
		FacePoints[fpB] = FacePoint.NullFacePoint;
		newFpIdx = fpA;
		foreach (int face3 in facePoints.Faces)
		{
			Face face4 = Faces[face3];
			if (face4.fpA == fpB)
			{
				face4.fpA = fpA;
			}
			else if (face4.fpB == fpB)
			{
				face4.fpB = fpA;
			}
			Faces[face3] = face4;
		}
		if (facePointIsPerimeterOrFacePointIsPerimeter)
		{
			Func<int, bool> func = (int f) =>
			{
				int cellB = Faces[f].cellB;
				if (cellB == -1)
				{
					return true;
				}
				return CellIsVirtual(cellB) ? true : false;
			};
			int ordinal = 0;
			while (!func(faces.First()) || !func(faces.Last()))
			{
				faces.Add(faces.First());
				faces.RemoveAt(0);
				ordinal++;
				if (ordinal == faces.Count)
				{
					break;
				}
			}
		}
		Faces[fCollapseIdx] = Face.NullFace;
	}

	private bool SqueezeNullFFP()
	{
		List<int> intList = new List<int>();
		int facesFaceTerm = Faces.Where((Face f) => f.IsNull()).Count();
		int facePointsFacePointTerm = FacePoints.Where((FacePoint fp) => fp.IsNull()).Count();
		Face[] faces = new Face[Faces.Count - facesFaceTerm];
		List<Point>[] pointListArray = new List<Point>[Faces.Count - facesFaceTerm];
		FacePoint[] facePoints = new FacePoint[FacePoints.Count - facePointsFacePointTerm];
		int ordinal = 0;
		for (int intervalIndex = 0; intervalIndex < Faces.Count; intervalIndex++)
		{
			Face face = Faces[intervalIndex];
			if (face.IsNull())
			{
				ordinal++;
				continue;
			}
			if (ordinal == 0)
			{
				faces[intervalIndex] = face;
				pointListArray[intervalIndex] = FaceInternalPoints[intervalIndex];
				continue;
			}
			int faces2 = intervalIndex - ordinal;
			int cellA = face.cellA;
			Cell cell = Cells[cellA];
			Span<int> facesSpan = cell.Faces.Span;
			int indexOf = ((ReadOnlySpan<int>)facesSpan).IndexOf(intervalIndex);
			if (indexOf == -1)
			{
				intList.Add(cellA);
				break;
			}
			facesSpan[indexOf] = faces2;
			int cellB = face.cellB;
			if (cellB != -1)
			{
				Cell cell2 = Cells[cellB];
				Span<int> faces3 = cell2.Faces.Span;
				int indexOf2 = ((ReadOnlySpan<int>)faces3).IndexOf(intervalIndex);
				if (indexOf2 == -1)
				{
					intList.Add(cellB);
					break;
				}
				faces3[indexOf2] = faces2;
			}
			int fpA = face.fpA;
			int fpB = face.fpB;
			FacePoint facePoint = FacePoints[fpA];
			FacePoint facePoint2 = FacePoints[fpB];
			if (!facePoint.IsNull())
			{
				int indexOf3 = facePoint.Faces.IndexOf(intervalIndex);
				if (indexOf3 == -1)
				{
					intList.Add(cellA);
					intList.Add(cellB);
					break;
				}
				facePoint.Faces[indexOf3] = faces2;
			}
			if (!facePoint2.IsNull())
			{
				int indexOf4 = facePoint2.Faces.IndexOf(intervalIndex);
				if (indexOf4 == -1)
				{
					intList.Add(cellA);
					intList.Add(cellB);
					break;
				}
				facePoint2.Faces[indexOf4] = faces2;
			}
			faces[faces2] = face;
			pointListArray[faces2] = FaceInternalPoints[intervalIndex];
		}
		if (!ValidateSqueezeErrors(intList))
		{
			return false;
		}
		int ordinal2 = 0;
		for (int intervalIndex2 = 0; intervalIndex2 <= FacePoints.Count - 1; intervalIndex2++)
		{
			FacePoint facePoint3 = FacePoints[intervalIndex2];
			if (facePoint3.IsNull())
			{
				ordinal2++;
				continue;
			}
			if (ordinal2 == 0)
			{
				facePoints[intervalIndex2] = facePoint3;
				continue;
			}
			int ordinal3 = intervalIndex2 - ordinal2;
			foreach (int face3 in facePoint3.Faces)
			{
				Face face2 = faces[face3];
				if (face2.fpA == intervalIndex2)
				{
					face2.fpA = ordinal3;
				}
				else
				{
					if (face2.fpB != intervalIndex2)
					{
						throw new Exception("Bad Face/FacePoint pointer agreement");
					}
					face2.fpB = ordinal3;
				}
				faces[face3] = face2;
			}
			facePoints[ordinal3] = facePoint3;
		}
		if (!ValidateSqueezeErrors(intList))
		{
			return false;
		}
		Faces = faces.ToList();
		FaceInternalPoints = pointListArray.ToList();
		FacePoints = facePoints.ToList();
		return true;
	}

	private void EnforceInternalPoints(IList<Polyline> breaklines, Dictionary<int, Tuple<int, int>> fpsMoved, Dictionary<int, Tuple<int, int>> fpsMovedSecondPass = null)
	{
		for (int i = 0; i < Faces.Count; i++)
		{
			Face face = Faces[i];
			if (face.IsNull() || FaceIsPerimeter(i) || !fpsMoved.ContainsKey(face.fpA) || !fpsMoved.ContainsKey(face.fpB))
			{
				continue;
			}
			Tuple<int, int> tuple = fpsMoved[face.fpA];
			Tuple<int, int> tuple2 = fpsMoved[face.fpB];
			if (tuple.Item1 != tuple2.Item1)
			{
				if (fpsMovedSecondPass == null)
				{
					continue;
				}
				if (fpsMovedSecondPass.ContainsKey(face.fpA))
				{
					Tuple<int, int> tuple3 = fpsMovedSecondPass[face.fpA];
					if (tuple3.Item1 != tuple2.Item1)
					{
						continue;
					}
					tuple = tuple3;
				}
				else
				{
					if (!fpsMovedSecondPass.ContainsKey(face.fpB))
					{
						continue;
					}
					Tuple<int, int> tuple4 = fpsMovedSecondPass[face.fpB];
					if (tuple4.Item1 != tuple.Item1)
					{
						continue;
					}
					tuple2 = tuple4;
				}
			}
			if (tuple.Item2 == tuple2.Item2)
			{
				continue;
			}
			if (tuple.Item2 < tuple2.Item2)
			{
				List<Point> faceInternalPoints = new List<Point>();
				for (int j = tuple.Item2; j < tuple2.Item2; j++)
				{
					Segment segment = breaklines[tuple.Item1].Segments[j];
					if (!(segment.A.DistanceTo(segment.B) < 1E-06))
					{
						faceInternalPoints.Add(segment.B);
					}
				}
				FaceInternalPoints[i] = faceInternalPoints;
			}
			else
			{
				List<Point> faceInternalPointsList = new List<Point>();
				for (int k = tuple2.Item2; k < tuple.Item2; k++)
				{
					Segment segment2 = breaklines[tuple.Item1].Segments[k];
					if (!(segment2.A.DistanceTo(segment2.B) < 1E-06))
					{
						faceInternalPointsList.Add(segment2.B);
					}
				}
				faceInternalPointsList.Reverse();
				FaceInternalPoints[i] = faceInternalPointsList;
			}
			List<Point> faceInternalPoint = FaceInternalPoints[i];
			if (faceInternalPoint != null && faceInternalPoint.Count > 0)
			{
				if (faceInternalPoint[0].DistanceTo(FacePoints[face.fpA].Point) < 1E-06)
				{
					faceInternalPoint.RemoveAt(0);
				}
				if (faceInternalPoint.Count > 0 && faceInternalPoint.Last().DistanceTo(FacePoints[face.fpB].Point) < 1E-06)
				{
					faceInternalPoint.RemoveAt(faceInternalPoint.Count - 1);
				}
			}
			if (!ValidateCell(face.cellA) || !ValidateCell(face.cellB))
			{
				FaceInternalPoints[i] = null;
			}
		}
	}

	private void AdHocFixPerimeterIssues(Tin perimeterDevTIN)
	{
		int t = 0;
		for (int i = 0; i < Faces.Count; i++)
		{
			Face face = Faces[i];
			List<Point> pointsOnFace = PointsOnFace(i);
			bool condition = false;
			for (int j = 0; j < pointsOnFace.Count; j++)
			{
				Point point = pointsOnFace[j];
				if (!perimeterDevTIN.ContainsPointBufferedFavorInBounds(point, _filterToleranceTIN / 10.0, ref t))
				{
					condition = true;
					break;
				}
			}
			if (condition)
			{
				_ = FacePoints[face.fpA];
				_ = FacePoints[face.fpB];
			}
		}
		throw new NotImplementedException();
	}

	private bool TryAutoFix(MeshError err, ref List<Point> suggestedNewCells, ref List<int> suggestedRemoveCells)
	{
		throw new NotImplementedException();
	}

	public int SmallestAdjacentFace(int fpIdx, out int fpLocalIdx, out double faceLen)
	{
		double maxValue = double.MaxValue;
		int result = -1;
		fpLocalIdx = -1;
		FacePoint facePoint = FacePoints[fpIdx];
		for (int i = 0; i < facePoint.Faces.Count; i++)
		{
			int faceIndex = facePoint.Faces[i];
			Face face = Faces[faceIndex];
			FacePoint facePoint2 = FacePoints[face.OtherFacepoint(fpIdx)];
			double distanceTo = facePoint.Point.DistanceTo(facePoint2.Point);
			if (distanceTo < maxValue)
			{
				result = faceIndex;
				fpLocalIdx = i;
				maxValue = distanceTo;
			}
		}
		faceLen = maxValue;
		return result;
	}

	public double AverageFaceLengthAroundFacepoint(int fpIdx)
	{
		double distanceToSum = 0.0;
		FacePoint facePoint = FacePoints[fpIdx];
		foreach (int face2 in facePoint.Faces)
		{
			Face face = Faces[face2];
			FacePoint facePoint2 = FacePoints[face.OtherFacepoint(fpIdx)];
			distanceToSum += facePoint.Point.DistanceTo(facePoint2.Point);
		}
		return distanceToSum / (double)facePoint.Faces.Count;
	}

	public int NearestFacePointInCell(int c, Point point, out double fpDistance)
	{
		Cell cell = Cells[c];
		fpDistance = double.MaxValue;
		int unity = -1;
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			int index = faces[i];
			double fpDistance2;
			if (unity == -1)
			{
				unity = Faces[index].fpA;
				fpDistance = point.DistanceTo(FacePoints[Faces[index].fpA].Point);
			}
			else
			{
				fpDistance2 = point.DistanceTo(FacePoints[Faces[index].fpA].Point);
				if (fpDistance > fpDistance2)
				{
					unity = Faces[index].fpA;
					fpDistance = fpDistance2;
				}
			}
			fpDistance2 = point.DistanceTo(FacePoints[Faces[index].fpB].Point);
			if (fpDistance > fpDistance2)
			{
				unity = Faces[index].fpB;
				fpDistance = fpDistance2;
			}
		}
		return unity;
	}

	public List<int> GetCellsAroundFacepoint(int fp)
	{
		IList<int> faces = FacePoints[fp].Faces;
		List<int> cellsAroundFacepoint = new List<int>();
		foreach (int faceIndex in faces)
		{
			if (Faces[faceIndex].fpB == fp)
			{
				cellsAroundFacepoint.Add(Faces[faceIndex].cellA);
			}
			else
			{
				cellsAroundFacepoint.Add(Faces[faceIndex].cellB);
			}
		}
		if (FacePointIsPerimeter(fp))
		{
			cellsAroundFacepoint.Add(Faces[faces.Last()].cellB);
		}
		return cellsAroundFacepoint;
	}

	public bool FacePointIsPerimeter(int fp)
	{
		FacePoint facePoint = FacePoints[fp];
		if (facePoint.Faces.IsNullOrEmpty())
		{
			return false;
		}
		foreach (int face in facePoint.Faces)
		{
			int cellB = Faces[face].cellB;
			if (cellB == -1)
			{
				return true;
			}
			if (CellIsVirtual(cellB))
			{
				return true;
			}
		}
		return false;
	}

	public bool CellIsVirtual(int cIdx)
	{
		if (cIdx < 0)
		{
			return false;
		}
		if (Cells[cIdx].Faces.Length == 1)
		{
			return true;
		}
		return false;
	}

	public double FaceSimpleLength(int f)
	{
		Face face = Faces[f];
		return FacePoints[face.fpA].Point.DistanceTo(FacePoints[face.fpB].Point);
	}

	public bool FaceIsPerimeter(int f)
	{
		int cellB = Faces[f].cellB;
		return cellB switch
		{
			-1 => true, 
			int.MinValue => false, 
			_ => Cells[cellB].Faces.Length == 1, 
		};
	}

	public Segment FaceSegment(int fIdx)
	{
		Face face = Faces[fIdx];
		return new Segment(FacePoints[face.fpA].Point, FacePoints[face.fpB].Point);
	}

	public bool CellIsPerimeter(int cIdx)
	{
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		if (faces.Length == 1)
		{
			return true;
		}
		Span<int> facesSpan = faces;
		for (int i = 0; i < facesSpan.Length; i++)
		{
			int f = facesSpan[i];
			if (FaceIsPerimeter(f))
			{
				return true;
			}
		}
		return false;
	}

	public double CellPolygonArea(int c)
	{
		return CellPolygon(c)?.Area ?? 0.0;
	}

	public Polygon CellPolygon(int c)
	{
		return new Polygon(CellPolygonPoints(c));
	}

	private List<Point> CellPolygonPoints(int c)
	{
		if (c >= Cells.Length)
		{
			return null;
		}
		Cell cell = Cells[c];
		Span<int> faces = cell.Faces.Span;
		List<Point> points = new List<Point>();
		if (faces.IsEmpty)
		{
			return null;
		}
		if (faces.Length == 1)
		{
			int index = faces[0];
			Face face = Faces[index];
			List<Point> faceInternalPoint = FaceInternalPoints[index];
			points.Add(new Point(FacePoints[face.fpA].Point));
			if (faceInternalPoint != null)
			{
				points.AddRange(faceInternalPoint);
			}
			points.Add(new Point(FacePoints[face.fpB].Point));
			if (faceInternalPoint != null)
			{
				points.AddRangeReverse(faceInternalPoint);
			}
			return points;
		}
		for (int i = 0; i < faces.Length; i++)
		{
			int index2 = faces[i];
			Face face2 = Faces[index2];
			List<Point> faceInternalPointList = FaceInternalPoints[index2];
			if (c == face2.cellA)
			{
				if (i == 0)
				{
					points.Add(new Point(FacePoints[face2.fpA].Point));
				}
				if (faceInternalPointList != null)
				{
					points.AddRange(faceInternalPointList);
				}
				points.Add(new Point(FacePoints[face2.fpB].Point));
				continue;
			}
			if (i == 0)
			{
				if (face2.fpB == -1)
				{
					continue;
				}
				points.Add(new Point(FacePoints[face2.fpB].Point));
			}
			if (faceInternalPointList != null)
			{
				points.AddRangeReverse(faceInternalPointList);
			}
			if (face2.fpA != -1)
			{
				points.Add(new Point(FacePoints[face2.fpA].Point));
			}
		}
		points.Reverse();
		return points;
	}

	public List<int> CellFacePoints(int cIdx)
	{
		List<int> intList = new List<int>();
		Cell cell = Cells[cIdx];
		Span<int> faces = cell.Faces.Span;
		for (int i = 0; i < faces.Length; i++)
		{
			intList.Add(Faces[faces[i]].GetFPPrev(cIdx));
		}
		return intList;
	}

	public List<Point> PointsOnFace(int f)
	{
		List<Point> points = new List<Point>();
		Face face = Faces[f];
		List<Point> faceInternalPoint = FaceInternalPoints[f];
		points.Add(FacePoints[face.fpA].Point);
		if (faceInternalPoint != null)
		{
			points.AddRange(faceInternalPoint);
		}
		points.Add(FacePoints[face.fpB].Point);
		return points;
	}

	private MeshStatistics GetMeshStatistics()
	{
		return MeshStatistics.ComputeCellStatistics(this);
	}

	private string SerializeMesh()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CELLS: " + CellCount);
		stringBuilder.AppendLine("FACES: " + FaceCount);
		stringBuilder.AppendLine("FACEPOINTS: " + FacePointCount);
		for (int i = 0; i < CellCount; i++)
		{
			Cell cell = Cells[i];
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
			handler.AppendLiteral("C[");
			handler.AppendFormatted(i);
			handler.AppendLiteral("]: F=");
			handler.AppendFormatted(string.Join(',', cell.Faces.ToArray()));
			stringBuilder3.AppendLine(ref handler);
		}
		for (int j = 0; j < FaceCount; j++)
		{
			Face face = Faces[j];
			List<Point> faceInternalPoint = FaceInternalPoints[j];
			string faceInternalPointTerm = "(NONE)";
			if (!faceInternalPoint.IsNullOrEmpty())
			{
				faceInternalPointTerm = string.Join('|', faceInternalPoint);
			}
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(33, 6, stringBuilder2);
			handler.AppendLiteral("F[");
			handler.AppendFormatted(j);
			handler.AppendLiteral("]: CA=");
			handler.AppendFormatted(face.cellA);
			handler.AppendLiteral(" CB=");
			handler.AppendFormatted(face.cellB);
			handler.AppendLiteral(" FPA=");
			handler.AppendFormatted(face.fpA);
			handler.AppendLiteral(" FPB=");
			handler.AppendFormatted(face.fpB);
			handler.AppendLiteral(", INTERNAL:");
			handler.AppendFormatted(faceInternalPointTerm);
			stringBuilder4.AppendLine(ref handler);
		}
		for (int k = 0; k < FacePointCount; k++)
		{
			FacePoint facePoint = FacePoints[k];
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 3, stringBuilder2);
			handler.AppendLiteral("FP[");
			handler.AppendFormatted(k);
			handler.AppendLiteral("]: F=");
			handler.AppendFormatted(string.Join(',', facePoint.Faces));
			handler.AppendLiteral(", PT=");
			handler.AppendFormatted(facePoint.Point);
			stringBuilder5.AppendLine(ref handler);
		}
		return stringBuilder.ToString();
	}
}

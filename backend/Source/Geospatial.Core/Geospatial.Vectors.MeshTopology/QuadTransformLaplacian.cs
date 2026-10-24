using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Transformers;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class QuadTransformLaplacian : QuadTransformBase, ISpatialTransformer
{
	public enum LaplaceBCMethod
	{
		NoFlux = 0,
		LinearSouth = 1,
		LinearEast = 2,
		LinearNorth = 4,
		LinearWest = 8,
		Linear = 15
	}

	public bool ClipInputs = true;

	public double numTinCellsAcross = 7.0;

	private LaplaceBCMethod laplaceBCMethod;

	public static bool UseCubic = true;

	private TinCubicInterpolator _cubicInterpolator;

	public bool IsReversible => true;

	public Tin Tin { get; private set; }

	public void Transform(Memory<Point> points, Memory<Point> transformed)
	{
		int t = 0;
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		for (int i = 0; i < points.Length; i++)
		{
			int t2 = t;
			Point point = pointsSpan[i];
			Point xyPoint;
			if (_cubicInterpolator != null)
			{
				xyPoint = _cubicInterpolator.MapZMtoXY(point, ref t);
			}
			else
			{
				if (ClipInputs)
				{
					point.X = Math.Min(1.0, Math.Max(0.0, point.X));
					point.Y = Math.Min(1.0, Math.Max(0.0, point.Y));
				}
				Tin.FindTriangleContainingZMPoint(point, ref t2);
				if (t2 == -1)
				{
					throw new DynamicMeshException($"Quad transform failed for ({pointsSpan[i].X},{pointsSpan[i].Y}). Patch corners may be ambiguous. Check if patch should be triangles.");
				}
				t = t2;
				Tin.Triangle4D[t2].ComputeXY(pointsSpan[i], out xyPoint);
			}
			if (!xyPoint.IsValid())
			{
				throw new DynamicMeshException($"Quad transform failed for ({pointsSpan[i].X},{pointsSpan[i].Y}). Numerical error.");
			}
			transformedSpan[i] = xyPoint;
		}
	}

	public void ReverseTransform(Memory<Point> points, Memory<Point> transformed)
	{
		int ordinal = 0;
		Span<Point> pointsSpan = points.Span;
		Span<Point> transformedSpan = transformed.Span;
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		for (int i = 0; i < points.Length; i++)
		{
			int t = ordinal;
			Tin.FindTriangleContainingPoint(pointsSpan[i], ref t);
			if (t == -1)
			{
				transformedSpan[i] = new Point(double.NaN, double.NaN);
				continue;
			}
			ordinal = t;
			Triangle4D triangle4D = Tin.Triangle4D[t];
			double Z;
			double M;
			if (triangleDefinition[t].IsDeleted)
			{
				triangle4D.ComputeZMSafe(pointsSpan[i].X, pointsSpan[i].Y, out Z, out M);
			}
			else if (_cubicInterpolator == null)
			{
				triangle4D.ComputeZM(pointsSpan[i], out Z, out M);
			}
			else
			{
				_cubicInterpolator.MapXYtoXMInTriangle(pointsSpan[i], t, out Z, out M);
			}
			transformedSpan[i] = new Point(Z, M);
		}
	}

	public Polyline InterpolateSegment(Segment seg)
	{
		double tolerance = 1E-05;
		Point[] points = new Point[2] { seg.A, seg.B };
		Point[] pointsArray = new Point[2];
		ReverseTransform(points, pointsArray);
		if ((pointsArray[0].X <= tolerance && pointsArray[1].X <= tolerance) || (pointsArray[0].X >= 1.0 - tolerance && pointsArray[1].X >= 1.0 - tolerance) || (pointsArray[0].Y <= tolerance && pointsArray[1].Y <= tolerance) || (pointsArray[0].Y >= 1.0 - tolerance && pointsArray[1].Y >= 1.0 - tolerance))
		{
			return null;
		}
		if (_cubicInterpolator != null)
		{
			return _cubicInterpolator.InterpolateSegment(seg);
		}
		Polyline polyline = new Polyline { seg.A };
		Segment segment = new Segment(pointsArray[0], pointsArray[1]);
		Vector other = segment.AsVector().UnitVector();
		double distanceToSegmentTerm = tolerance * segment.A.DistanceTo(segment.B);
		List<TriangleDefinition> triangleDefinition = Tin.TriangleDefinition;
		int t = -1;
		Tin.FindTriangleContainingZMPoint(segment.A, ref t);
		if (triangleDefinition[t].IsDeleted)
		{
			Tin.FindTriangleContainingPoint(seg.A + 2.0 * tolerance * seg.AsVector().UnitVector(), ref t);
		}
		if (t == -1)
		{
			throw new StructuredException(Message.GenericError("Discretizing quad multipoint, failed to find TIN triangle near {Coordinate} {RegionIndex}.", points[0], regionIdx));
		}
		Segment seg2 = default;
		int edgePointATriangleDefinitionTerm = -1;
		int edgePointBTriangleDefinitionTerm = -1;
		int triangleCount;
		for (triangleCount = Tin.TriangleCount; triangleCount >= 0; triangleCount--)
		{
			double maxValue = double.MaxValue;
			Point a = Point.NullPoint;
			Point nullPoint = Point.NullPoint;
			int ordinal = -1;
			int i;
			for (i = 0; i < 3; i++)
			{
				int edgePointA = triangleDefinition[t].EdgePointA(i);
				int edgePointB = triangleDefinition[t].EdgePointB(i);
				if (edgePointA == edgePointBTriangleDefinitionTerm && edgePointB == edgePointATriangleDefinitionTerm)
				{
					continue;
				}
				triangleDefinition[t].AdjacentTriangleIdx(i);
				seg2.A = new Point(Tin.Z[edgePointA], Tin.M[edgePointA]);
				seg2.B = new Point(Tin.Z[edgePointB], Tin.M[edgePointB]);
				Vector other2 = seg2.AsVector().Perpendicular(clockwise: true);
				if (!(other.Dot(other2) < 0.0))
				{
					segment.IntersectPoint(seg2, out var x, out var y, out var t2, out var t3);
					if (t2 < maxValue)
					{
						maxValue = t2;
						ordinal = i;
						a = new Point(x, y);
						nullPoint = Tin.Points[edgePointA] + t3 * (Tin.Points[edgePointB] - Tin.Points[edgePointA]);
					}
				}
			}
			if (ordinal < 0)
			{
				throw new StructuredException(Message.GenericError("Discretizing quad multipoint, stalled near {Coordinate} {RegionIndex}.", polyline.Last(), regionIdx));
			}
			segment.A = a;
			if (segment.AsVector().Dot(other) <= distanceToSegmentTerm)
			{
				break;
			}
			polyline.Add(nullPoint);
			edgePointATriangleDefinitionTerm = triangleDefinition[t].EdgePointA(i);
			edgePointBTriangleDefinitionTerm = triangleDefinition[t].EdgePointB(i);
			t = triangleDefinition[t].AdjacentTriangleIdx(ordinal);
		}
		if (triangleCount < 0)
		{
			throw new StructuredException(Message.GenericError("Discretizing quad multipoint failed to terminate near {Coordinate} {RegionIndex}.", polyline.Last(), regionIdx));
		}
		polyline.Add(seg.B);
		polyline.InplaceDepupe(tolerance * polyline.Length);
		return polyline;
	}

	public QuadTransformLaplacian(ConceptualMesh mesh, int cIdx, List<PatchSide> sides = null, ProgressReporter cancelReporter = null)
		: base(mesh, cIdx)
	{
		BuildByLaplacian(sides, cancelReporter);
		if (UseCubic)
		{
			_cubicInterpolator = new TinCubicInterpolator(Tin, ClipInputs);
		}
	}

	public QuadTransformLaplacian(ConceptualMesh mesh, int cIdx, Tin tin)
		: base(mesh, cIdx)
	{
		Tin = tin;
		if (UseCubic)
		{
			_cubicInterpolator = new TinCubicInterpolator(Tin, ClipInputs);
		}
	}

	public void BuildByLaplacian(List<PatchSide> sides, ProgressReporter cancelReporter = null)
	{
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.ComputeLaplaceOne);
		if (sides == null)
		{
			SetSides();
			sides = Sides;
		}
		else
		{
			_sides = sides;
		}
		MeshProfiler.Instance.StartTask(MeshProfiler.MeshTask.LaplaceBuildTin);
		float tinResolution = cmesh.CellRegionMetadata[regionIdx].TINResolution;
		if (float.IsNaN(tinResolution) || tinResolution <= 0f)
		{
			tinResolution = 1f;
		}
		else if ((double)tinResolution < 0.05)
		{
			tinResolution = 0.05f;
		}
		tinResolution *= (cmesh.CellRegionMetadata[regionIdx].Multipoint ? 3f : 1.5f);
		ConceptualMesh conceptualMesh = SidesToConceptualMesh(out var sizeFunction, sides, out var sidesOut, tinResolution);
		SetLaplaceBCMethod(sides);
		TrianglePaver trianglePaver = new TrianglePaver(conceptualMesh, 0)
		{
			sizeFunction = sizeFunction,
			expandMBR = 5.0
		};
		trianglePaver.Generate();
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.LaplaceBuildTin);
		if (cancelReporter != null && cancelReporter.CancelRequested)
		{
			throw new OperationCanceledException();
		}
		Tin = trianglePaver.DTin.Tin;
		TinLaplacian laplace = new TinLaplacian(Tin);
		BuildAndSolveLaplacian(laplace, conceptualMesh, sidesOut, out var nodeValuesWE, out var nodeValuesSN, cancelReporter);
		Tin.TrackZ(nodeValuesWE.AsSpan());
		Tin.TrackM(nodeValuesSN.AsSpan());
		result = trianglePaver.result;
		MeshProfiler.Instance.StopTask(MeshProfiler.MeshTask.ComputeLaplaceOne);
	}

	private static double AverageAngles180(double a, double b)
	{
		return a + 0.5 * ((b - a + 7.853981633974483) % Math.PI - Math.PI / 2.0);
	}

	private void SetLaplaceBCMethod(List<PatchSide> sides)
	{
		for (int i = 0; i < 4; i++)
		{
			if (SideBCIsDirichlet(sides[i]))
			{
				switch (i)
				{
				case 0:
					laplaceBCMethod |= LaplaceBCMethod.LinearSouth;
					break;
				case 1:
					laplaceBCMethod |= LaplaceBCMethod.LinearEast;
					break;
				case 2:
					laplaceBCMethod |= LaplaceBCMethod.LinearNorth;
					break;
				case 3:
					laplaceBCMethod |= LaplaceBCMethod.LinearWest;
					break;
				}
			}
		}
	}

	private void BuildAndSolveLaplacian(TinLaplacian laplace, ConceptualMesh localMesh, List<PatchSide> localSides, out double[] nodeValuesWE, out double[] nodeValuesSN, ProgressReporter cancelReporter = null)
	{
		Tin tin = laplace.Tin;
		nodeValuesWE = new double[tin.Count];
		nodeValuesSN = new double[tin.Count];
		Dictionary<int, double> gradients = null;
		Dictionary<int, double> gradients2 = null;
		for (int i = 0; i < tin.Count; i++)
		{
			nodeValuesWE[i] = double.NaN;
			nodeValuesSN[i] = double.NaN;
		}
		SetSideValues(tin, localMesh, localSides, 3, nodeValuesWE, 0.0, 0.0, ref gradients);
		SetSideValues(tin, localMesh, localSides, 1, nodeValuesWE, 1.0, 1.0, ref gradients);
		if ((laplaceBCMethod & LaplaceBCMethod.LinearSouth) == 0)
		{
			SetSideValues(tin, localMesh, localSides, 0, nodeValuesWE, double.NaN, double.NaN, ref gradients);
		}
		else
		{
			SetSideValues(tin, localMesh, localSides, 0, nodeValuesWE, 0.0, 1.0, ref gradients);
		}
		if ((laplaceBCMethod & LaplaceBCMethod.LinearNorth) == 0)
		{
			SetSideValues(tin, localMesh, localSides, 2, nodeValuesWE, double.NaN, double.NaN, ref gradients);
		}
		else
		{
			SetSideValues(tin, localMesh, localSides, 2, nodeValuesWE, 1.0, 0.0, ref gradients);
		}
		SetSideValues(tin, localMesh, localSides, 0, nodeValuesSN, 0.0, 0.0, ref gradients2);
		SetSideValues(tin, localMesh, localSides, 2, nodeValuesSN, 1.0, 1.0, ref gradients2);
		if ((laplaceBCMethod & LaplaceBCMethod.LinearWest) == 0)
		{
			SetSideValues(tin, localMesh, localSides, 3, nodeValuesSN, double.NaN, double.NaN, ref gradients2);
		}
		else
		{
			SetSideValues(tin, localMesh, localSides, 3, nodeValuesSN, 1.0, 0.0, ref gradients2);
		}
		if ((laplaceBCMethod & LaplaceBCMethod.LinearEast) == 0)
		{
			SetSideValues(tin, localMesh, localSides, 1, nodeValuesSN, double.NaN, double.NaN, ref gradients2);
		}
		else
		{
			SetSideValues(tin, localMesh, localSides, 1, nodeValuesSN, 0.0, 1.0, ref gradients2);
		}
		if (cancelReporter != null && cancelReporter.CancelRequested)
		{
			throw new OperationCanceledException();
		}
		laplace.SolveLaplacian(nodeValuesWE, gradients, cancelReporter);
		CheckZeroGradient(nodeValuesWE);
		laplace.SolveLaplacian(nodeValuesSN, gradients2, cancelReporter);
		CheckZeroGradient(nodeValuesWE);
		void CheckZeroGradient(double[] nodeValues)
		{
			for (int j = 0; j < tin.TriangleCount; j++)
			{
				TriangleDefinition triangleDefinition = tin.TriangleDefinition[j];
				double nodeValue = nodeValues[triangleDefinition.p0];
				if (nodeValue != 0.0 && nodeValue != 1.0 && !triangleDefinition.IsDeleted && nodeValue == nodeValues[triangleDefinition.p1] && nodeValue == nodeValues[triangleDefinition.p2])
				{
					throw new DynamicMeshException("Quad transform failed. Patch corners may be ambiguous. Decrease cell size, subdivide quad patch, check position of nodes, or use triangles");
				}
			}
		}
	}

	public void SetSideValues(Tin tin, ConceptualMesh localMesh, List<PatchSide> localSides, int sideIdx, double[] values, double dirichletValueA, double dirichletValueB, ref Dictionary<int, double> gradients)
	{
		PatchSide patchSide = localSides[sideIdx];
		int t = 0;
		double minXExtentTerm = Math.Max(Math.Max(Math.Abs(tin.Extent.MinX), Math.Abs(tin.Extent.MaxX)), Math.Max(Math.Abs(tin.Extent.MinY), Math.Abs(tin.Extent.MaxY)));
		double nearTolerance = 1E-15 * minXExtentTerm;
		HalfEdge halfEdge = patchSide.SidePieces[0];
		double faceHalfArcAngle = localMesh.FaceHalfArcAngle[halfEdge.fIdx][halfEdge.orient];
		if (double.IsNaN(dirichletValueA) && faceHalfArcAngle == 0.0)
		{
			return;
		}
		Polyline nodes = patchSide.Nodes;
		if (faceHalfArcAngle == 0.0)
		{
			double length = nodes.Length;
			double distanceToSum = 0.0;
			for (int i = 0; i < nodes.Count; i++)
			{
				if (i > 0)
				{
					distanceToSum += nodes[i - 1].DistanceTo(nodes[i]);
				}
				double distanceToSumOverLength = distanceToSum / length;
				tin.FindTriangleCheckNearbyCorners(nodes[i], nearTolerance, ref t, out var p);
				values[p] = (1.0 - distanceToSumOverLength) * dirichletValueA + distanceToSumOverLength * dirichletValueB;
			}
			return;
		}
		if (gradients == null)
		{
			gradients = new Dictionary<int, double>();
		}
		double scaledPi = (double.IsNaN(dirichletValueA) ? 0.0 : (Math.PI / 2.0));
		for (int j = 1; j < nodes.Count - 1; j++)
		{
			tin.FindTriangleCheckNearbyCorners(nodes[j], nearTolerance, ref t, out var p2);
			double gradients2 = (nodes[j + 1] - nodes[j - 1]).MathBearing + scaledPi - faceHalfArcAngle;
			gradients[p2] = gradients2;
		}
		if (double.IsFinite(dirichletValueA))
		{
			if (faceHalfArcAngle > 0.0)
			{
				tin.FindTriangleCheckNearbyCorners(nodes.First(), nearTolerance, ref t, out var p3);
				values[p3] = dirichletValueA;
				double a = nodes.Segments.Last().Bearing + scaledPi - faceHalfArcAngle;
				PatchSide patchSide2 = localSides[(sideIdx + 1) % 4];
				HalfEdge halfEdge2 = patchSide2.SidePieces[0];
				float faceHalfArcAngle2 = localMesh.FaceHalfArcAngle[halfEdge2.fIdx][halfEdge2.orient];
				double b = patchSide2.Nodes.Segments.First().Bearing + scaledPi + Math.PI / 2.0 - (double)faceHalfArcAngle2;
				tin.FindTriangleCheckNearbyCorners(nodes.Last(), nearTolerance, ref t, out p3);
				gradients[p3] = AverageAngles180(a, b);
			}
			else
			{
				tin.FindTriangleCheckNearbyCorners(nodes.Last(), nearTolerance, ref t, out var p4);
				values[p4] = dirichletValueA;
				double a2 = nodes.Segments.First().Bearing + scaledPi - faceHalfArcAngle;
				PatchSide patchSide3 = localSides[(sideIdx + 3) % 4];
				HalfEdge halfEdge3 = patchSide3.SidePieces.Last();
				float faceHalfArcAngle3 = localMesh.FaceHalfArcAngle[halfEdge3.fIdx][halfEdge3.orient];
				double b2 = patchSide3.Nodes.Segments.Last().Bearing + scaledPi - Math.PI / 2.0 - (double)faceHalfArcAngle3;
				tin.FindTriangleCheckNearbyCorners(nodes.First(), nearTolerance, ref t, out p4);
				gradients[p4] = AverageAngles180(a2, b2);
			}
		}
	}

	public void DumpToJson(Segment segXY, Segment segZM, Polyline result = null)
	{
		List<List<object>> objectListList = new List<List<object>>
		{
			new List<object>
			{
				"PYTHON",
				$"axs[0].plot([{segXY.A.X},{segXY.B.X}],[{segXY.A.Y},{segXY.B.Y}],'r-o',lw=2)"
			},
			new List<object>
			{
				"PYTHON",
				$"axs[1].plot([{segZM.A.X},{segZM.B.X}],[{segZM.A.Y},{segZM.B.Y}],'r-o',lw=2)"
			}
		};
		if (result != null)
		{
			objectListList.Add(new List<object>
			{
				"SET",
				"result_raw",
				DynamicMesh.SerializePolyline(result)
			});
			objectListList.Add(new List<object> { "PYTHON", "result=np.array(result_raw)\r\naxs[0].plot(result[:,0], result[:,1],'b-o')" });
		}
		DumpToJson("C:\\Temp\\cmd.json", objectListList);
	}

	public void DumpToJson(string cmdFile = "C:\\Temp\\cmd.json", List<List<object>> post = null)
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
			new List<object> { "SET", "dt_raw", Tin },
			new List<object> { "PYTHON", "\r\ndt=json_to_mesh(dt_raw)\r\n# generic contour plotting\r\nfor c in np.nonzero(dt.cells['state']==1)[0]:\r\n    dt.delete_cell(c)\r\ndt.delete_naked_edges()\r\n# ad-hoc normalization of contours:\r\ndZ = np.abs(dt.nodes['Z'][dt.edges['nodes'][:,0]] - dt.nodes['Z'][dt.edges['nodes'][:,1]]).mean()\r\ndM = np.abs(dt.nodes['M'][dt.edges['nodes'][:,0]] - dt.nodes['M'][dt.edges['nodes'][:,1]]).mean()\r\nfig,axs=plt.subplots(2,1,num=1,clear=1)\r\nfor ax in axs:\r\n    ax.set_adjustable('datalim')\r\nax=axs[0]\r\ndt.plot_edges(ax=ax,color='k',lw=0.5)\r\ndt.contour_node_values( dt.nodes['Z'], np.linspace(0.0001,0.9999,int(100*dM/(dZ+dM))), colors='b', ax=ax)\r\ndt.contour_node_values( dt.nodes['M'], np.linspace(0.0001,0.9999,int(100*dZ/(dZ+dM))), colors='orange', ax=ax)\r\nax.axis('equal')\r\n\r\nax=axs[1]\r\ndt2=dt.copy()\r\ndt2.nodes['x'] = np.c_[ dt.nodes['Z'], dt.nodes['M']]\r\ndt2.cells['_center'] = dt2.cells_centroid(method='shapely')\r\ndt2.plot_edges(ax=ax,color='k',lw=0.5)\r\nax.set_aspect(0.1)\r\n" }
		};
		if (post != null)
		{
			objectList.AddRange(post);
		}
		JsonSerializer.Serialize(utf8Json, objectList, options);
	}
}

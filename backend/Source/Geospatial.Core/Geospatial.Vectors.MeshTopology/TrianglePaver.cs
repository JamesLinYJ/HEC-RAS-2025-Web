using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class TrianglePaver : IRegionGenerator
{
	private struct ActiveTriangle
	{
		public int p0;

		public int p1;

		public int p2;

		public int idx;
	}

	public enum TriState
	{
		UNKNOWN,
		EXTERNAL,
		INTERNAL,
		ACTIVE,
		WAITING,
		ACCEPTED
	}

	public static class MessageCodes
	{
		public static readonly MessageClass PaverError = new MessageClass("TrianglePaver_PaverError", MessageSeverity.Error, "Error building triangular mesh near {Coordinate}]). \"{InnerMessage}\"");

		public static Message GetPaverError(Point p, string innerMsg)
		{
			return PaverError.Create(p, innerMsg);
		}
	}

	public SlidingMesh result;

	public DynamicTINFactory<NativePredicates> DTin;

	public ConceptualMesh cmesh;

	public HashSet<int> islandArcs;

	public int regionIdx;

	public List<int> cmeshBreakFaces;

	private PriorityQueue<ActiveTriangle, double> activeQueue;

	public ISizeFunction sizeFunction;

	public ISizeFunction targetSizeFunction;

	private bool eagerAcceptance = true;

	public Dictionary<int, TriState> TriStates;

	public double deltaAccept = 1.3;

	public float stretchRate = 0.2f;

	public double expandMBR;

	private int loop;

	public double BiasRebay
	{
		get
		{
			if (!((double)stretchRate > 0.0))
			{
				return 0.0;
			}
			return Math.Max(0.1, 1.1 * (double)stretchRate);
		}
	}

	public TrianglePaver(ConceptualMesh mesh, int cIdx)
	{
		cmesh = mesh;
		regionIdx = cIdx;
	}

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		DTin = InitializeTin(reporter.WithProgressModifier(0, 0.5f));
		if (reporter.CancelRequested)
		{
			return null;
		}
		InitializePointScale(DTin);
		reporter.ReportProgress(70);
		DoRebay(reporter.WithProgressModifier(70, 0.2f));
		if (reporter.CancelRequested)
		{
			return null;
		}
		reporter.ReportProgress(90);
		result = TinToSlidingMesh(reporter.WithProgressModifier(90, 0.1f));
		if (reporter.CancelRequested)
		{
			return null;
		}
		return result;
	}

	public DynamicTINFactory<NativePredicates> InitializeTin(ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		List<Polygon> cellNodeComplexRings = cmesh.CellNodeComplexRings(regionIdx, out islandArcs);
		TriangulationParameters tparams = null;
		if (expandMBR > 1.0)
		{
			tparams = new TriangulationParameters
			{
				EnforcedMBR = cellNodeComplexRings[0].Extent.RatioAroundCenter(expandMBR)
			};
		}
		DynamicTINFactory<NativePredicates> dynamicTINFactory = new DynamicTINFactory<NativePredicates>(tparams);
		foreach (Polygon cellNodeComplexRing in cellNodeComplexRings)
		{
			dynamicTINFactory.AddPerimeter(cellNodeComplexRing);
		}
		cmeshBreakFaces = cmesh.CellBreaklines(regionIdx);
		List<Polyline> breaklines = cmeshBreakFaces.Select((int fIdx) => cmesh.FaceNodeString[fIdx]).ToList();
		dynamicTINFactory.AddBreaklines(breaklines);
		dynamicTINFactory.ComputeTIN(reporter);
		return dynamicTINFactory;
	}

	public ISizeFunction GetRegionSizeFunction(bool secondary, DynamicTINFactory<NativePredicates> dynTin = null)
	{
		if (dynTin == null)
		{
			dynTin = InitializeTin();
		}
		InitializePointScale(dynTin);
		Tin tin = dynTin.Tin;
		int pointCount = tin.PointCount;
		if ((double)stretchRate > 0.0)
		{
			double target = double.NaN;
			if (targetSizeFunction is ConstantSizeFunction constantSizeFunction)
			{
				target = constantSizeFunction.size;
			}
			dynTin.AddInteriorFill(0.25, target);
		}
		else
		{
			dynTin.AddInteriorFill(0.25);
		}
		TinSizeFunction.NodeState[] nodeStates = new TinSizeFunction.NodeState[tin.PointCount];
		int i;
		for (i = 0; i < 8; i++)
		{
			nodeStates[i] = TinSizeFunction.NodeState.FREE;
		}
		for (; i < pointCount; i++)
		{
			nodeStates[i] = TinSizeFunction.NodeState.FIXED;
		}
		for (; i < tin.PointCount; i++)
		{
			nodeStates[i] = TinSizeFunction.NodeState.FREE;
		}
		return new TinSizeFunction(tin, stretchRate, nodeStates);
	}

	private void InitializePointScale(DynamicTINFactory<NativePredicates> dynTin)
	{
		Dictionary<int, Tuple<int, double>> valueTupleByInt = new Dictionary<int, Tuple<int, double>>();
		foreach (var constrainedEdge in dynTin.ConstrainedEdges())
		{
			double distanceTo = dynTin.Tin[constrainedEdge.Item1].DistanceTo(dynTin.Tin[constrainedEdge.Item2]);
			foreach (int ordinal in new List<int> { constrainedEdge.Item1, constrainedEdge.Item2 })
			{
				if (!valueTupleByInt.ContainsKey(ordinal))
				{
					valueTupleByInt[ordinal] = new Tuple<int, double>(0, 0.0);
				}
				Tuple<int, double> tuple = valueTupleByInt[ordinal];
				valueTupleByInt[ordinal] = new Tuple<int, double>(tuple.Item1 + 1, tuple.Item2 + distanceTo);
			}
		}
		dynTin.Tin.TrackZ();
		foreach (int key in valueTupleByInt.Keys)
		{
			Tuple<int, double> tuple2 = valueTupleByInt[key];
			double tuple2Term = tuple2.Item2 / (double)tuple2.Item1;
			dynTin.Tin.Z[key] = tuple2Term;
		}
	}

	public SlidingMesh TinToSlidingMesh(ProgressReporter reporter = null)
	{
		SlidingMesh mesh = new SlidingMesh();
		DTin.CopyToDynMesh(mesh, copyDeleted: false, reporter);
		reporter.ReportProgress(90);
		ConstrainMesh(mesh);
		reporter.ReportProgress(95);
		AnnotateScale(mesh);
		reporter.ReportProgress(100);
		return mesh;
	}

	public void ConstrainMesh(SlidingMesh mesh)
	{
		List<TriangleDefinition> triangleDefinition = DTin.Tin.TriangleDefinition;
		int i;
		for (i = 0; i < triangleDefinition.Count && triangleDefinition[i].IsDeleted; i++)
		{
		}
		double minXExtentTerm = Math.Max(Math.Max(Math.Abs(DTin.Tin.Extent.MinX), Math.Abs(DTin.Tin.Extent.MaxX)), Math.Max(Math.Abs(DTin.Tin.Extent.MinY), Math.Abs(DTin.Tin.Extent.MaxY)));
		double tolerance = 1E-15 * minXExtentTerm;
		ConstrainToCell(mesh, regionIdx, tolerance, ref i);
		foreach (int islandArc in islandArcs)
		{
			ConstrainToFace(mesh, islandArc, tolerance, ref i);
		}
		foreach (int cmeshBreakFace in cmeshBreakFaces)
		{
			ConstrainToFace(mesh, cmeshBreakFace, tolerance, ref i);
		}
	}

	public void AnnotateScale(SlidingMesh mesh)
	{
		for (int i = 0; i < mesh.FaceCount; i++)
		{
			if (!mesh.FaceDeleted[i])
			{
				mesh.FaceTargetLength[i] = (float)sizeFunction.Query(mesh.FaceMidPoint(i));
			}
		}
	}

	public void ConstrainToCell(SlidingMesh mesh, int cIdx, double tolerance, ref int t)
	{
		foreach (HalfEdge cellFace in cmesh.CellFaces[cIdx])
		{
			ConstrainToFace(mesh, cellFace.fIdx, tolerance, ref t);
		}
	}

	public void ConstrainToFace(SlidingMesh mesh, int arcIdx, double tolerance, ref int t)
	{
		Polyline polyline = cmesh.FaceNodeString[arcIdx];
		Polyline polyline2 = cmesh.FaceSmoothLine[arcIdx];
		ConstraintState constraintState = (ConstraintState)mesh.constraints.Add(polyline2, arcIdx, cmesh.ArcRigidity(arcIdx));
		int n1Idx = -1;
		for (int i = 0; i < polyline.Count; i++)
		{
			DTin.Tin.FindTriangleCheckNearbyCorners(polyline[i], tolerance, ref t, out var p);
			if (i == 0 || i == polyline.Count - 1)
			{
				mesh.NodeConstraints[p] = ConstraintState.FIXED;
			}
			else
			{
				mesh.NodeConstraints[p] = constraintState;
				mesh.NodePointOnConstraint[p] = polyline2.NearestPointOnPolyline(mesh.NodePoints[p]);
			}
			if (i > 0)
			{
				HalfEdge halfEdge = mesh.NodesToHalfEdge(n1Idx, p);
				if (!halfEdge.IsNull())
				{
					mesh.FaceConstraints[halfEdge.fIdx] = constraintState;
				}
			}
			n1Idx = p;
		}
	}

	public void DoRebay(ProgressReporter reporter = null)
	{
		activeQueue = new PriorityQueue<ActiveTriangle, double>();
		LabelInitial();
		List<TriangleDefinition> triangleDefinition = DTin.Tin.TriangleDefinition;
		for (loop = 0; reporter == null || !reporter.CancelRequested; loop++)
		{
			int selectNextTriangle = SelectNextTriangle();
			if (selectNextTriangle < 0)
			{
				break;
			}
			if (TriangleAcceptable(selectNextTriangle))
			{
				MarkAccepted(selectNextTriangle, checkAcceptable: false);
				continue;
			}
			Segment segment = Segment.NullSegment;
			double maxValue = double.MaxValue;
			bool triFaceIsConstrained = false;
			for (int i = 0; i < 3; i++)
			{
				int key = triangleDefinition[selectNextTriangle].AdjacentTriangleIdx(i);
				if (TriStates[key] == TriState.EXTERNAL || TriStates[key] == TriState.ACCEPTED)
				{
					Segment segment2 = DTin.Tin.Edge(selectNextTriangle, i);
					bool triFaceIsConstrained2 = DTin.TriFaceIsConstrained(selectNextTriangle, i);
					if ((triFaceIsConstrained2 == triFaceIsConstrained && segment2.Length < maxValue) | triFaceIsConstrained2)
					{
						maxValue = segment2.Length;
						segment = segment2;
						triFaceIsConstrained = triFaceIsConstrained2;
					}
				}
			}
			double scaledLength = 0.5 * segment.Length;
			Point point = segment.Midpoint();
			double targetRadius = TargetRadius(point, selectNextTriangle);
			double radius = targetRadius;
			targetRadius = Math.Max(targetRadius, scaledLength);
			Point point2 = DTin.Tin.Triangles[selectNextTriangle].CircumCircleCenter();
			Vector vector = segment.AsVector().Perpendicular(clockwise: false).UnitVector();
			double dot = (point2 - point).Dot(vector);
			if (dot <= 0.0)
			{
				MarkAccepted(selectNextTriangle, checkAcceptable: false);
				continue;
			}
			double targetRadiusTerm = targetRadius + Math.Sqrt(targetRadius * targetRadius - scaledLength * scaledLength);
			Point point3;
			if (targetRadiusTerm >= 1.33 * dot)
			{
				if (triFaceIsConstrained)
				{
					MarkAccepted(selectNextTriangle, checkAcceptable: false);
					continue;
				}
				point3 = point2;
			}
			else
			{
				point3 = point + targetRadiusTerm * vector;
			}
			int t = selectNextTriangle;
			DTin.Tin.FindTriangleContainingPoint(point3, ref t);
			if (t < 0 || (TriStates[t] != TriState.ACTIVE && TriStates[t] != TriState.WAITING))
			{
				MarkAccepted(selectNextTriangle, checkAcceptable: false);
				continue;
			}
			bool condition = false;
			int selectNextTriangle2 = selectNextTriangle;
			int selectNextTriangle3 = selectNextTriangle;
			Segment segment3 = new Segment(point, point3);
			int ordinal = 100;
			while (ordinal > 0 && selectNextTriangle2 >= 0)
			{
				for (int j = 0; j < 3; j++)
				{
					int key2 = triangleDefinition[selectNextTriangle2].AdjacentTriangleIdx(j);
					if ((DTin.TriFaceIsConstrained(selectNextTriangle2, j) || TriStates[key2] == TriState.EXTERNAL || TriStates[key2] == TriState.ACCEPTED) && DTin.Tin.Edge(selectNextTriangle2, j).PerpendicularSignedDistance(point3) < 0.1 * targetRadius)
					{
						condition = true;
						break;
					}
				}
				if (selectNextTriangle2 == t)
				{
					break;
				}
				EdgeDefinition edgeDefinition = EdgeDefinition.NullEdgeDefinition();
				for (int k = 0; k < 3; k++)
				{
					EdgeDefinition edgeDefinition2 = DTin.Tin.GetEdgeDefinition(selectNextTriangle2, k);
					int otherTriangle = edgeDefinition2.OtherTriangle(selectNextTriangle2);
					if (otherTriangle != selectNextTriangle3)
					{
						Segment edgeSegment = DTin.Tin.GetEdgeSegment(edgeDefinition2);
						if (segment3.AsVector().CrossProduct(edgeSegment.AsVector()) >= 0.0 && segment3.Intersects(edgeSegment))
						{
							edgeDefinition = edgeDefinition2;
							selectNextTriangle3 = selectNextTriangle2;
							selectNextTriangle2 = otherTriangle;
							break;
						}
					}
				}
				if (edgeDefinition.IsNull())
				{
					throw new StructuredException(MessageCodes.GetPaverError(point, "Geometry invariant error in TrianglePaver. Check cell sizes"));
				}
				ordinal--;
			}
			if (ordinal == 0)
			{
				throw new StructuredException(MessageCodes.GetPaverError(point, "Could not traverse candidate triangles. Internal Error. Check cell sizes"));
			}
			if (condition)
			{
				MarkAccepted(selectNextTriangle, checkAcceptable: false);
				continue;
			}
			InsertAndUpdateMarks(point3, t, radius);
			_ = 0;
		}
	}

	public void DumpRebayState()
	{
		using FileStream utf8Json = File.Create("C:\\Temp\\cmd.json");
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		List<object> localValue = new List<object>
		{
			new List<object> { "SET", "dt_raw", DTin.Tin },
			new List<object> { "SET", "dt_state", TriStates },
			new List<object> { "PYTHON", "\r\ndt=json_to_mesh(dt_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\nstate=[dt_state.get(str(i),0) for i in range(dt.Ncells())]\r\ndt.plot_cells(ax=ax,values=state,zorder=-2,alpha=0.3,clim=[0,5])\r\ndt.plot_edges(ax=ax,color='k',lw=0.5)\r\nax.axis('equal')\r\n" }
		};
		JsonSerializer.Serialize(utf8Json, localValue, options);
	}

	private double TargetRadius(Point p, int triIdx)
	{
		double query = sizeFunction.Query(p);
		TriangleDefinition triangleDefinition = DTin.Tin.TriangleDefinition[triIdx];
		double computed = 0.0;
		double maxValue = double.MaxValue;
		for (int i = 0; i < 3; i++)
		{
			int index = triangleDefinition.AdjacentPointIdx(i);
			double tinDTinTerm = DTin.Tin.Z[index];
			if (double.IsFinite(tinDTinTerm))
			{
				double biasRebayTerm = 1.0 / (1.0 + BiasRebay);
				double distanceToTinTerm = p.DistanceTo(DTin.Tin[index]) * (1.0 - biasRebayTerm);
				computed = Math.Max(computed, tinDTinTerm - distanceToTinTerm);
				maxValue = Math.Min(maxValue, tinDTinTerm + distanceToTinTerm);
			}
		}
		if (!(query >= computed) || !(query <= maxValue))
		{
			if (computed > maxValue)
			{
				query = Math.Sqrt(computed * maxValue);
			}
			else
			{
				query = Math.Min(query, maxValue);
				query = Math.Max(query, computed);
			}
		}
		return query / Math.Sqrt(3.0);
	}

	public bool TriangleAcceptable(int t, double targetRadius = 0.0)
	{
		if (deltaAccept < 0.0)
		{
			return true;
		}
		Triangle triangle = DTin.Tin.Triangles[t];
		double constrainedRadius = DTin.ConstrainedRadius(t);
		if (targetRadius <= 0.0)
		{
			targetRadius = TargetRadius(triangle.Centroid(), t);
		}
		return constrainedRadius < deltaAccept * targetRadius;
	}

	private bool ValidCandidate(ActiveTriangle candidate)
	{
		List<TriangleDefinition> triangleDefinition = DTin.Tin.TriangleDefinition;
		if (TriStates.TryGetValue(candidate.idx, out var localValue) && localValue == TriState.ACTIVE && triangleDefinition[candidate.idx].p0 == candidate.p0 && triangleDefinition[candidate.idx].p1 == candidate.p1)
		{
			return triangleDefinition[candidate.idx].p2 == candidate.p2;
		}
		return false;
	}

	private void CheckQueue()
	{
		HashSet<int> intSet = new HashSet<int>();
		foreach (var unorderedItem in activeQueue.UnorderedItems)
		{
			if (ValidCandidate(unorderedItem.Element))
			{
				intSet.Add(unorderedItem.Element.idx);
			}
		}
		int ordinal = 0;
		foreach (KeyValuePair<int, TriState> triState in TriStates)
		{
			if (triState.Value == TriState.ACTIVE)
			{
				ordinal++;
			}
		}
	}

	public int SelectNextTriangle()
	{
		int idxCandidateTerm = -1;
		while (activeQueue.Count > 0)
		{
			ActiveTriangle candidate = activeQueue.Peek();
			if (!ValidCandidate(candidate))
			{
				activeQueue.Dequeue();
				continue;
			}
			idxCandidateTerm = candidate.idx;
			break;
		}
		return idxCandidateTerm;
	}

	public void LabelInitial()
	{
		TriStates = new Dictionary<int, TriState>();
		List<TriangleDefinition> triangleDefinition = DTin.Tin.TriangleDefinition;
		for (int i = 0; i < triangleDefinition.Count; i++)
		{
			TriangleDefinition triangleDefinition2 = triangleDefinition[i];
			if (triangleDefinition2.IsDeleted)
			{
				TriStates[i] = TriState.EXTERNAL;
			}
			else if (triangleDefinition[triangleDefinition2.at0].IsDeleted || triangleDefinition[triangleDefinition2.at1].IsDeleted || triangleDefinition[triangleDefinition2.at2].IsDeleted)
			{
				MarkActive(i);
			}
			else
			{
				TriStates[i] = TriState.WAITING;
			}
		}
	}

	public void MarkActive(int triIdx)
	{
		TriStates[triIdx] = TriState.ACTIVE;
		ActiveTriangle element = default;
		TriangleDefinition triangleDefinition = DTin.Tin.TriangleDefinition[triIdx];
		element.p0 = triangleDefinition.p0;
		element.p1 = triangleDefinition.p1;
		element.p2 = triangleDefinition.p2;
		element.idx = triIdx;
		double constrainedRadius = DTin.ConstrainedRadius(triIdx);
		activeQueue.Enqueue(element, 0.0 - constrainedRadius);
	}

	public void MarkAccepted(int triIdx, bool checkAcceptable = true, double targetRadius = 0.0)
	{
		TriangleDefinition triangleDefinition = DTin.Tin.TriangleDefinition[triIdx];
		if (checkAcceptable && !TriangleAcceptable(triIdx, targetRadius))
		{
			throw new DynamicMeshException("Triangle was not acceptable");
		}
		TriStates[triIdx] = TriState.ACCEPTED;
		for (int i = 0; i < 3; i++)
		{
			int adjacentTriangleIdx = triangleDefinition.AdjacentTriangleIdx(i);
			if (!TriStates.TryGetValue(adjacentTriangleIdx, out var localValue) || (localValue != TriState.ACTIVE && localValue != TriState.ACCEPTED && localValue != TriState.EXTERNAL))
			{
				if (TriangleAcceptable(adjacentTriangleIdx))
				{
					MarkAccepted(adjacentTriangleIdx);
				}
				else
				{
					MarkActive(adjacentTriangleIdx);
				}
			}
		}
	}

	public int InsertAndUpdateMarks(Point xNew, int triIdx, double radius)
	{
		bool dynInsertPoint = DTin.DynInsertPoint(xNew, ref triIdx, translatePoint: false, out var p);
		DTin.Tin.Z[p] = radius * Math.Sqrt(3.0);
		if (!dynInsertPoint)
		{
			throw new Exception("TIN insertion got unexpected duplicate node");
		}
		foreach (int dynDeletedTriangle in DTin.dynDeletedTriangles)
		{
			TriStates.Remove(dynDeletedTriangle);
		}
		List<TriangleDefinition> triangleDefinition = DTin.Tin.TriangleDefinition;
		foreach (int dynCreatedTriangle in DTin.dynCreatedTriangles)
		{
			TriangleDefinition triangleDefinition2 = triangleDefinition[dynCreatedTriangle];
			TriStates[dynCreatedTriangle] = TriState.WAITING;
			for (int i = 0; i < 3; i++)
			{
				int adjacentTriangleIdx = triangleDefinition2.AdjacentTriangleIdx(i);
				if (DTin.dynCreatedTriangles.Contains(adjacentTriangleIdx))
				{
					continue;
				}
				if (TriStates[adjacentTriangleIdx] == TriState.EXTERNAL || TriStates[adjacentTriangleIdx] == TriState.ACCEPTED)
				{
					MarkActive(dynCreatedTriangle);
				}
				else
				{
					if (TriStates[adjacentTriangleIdx] != TriState.ACTIVE)
					{
						continue;
					}
					bool condition = true;
					TriangleDefinition triangleDefinition3 = triangleDefinition[adjacentTriangleIdx];
					for (int j = 0; j < 3; j++)
					{
						int adjacentTriangleIdx2 = triangleDefinition3.AdjacentTriangleIdx(j);
						if (!DTin.dynCreatedTriangles.Contains(adjacentTriangleIdx2) && (TriStates[adjacentTriangleIdx2] == TriState.ACCEPTED || TriStates[adjacentTriangleIdx2] == TriState.EXTERNAL))
						{
							condition = false;
							break;
						}
					}
					if (condition)
					{
						TriStates[adjacentTriangleIdx] = TriState.WAITING;
					}
				}
			}
		}
		if (eagerAcceptance)
		{
			foreach (int dynCreatedTriangle2 in DTin.dynCreatedTriangles)
			{
				if (TriStates[dynCreatedTriangle2] == TriState.ACTIVE && TriangleAcceptable(dynCreatedTriangle2))
				{
					MarkAccepted(dynCreatedTriangle2);
				}
			}
		}
		return p;
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.DataStructures;
using Geospatial.Vectors.Topology;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

[JsonConverter(typeof(ConceptualMeshJsonConverter))]
public class ConceptualMesh : DynamicMesh
{
	public class ConceptualMeshJsonConverter : JsonConverter<ConceptualMesh>
	{
		public override ConceptualMesh Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, ConceptualMesh obj, JsonSerializerOptions options)
		{
			string json = JsonSerializer.Serialize((object)obj.SimpleRepr(), options);
			writer.WriteRawValue(json);
		}
	}

	public new static class MessageCodes
	{
		public static readonly MessageClass VisitedFaceTwice = new MessageClass("ConceptualMesh_VisitedFaceTwiceAroundRing", MessageSeverity.Error, "Visited face {FaceIndex} twice, near ({Coordinate}).");

		public static readonly MessageClass DiscretizationFailed = new MessageClass("ConceptualMesh_DiscretizationFailed", MessageSeverity.Error, "Discretization failed near ({Coordinate}).");

		public static readonly MessageClass QuadRepeatedCorner = new MessageClass("ConceptualMesh_QuadRepeatedNode", MessageSeverity.Error, "Quad patch {ControlPointIndex} (Internal FID {RegionIndex}) has a repeated node along boundary {NodeIndex}, cannot solve Laplacian");

		public static readonly MessageClass DiscretizationWarned = new MessageClass("ConceptualMesh_DiscretizationWarned", MessageSeverity.Warning, "Discretization may be invalid near ({Coordinate}).");

		public static readonly MessageClass DiscretizationConflictUnrepaired = new MessageClass("ConceptualMesh_DiscretizationConflictUnrepaired", MessageSeverity.Warning, "Could not separate the discretization of Arc[{ArcIndex}] from a crossing arc near ({Coordinate}). A smaller cell size may fix the issue.");

		public static readonly MessageClass ArcDiscretizationFailed = new MessageClass("ConceptualMesh_ArcDiscretizationFailed", MessageSeverity.Error, "Discretization failed on Arc[{ArcIndex}], near ({Coordinate}). A smaller cell size may fix the issue.");

		public static readonly MessageClass InvalidRegion = new MessageClass("ConceptualMesh_InvalidRegion", MessageSeverity.Error, "Discretized region {ControlPointIndex} is invalid. Check if cell or arc scale is larger than region ({innerMessage})");

		public static readonly MessageClass TargetSizeNotReached = new MessageClass("ConceptualMesh_TargetSizeNotReached", MessageSeverity.Information, "Region {ControlPointIndex} did not reach target size {regionSize}. Increase stretching rate, adjust region size, or leave blank to avoid warning");

		public static Message GetVisitedFaceTwice(int fIdx, Point pt)
		{
			return VisitedFaceTwice.Create(fIdx, pt);
		}

		public static Message GetDiscretizationFailed(Point pt)
		{
			return DiscretizationFailed.Create(pt);
		}

		public static Message GetQuadRepeatedNodes(int ctrlPtIdx, int regionIdx, int nodeIdx)
		{
			return QuadRepeatedCorner.Create(ctrlPtIdx, regionIdx, nodeIdx);
		}

		public static Message GetDiscretizationWarned(Point pt)
		{
			return DiscretizationWarned.Create(pt);
		}

		public static Message GetDiscretizationConflictUnrepaired(int arcIdx, Point pt)
		{
			return DiscretizationConflictUnrepaired.Create(arcIdx, pt);
		}

		public static Message GetArcDiscretizationFailedOnArc(int arcIdx, Point pt)
		{
			return ArcDiscretizationFailed.Create(arcIdx, pt);
		}

		public static Message GetInvalidRegion(int controlPointIdx, int regionIdx, string innerMessage)
		{
			return InvalidRegion.Create(controlPointIdx, innerMessage);
		}

		public static Message GetTargetSizeNotReached(int controlPointIdx, double target, double actualMin, double actualMax)
		{
			return TargetSizeNotReached.Create(controlPointIdx, target.ToString("F0"));
		}

		public static Message GetCartesianTargetSizeNotReached(int controlPointIndex, double cartPri, double cartSec)
		{
			return new Message(TargetSizeNotReached, $"Region {{{"ControlPointIndex"}}} did not reach target size {cartPri:F0}x{cartSec:F0}.\n" + "Increase stretching rates or leave blank, or adjust region sizes.", controlPointIndex);
		}
	}

	public static readonly bool allowTangentIslands = true;

	public List<Polyline> FaceRawLine;

	public List<Polyline> FaceNodeString;

	public List<DiscretizeLine> FaceDiscretization;

	public List<FaceDiscretizationState> FaceNodeState;

	public List<Duple<float>> FaceHalfArcAngle;

	private bool _globalSizeFnUsesNodes = true;

	private readonly bool _globalSizeFnUsesDiffusion;

	private bool _globalSizeFnUsesRegionSize = true;

	private ConceptualMeshCache cache;

	public List<MeshRegionMetadata> CellRegionMetadata;

	private TinSizeFunction _globalSizeFunction;

	[JsonIgnore]
	public List<Polyline> FaceSmoothLine => FaceRawLine;

	public override int AddFace(int nodeA, int nodeB, int cellA = -99, int cellB = -99)
	{
		int addFace = base.AddFace(nodeA, nodeB, cellA, cellB);
		FaceRawLine.Add(null);
		FaceNodeString.Add(null);
		SetArcRawFromSimple(addFace);
		FaceNodeString[addFace] = FaceRawLine[addFace].Clone();
		FaceDiscretization.Add(null);
		FaceNodeState.Add(FaceDiscretizationState.FREE);
		FaceHalfArcAngle.Add(new Duple<float>(0f, 0f));
		return addFace;
	}

	public override void DeleteFace(int fIdx, bool checkCells = true)
	{
		FaceRawLine[fIdx] = null;
		FaceNodeString[fIdx] = null;
		FaceDiscretization[fIdx] = null;
		FaceNodeState[fIdx] = FaceDiscretizationState.FREE;
		FaceHalfArcAngle.Add(new Duple<float>(0f, 0f));
		base.DeleteFace(fIdx, checkCells);
	}

	public override int AddCell()
	{
		int result = base.AddCell();
		MeshRegionMetadata meshRegionMetadata = new MeshRegionMetadata
		{
			PatchType = PatchType.Empty
		};
		CellRegionMetadata.Add(meshRegionMetadata);
		return result;
	}

	public override void DeleteCell(int cIdx)
	{
		CellRegionMetadata[cIdx] = MeshRegionMetadata.NullMetadata();
		base.DeleteCell(cIdx);
	}

	public ConceptualMesh(ConceptualMeshCache cache = null, bool useSizeDiffusion = false)
	{
		_globalSizeFnUsesDiffusion = useSizeDiffusion;
		CellRegionMetadata = new List<MeshRegionMetadata>();
		FaceRawLine = new List<Polyline>();
		FaceNodeString = new List<Polyline>();
		FaceDiscretization = new List<DiscretizeLine>();
		FaceNodeState = new List<FaceDiscretizationState>();
		FaceHalfArcAngle = new List<Duple<float>>();
		this.cache = cache ?? new ConceptualMeshCache();
	}

	public ConceptualMesh(DynamicMesh dMesh, bool useSizeDiffusion = false)
		: base(dMesh)
	{
		_globalSizeFnUsesDiffusion = useSizeDiffusion;
		CellRegionMetadata = Enumerable.Repeat(MeshRegionMetadata.NullMetadata(), CellCount).ToList();
		FaceRawLine = Enumerable.Repeat<Polyline>(null, FaceCount).ToList();
		FaceNodeString = Enumerable.Repeat<Polyline>(null, FaceCount).ToList();
		FaceDiscretization = Enumerable.Repeat<DiscretizeLine>(null, FaceCount).ToList();
		FaceNodeState = Enumerable.Repeat(FaceDiscretizationState.FREE, FaceCount).ToList();
		FaceHalfArcAngle = Enumerable.Repeat(new Duple<float>(0f, 0f), FaceCount).ToList();
		cache = new ConceptualMeshCache();
	}

	public ConceptualMesh GetRegionConceptualMesh(ref int regionIdx, out List<int> faceMap)
	{
		ConceptualMesh rmesh = new ConceptualMesh(useSizeDiffusion: _globalSizeFnUsesDiffusion);
		rmesh.NodePoints = NodePoints;
		rmesh.NodeDeleted = NodeDeleted;
		List<int> localFaceMap = new List<int>();
		int regionIdx2 = rmesh.AddCell();
		rmesh.CellRegionMetadata[regionIdx2] = CellRegionMetadata[regionIdx];
		foreach (HalfEdge cellFace in CellFaces[regionIdx])
		{
			int copyFace = CopyFace(cellFace.fIdx);
			Duple<int> faceCells = new Duple<int> { [cellFace.orient] = regionIdx2 };
			int halfEdgeToOppositeCell = HalfEdgeToOppositeCell(cellFace);
			if (halfEdgeToOppositeCell < 0)
			{
				faceCells[1 - cellFace.orient] = halfEdgeToOppositeCell;
			}
			else
			{
				faceCells[1 - cellFace.orient] = -2;
			}
			rmesh.FaceCells[copyFace] = faceCells;
			rmesh.CellAddFace(regionIdx2, copyFace, cellFace.orient);
		}
		foreach (int cellBreakline in CellBreaklines(regionIdx))
		{
			int fIdx = CopyFace(cellBreakline);
			rmesh.SetFaceCell(fIdx, 0, regionIdx2);
			rmesh.SetFaceCell(fIdx, 1, regionIdx2);
		}
		foreach (int cellIslandFaceIndex in CellIslandFaces(regionIdx))
		{
			int fIdx2 = CopyFace(cellIslandFaceIndex);
			if (FaceCells[cellIslandFaceIndex].A == regionIdx)
			{
				rmesh.SetFaceCell(fIdx2, 0, regionIdx2);
				rmesh.SetFaceCell(fIdx2, 1, (FaceCells[cellIslandFaceIndex].B < 0) ? FaceCells[cellIslandFaceIndex].B : (-2));
			}
			else
			{
				rmesh.SetFaceCell(fIdx2, 1, regionIdx2);
				rmesh.SetFaceCell(fIdx2, 0, (FaceCells[cellIslandFaceIndex].A < 0) ? FaceCells[cellIslandFaceIndex].A : (-2));
			}
		}
		rmesh.AddCellsFromFaces();
		regionIdx = regionIdx2;
		faceMap = localFaceMap;
		rmesh.cache = cache;
		return rmesh;
		int CopyFace(int ordinal)
		{
			int addFace = rmesh.AddFace(FaceNodes[ordinal].A, FaceNodes[ordinal].B);
			localFaceMap.Add(ordinal);
			rmesh.FaceRawLine[addFace] = FaceRawLine[ordinal];
			rmesh.FaceNodeString[addFace] = FaceNodeString[ordinal].Clone();
			rmesh.FaceHalfArcAngle[addFace] = FaceHalfArcAngle[ordinal];
			rmesh.FaceDiscretization[addFace] = FaceDiscretization[ordinal];
			rmesh.FaceNodeState[addFace] = FaceNodeState[ordinal];
			return addFace;
		}
	}

	public override void UpdateCellFromJson(int cIdx, JsonElement rec)
	{
		base.UpdateCellFromJson(cIdx, rec);
	}

	public override Dictionary<string, object> CellSimpleRepr(int cIdx)
	{
		Dictionary<string, object> cellSimpleRepr = base.CellSimpleRepr(cIdx);
		cellSimpleRepr["regionMetadata"] = CellRegionMetadata[cIdx].PatchType;
		return cellSimpleRepr;
	}

	public override void UpdateFaceFromJson(int fIdx, JsonElement rec)
	{
		base.UpdateFaceFromJson(fIdx, rec);
		if (rec.TryGetProperty("nodeStringXY", out var localValue))
		{
			FaceNodeString[fIdx] = DynamicMesh.DeserializePolyline(localValue);
		}
		if (rec.TryGetProperty("rawStringXY", out var localValue2) && localValue2.ValueKind == JsonValueKind.Array)
		{
			FaceRawLine[fIdx] = DynamicMesh.DeserializePolyline(localValue2);
		}
	}

	public override Dictionary<string, object> FaceSimpleRepr(int fIdx)
	{
		Dictionary<string, object> faceSimpleRepr = base.FaceSimpleRepr(fIdx);
		faceSimpleRepr["nodeStringXY"] = DynamicMesh.SerializePolyline(FaceNodeString[fIdx]);
		faceSimpleRepr["rawStringXY"] = DynamicMesh.SerializePolyline(FaceRawLine[fIdx]);
		faceSimpleRepr["faceDiscretization"] = FaceDiscretization[fIdx]?.Serialize();
		faceSimpleRepr["faceNodeState"] = (int)FaceNodeState[fIdx];
		faceSimpleRepr["faceHalfArcAngle"] = FaceHalfArcAngle[fIdx].ToArray();
		return faceSimpleRepr;
	}

	public void SetArcRawFromSimple(int fIdx)
	{
		FaceRawLine[fIdx] = FaceSimpleGeometry(fIdx);
	}

	public void SetArcNodesFromSmooth(int fIdx, int count, bool update = true)
	{
		SetArcNodesFromSmooth(fIdx, new DiscretizeLine(count), update);
	}

	public void SetArcNodesFromSmooth(int fIdx, DiscretizeLine discretizeLine, bool update = true, bool includeExisting = false)
	{
		Polyline dst = FaceNodeString[fIdx];
		discretizeLine.Apply(FaceSmoothLine[fIdx], ref dst, includeExisting);
		FaceNodeString[fIdx] = dst;
		if (update)
		{
			FaceDiscretization[fIdx] = discretizeLine;
		}
	}

	public void SetArcNodesFromSmooth(int fIdx, FaceDiscretizationState nodeState, bool update = true)
	{
		SetArcNodesFromSmooth(fIdx, FaceDiscretization[fIdx], update);
		FaceNodeState[fIdx] = nodeState;
	}

	public OperationResult RepairDiscretizationConflicts(ProgressReporter reporter)
	{
		OperationResult operationResult = null;
		List<(ConstrainedEdge, ConstrainedEdge)> valueTuples = new List<(ConstrainedEdge, ConstrainedEdge)>();
		DynamicTINFactory<NativePredicates> dynTin = null;
		for (int ordinal = 5; ordinal >= 0; ordinal--)
		{
			valueTuples.Clear();
			operationResult = TriangulateConceptualMesh(out dynTin, out var _, forSizeFunction: false, errorOnRevisitFace: false, valueTuples);
			if (!operationResult.Result)
			{
				return operationResult;
			}
			if (valueTuples.Count == 0)
			{
				return operationResult;
			}
			foreach (var localValue in valueTuples)
			{
				FindNodeSegment(dynTin.Tin, localValue.Item1, out var arcIdx, out var fnAIdx, out var fnBIdx);
				if (arcIdx >= 0)
				{
					FindNodeSegment(dynTin.Tin, localValue.Item2, out var arcIdx2, out var fnAIdx2, out var fnBIdx2);
					if (arcIdx2 >= 0)
					{
						RepairDiscretizationConflict(arcIdx, fnAIdx, fnBIdx, arcIdx2, fnAIdx2, fnBIdx2, operationResult);
					}
				}
			}
			if (!operationResult.Result)
			{
				return operationResult;
			}
		}
		foreach (var localValue2 in valueTuples)
		{
			operationResult.AddMessage(MessageCodes.GetDiscretizationWarned(dynTin.Tin[localValue2.Item1.PointA]));
		}
		for (int i = 0; i < CellCount; i++)
		{
			if (CellNodePolygon(i).Area <= 0.0)
			{
				operationResult.AddFailure(MessageCodes.GetInvalidRegion(CellRegionMetadata[i].ControlPointIndex, i, "Discretized region had non-positive area"));
			}
		}
		return operationResult;
		void FindNodeSegment(Tin tin, ConstrainedEdge edge, out int reference3, out int reference, out int reference2)
		{
			reference = -1;
			reference2 = -1;
			int tinPointATerm = (int)tin.Z[edge.PointA];
			int reference4 = (int)tin.M[edge.PointA];
			int tinPointBTerm = (int)tin.Z[edge.PointB];
			int reference22 = (int)tin.M[edge.PointB];
			if (tinPointATerm < 0 && tinPointBTerm < 0)
			{
				reference3 = NodesToHalfEdge(reference4, reference22).fIdx;
				if (FaceNodeString[reference3].Count > 2)
				{
					reference3 = -1;
				}
				else
				{
					reference = 0;
					reference2 = 1;
				}
			}
			else
			{
				reference3 = Math.Max(tinPointATerm, tinPointBTerm);
				if (tinPointATerm >= 0)
				{
					if (reference4 < 0 || reference4 >= FaceNodeString[reference3].Count)
					{
						reference3 = -1;
						return;
					}
					reference = reference4;
				}
				else if (FaceNodes[reference3].A == reference4)
				{
					reference = 0;
				}
				else
				{
					if (FaceNodes[reference3].B != reference4)
					{
						reference3 = -1;
						return;
					}
					reference = FaceNodeString[reference3].Count - 1;
				}
				if (tinPointBTerm >= 0)
				{
					if (reference22 < 0 || reference22 >= FaceNodeString[reference3].Count)
					{
						reference3 = -1;
						return;
					}
					reference2 = reference22;
				}
				else if (FaceNodes[reference3].A == reference22)
				{
					reference2 = 0;
				}
				else
				{
					if (FaceNodes[reference3].B != reference22)
					{
						reference3 = -1;
						return;
					}
					reference2 = FaceNodeString[reference3].Count - 1;
				}
				if (reference2 < reference)
				{
					int reference23 = reference2;
					reference2 = reference;
					reference = reference23;
				}
			}
		}
	}

	private void RepairDiscretizationConflict(int arc1Idx, int fn1AIdx, int fn1BIdx, int arc2Idx, int fn2AIdx, int fn2BIdx, OperationResult res)
	{
		Segment seg = new Segment(FaceNodeString[arc1Idx][fn1AIdx], FaceNodeString[arc1Idx][fn1BIdx]);
		Segment seg2 = new Segment(FaceNodeString[arc2Idx][fn2AIdx], FaceNodeString[arc2Idx][fn2BIdx]);
		PointOnPolyline startPoint = FaceSmoothLine[arc1Idx].NearestPointOnPolyline(seg.A);
		PointOnPolyline endPoint = FaceSmoothLine[arc1Idx].NearestPointOnPolyline(seg.B);
		PointOnPolyline startPoint2 = FaceSmoothLine[arc2Idx].NearestPointOnPolyline(seg2.A);
		PointOnPolyline endPoint2 = FaceSmoothLine[arc2Idx].NearestPointOnPolyline(seg2.B);
		Polyline polyline = FaceSmoothLine[arc1Idx].Subsection(startPoint, endPoint);
		Polyline polyline2 = FaceSmoothLine[arc2Idx].Subsection(startPoint2, endPoint2);
		if (!polyline.Intersects(seg2))
		{
			int scanForNewNode = ScanForNewNode(polyline, seg2);
			if (scanForNewNode >= 0)
			{
				FaceNodeString[arc1Idx].Insert(fn1BIdx, polyline.Points[scanForNewNode]);
			}
			return;
		}
		if (!polyline2.Intersects(seg))
		{
			int scanForNewNode2 = ScanForNewNode(polyline2, seg);
			if (scanForNewNode2 >= 0)
			{
				FaceNodeString[arc2Idx].Insert(fn2BIdx, polyline2.Points[scanForNewNode2]);
			}
			return;
		}
		List<int> intList = new List<int>
		{
			0,
			polyline.PointCount - 1
		};
		List<int> intListList = new List<int>
		{
			0,
			polyline2.PointCount - 1
		};
		Polyline polyline3 = new Polyline();
		foreach (int ordinal in intList)
		{
			polyline3.Add(polyline[ordinal]);
		}
		Polyline polyline4 = new Polyline();
		foreach (int ordinal2 in intListList)
		{
			polyline4.Add(polyline2[ordinal2]);
		}
		int pointCountDifference = polyline.PointCount + polyline2.PointCount;
		while (pointCountDifference >= 0 && polyline3.Intersects(polyline4))
		{
			double distanceSquaredTo = 0.0;
			int unity = -1;
			int unity2 = 0;
			if (intList.Count < polyline.PointCount)
			{
				for (int i = 0; i < intList.Count - 1; i++)
				{
					if (intList[i + 1] - intList[i] != 1)
					{
						double distanceSquaredTo2 = polyline[intList[i]].DistanceSquaredTo(polyline[intList[i + 1]]);
						if (distanceSquaredTo2 > distanceSquaredTo)
						{
							distanceSquaredTo = distanceSquaredTo2;
							unity = i;
							unity2 = 1;
						}
					}
				}
			}
			if (intListList.Count < polyline2.PointCount)
			{
				for (int j = 0; j < intListList.Count - 1; j++)
				{
					if (intListList[j + 1] - intListList[j] != 1)
					{
						double distanceSquaredTo3 = polyline2[intListList[j]].DistanceSquaredTo(polyline2[intListList[j + 1]]);
						if (distanceSquaredTo3 > distanceSquaredTo)
						{
							distanceSquaredTo = distanceSquaredTo3;
							unity = j;
							unity2 = 2;
						}
					}
				}
			}
			if (unity2 == 1)
			{
				int ordinal3 = (intList[unity] + intList[unity + 1]) / 2;
				intList.Insert(unity + 1, ordinal3);
				polyline3.Clear();
				foreach (int ordinal4 in intList)
				{
					polyline3.Add(polyline[ordinal4]);
				}
			}
			else
			{
				int ordinal5 = (intListList[unity] + intListList[unity + 1]) / 2;
				intListList.Insert(unity + 1, ordinal5);
				polyline4.Clear();
				foreach (int ordinal6 in intListList)
				{
					polyline4.Add(polyline2[ordinal6]);
				}
			}
			pointCountDifference--;
		}
		if (pointCountDifference >= 0)
		{
			if (polyline3.Count > 2)
			{
				FaceNodeString[arc1Idx].InsertRange(fn1BIdx, polyline3, 1, polyline3.Count - 2);
			}
			if (polyline4.Count > 2)
			{
				int countOffset = ((arc1Idx == arc2Idx && fn2BIdx > fn1BIdx) ? (polyline3.Count - 2) : 0);
				FaceNodeString[arc2Idx].InsertRange(fn2BIdx + countOffset, polyline4, 1, polyline4.Count - 2);
			}
		}
		else
		{
			res.AddMessage(MessageCodes.GetDiscretizationConflictUnrepaired(arc1Idx, seg.A));
			if (arc2Idx != arc1Idx)
			{
				res.AddMessage(MessageCodes.GetDiscretizationConflictUnrepaired(arc2Idx, seg2.A));
			}
		}
		static int ScanForNewNode(Polyline poly, Segment segment)
		{
			int result = -1;
			double minValue = double.MinValue;
			Point ptA = poly.Points[0];
			Point ptA2 = poly.Points.Last();
			for (int k = 1; k < poly.Count - 1; k++)
			{
				Segment seg3 = new Segment(ptA, poly.Points[k]);
				Segment seg4 = new Segment(ptA2, poly.Points[k]);
				if (!segment.IntersectPoint(seg3, out var _, out var _, out var t, out var t2) && !segment.IntersectPoint(seg4, out var _, out var _, out var t3, out var t4))
				{
					t = Math.Abs(t - 0.5) - 0.5;
					t2 = Math.Abs(t2 - 0.5) - 0.5;
					t3 = Math.Abs(t3 - 0.5) - 0.5;
					t4 = Math.Abs(t4 - 0.5) - 0.5;
					double minValue2 = Math.Min(Math.Max(t, t2), Math.Max(t3, t4));
					if (minValue2 > minValue)
					{
						result = k;
						minValue = minValue2;
					}
				}
			}
			return result;
		}
	}

	public bool SegmentToArcNodes(Segment seg, out int arcIdx, out int fnAIdx, out int fnBIdx)
	{
		arcIdx = (fnAIdx = (fnBIdx = -1));
		PointToArcNode(seg.A, out var nIdx, out var fIdx, out fnAIdx);
		PointToArcNode(seg.B, out var nIdx2, out var fIdx2, out fnBIdx);
		if (fIdx >= 0 && fIdx == fIdx2)
		{
			arcIdx = fIdx;
			return true;
		}
		if (nIdx >= 0 && nIdx2 >= 0)
		{
			HalfEdge halfEdge = NodesToHalfEdge(nIdx, nIdx2);
			if (halfEdge.IsNull())
			{
				return false;
			}
			if (FaceNodeString[halfEdge.fIdx].Count != 2)
			{
				return false;
			}
			arcIdx = halfEdge.fIdx;
			fnAIdx = halfEdge.orient;
			fnBIdx = 1 - halfEdge.orient;
			return false;
		}
		arcIdx = Math.Max(fIdx, fIdx2);
		Polyline polyline = FaceNodeString[arcIdx];
		if (fnAIdx < 0)
		{
			if (seg.A == polyline[0])
			{
				fnAIdx = 0;
			}
			else
			{
				if (!(seg.A == polyline.Last()))
				{
					arcIdx = (fnAIdx = (fnBIdx = -1));
					return false;
				}
				fnAIdx = polyline.Count - 1;
			}
		}
		if (fnBIdx < 0)
		{
			if (seg.B == polyline[0])
			{
				fnBIdx = 0;
			}
			else
			{
				if (!(seg.B == polyline.Last()))
				{
					arcIdx = (fnAIdx = (fnBIdx = -1));
					return false;
				}
				fnBIdx = polyline.Count - 1;
			}
		}
		return true;
	}

	public bool PointToArcNode(Point pnt, out int nIdx, out int fIdx, out int arcNodeIdx)
	{
		nIdx = (fIdx = (arcNodeIdx = -1));
		foreach (int nodeIterator in NodeIterator())
		{
			if (pnt == NodePoints[nodeIterator])
			{
				nIdx = nodeIterator;
				return true;
			}
		}
		foreach (int faceIterator in FaceIterator())
		{
			Polyline polyline = FaceNodeString[faceIterator];
			if (polyline == null)
			{
				continue;
			}
			for (int i = 1; i < polyline.Count; i++)
			{
				if (pnt == polyline[i])
				{
					fIdx = faceIterator;
					arcNodeIdx = i;
					return true;
				}
			}
		}
		return false;
	}

	public int AddFaceFromCoords(double[,] coords)
	{
		List<Point> points = new List<Point>();
		for (int i = 0; i < coords.GetLength(0); i++)
		{
			points.Add(new Point(coords[i, 0], coords[i, 1]));
		}
		Polyline pline = new Polyline(points);
		return AddFaceFromCoords(pline);
	}

	public int AddFaceFromCoords(Polyline pline)
	{
		int nodeA = AddNode(pline.First());
		int nodeB = AddNode(pline.Last());
		int addFace = AddFace(nodeA, nodeB);
		FaceRawLine[addFace] = pline;
		return addFace;
	}

	public Polygon CellNodePolygon(int cIdx)
	{
		return CellGenericPolygon(cIdx, (int fIdx) => FaceNodeString[fIdx]);
	}

	public Polygon CellRawPolygon(int cIdx)
	{
		return CellGenericPolygon(cIdx, (int fIdx) => FaceRawLine[fIdx]);
	}

	public Polygon CellSmoothPolygon(int cIdx)
	{
		return CellGenericPolygon(cIdx, (int fIdx) => FaceSmoothLine[fIdx]);
	}

	public ComplexPolygon CellNodeComplexPolygon(int cIdx)
	{
		HashSet<int> faceVisited;
		return CellGenericComplexPolygon(cIdx, (int fIdx) => FaceNodeString[fIdx], out faceVisited);
	}

	public List<Polygon> CellNodeComplexRings(int cIdx, out HashSet<int> islandArcs)
	{
		return CellGenericComplexRings(cIdx, (int fIdx) => FaceNodeString[fIdx], out islandArcs);
	}

	public ComplexPolygon CellNodeComplexPolygon(int cIdx, out HashSet<int> islandArcs)
	{
		return CellGenericComplexPolygon(cIdx, (int fIdx) => FaceNodeString[fIdx], out islandArcs);
	}

	public ComplexPolygon CellRawComplexPolygon(int cIdx)
	{
		HashSet<int> faceVisited;
		return CellGenericComplexPolygon(cIdx, (int fIdx) => FaceRawLine[fIdx], out faceVisited);
	}

	public ComplexPolygon CellSmoothComplexPolygon(int cIdx)
	{
		HashSet<int> faceVisited;
		return CellGenericComplexPolygon(cIdx, (int fIdx) => FaceSmoothLine[fIdx], out faceVisited);
	}

	public double ClearanceToNodeGeometry(Segment seg, int regionIdx)
	{
		double maxValue = double.MaxValue;
		foreach (HalfEdge cellFace in CellFaces[regionIdx])
		{
			FaceNodeString[cellFace.fIdx].NearestPointOnPolyline(seg.A, out var actualDistance);
			maxValue = Math.Min(maxValue, actualDistance);
			FaceNodeString[cellFace.fIdx].NearestPointOnPolyline(seg.B, out actualDistance);
			maxValue = Math.Min(maxValue, actualDistance);
		}
		foreach (int cellBreakline in CellBreaklines(regionIdx))
		{
			FaceNodeString[cellBreakline].NearestPointOnPolyline(seg.A, out var actualDistance2);
			maxValue = Math.Min(maxValue, actualDistance2);
			FaceNodeString[cellBreakline].NearestPointOnPolyline(seg.B, out actualDistance2);
			maxValue = Math.Min(maxValue, actualDistance2);
		}
		return maxValue;
	}

	public List<int> CellIslandCells(int cIdx)
	{
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		List<Polygon> polygons = new List<Polygon>();
		Polygon polygon = CellSmoothPolygon(cIdx);
		HashSet<Point> outerPoints = null;
		for (int i = 0; i < CellCount; i++)
		{
			if (i != cIdx && !CellDeleted[cIdx])
			{
				Polygon polygon2 = CellSmoothPolygon(i);
				if (allowTangentIslands ? ContainsIsland(polygon, polygon2, ref outerPoints) : polygon.Contains(polygon2))
				{
					intListList.Add(i);
					polygons.Add(polygon2);
				}
			}
		}
		double[] doubleValues = polygons.Select((Polygon p) => p.Area).ToArray();
		int[] range = Enumerable.Range(0, doubleValues.Length).ToArray();
		Array.Sort(doubleValues, range);
		List<int> intList2 = new List<int>();
		for (int lengthOffset = range.Length - 1; lengthOffset >= 0; lengthOffset--)
		{
			int range2 = range[lengthOffset];
			Polygon polygon3 = polygons[range2];
			bool condition = true;
			foreach (int ordinal in intList2)
			{
				if (allowTangentIslands ? polygons[ordinal].ContainsAll(polygon3.Points) : polygons[ordinal].Contains(polygon3))
				{
					condition = false;
					break;
				}
			}
			if (condition)
			{
				intList2.Add(range2);
				intList.Add(intListList[range2]);
			}
		}
		return intList;
	}

	public void UpdateIslandBreaklineFaceCells()
	{
		foreach (int cellIterator in CellIterator())
		{
			UpdateIslandFaceCells(cellIterator);
		}
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator].A >= 0 || FaceCells[faceIterator].B >= 0)
			{
				continue;
			}
			int cellIterator2Term = -1;
			double maxValue = double.MaxValue;
			Polyline polyline = FaceSmoothLine[faceIterator];
			foreach (int cellIterator2 in CellIterator())
			{
				Polygon polygon = CellSmoothPolygon(cellIterator2);
				if (!(polygon.Area > maxValue) && (polygon.Contains(polyline.First()) || polygon.Contains(polyline.Last())))
				{
					cellIterator2Term = cellIterator2;
					maxValue = polygon.Area;
				}
			}
			if (cellIterator2Term >= 0)
			{
				SetFaceCell(faceIterator, 0, cellIterator2Term);
				SetFaceCell(faceIterator, 1, cellIterator2Term);
			}
		}
	}

	public void UpdateIslandFaceCells(int parentIdx)
	{
		foreach (int cellIslandCellIndex in CellIslandCells(parentIdx))
		{
			foreach (HalfEdge cellFace in CellFaces[cellIslandCellIndex])
			{
				if (HalfEdgeToOppositeCell(cellFace) < 0)
				{
					SetFaceCell(HalfEdgeFlip(cellFace), parentIdx);
				}
			}
		}
	}

	public IEnumerable<int> CellIslandFaces(int regionIdx)
	{
		foreach (int cellIslandCellIndex in CellIslandCells(regionIdx))
		{
			foreach (HalfEdge cellFace in CellFaces[cellIslandCellIndex])
			{
				if (HalfEdgeToOppositeCell(cellFace) == regionIdx)
				{
					yield return cellFace.fIdx;
				}
			}
		}
	}

	public bool HalfEdgeIsInterior(HalfEdge he)
	{
		int halfEdgeToCell = HalfEdgeToCell(he);
		if (halfEdgeToCell < 0)
		{
			return false;
		}
		return !CellFaces[halfEdgeToCell].Contains(he);
	}

	public ComplexPolygon CellGenericComplexPolygon(int cIdx, Func<int, Polyline> getArcPoints, out HashSet<int> faceVisited)
	{
		OperationResult operationResult = ComplexPolygon.TryCreateComplexPolygon(CellGenericComplexRings(cIdx, getArcPoints, out faceVisited), out var poly, checkIntersections: false);
		if (!operationResult)
		{
			throw new StructuredException(operationResult);
		}
		return poly;
	}

	public static bool ContainsIsland(Polygon outer, Polyline inner, ref HashSet<Point> outerPoints)
	{
		if (!outer.Extent.Overlaps(inner.Extent))
		{
			return false;
		}
		if (outerPoints == null)
		{
			outerPoints = new HashSet<Point>(outer.Points);
		}
		foreach (Point point in inner.Points)
		{
			if (!outerPoints.Contains(point))
			{
				return outer.Contains(point);
			}
		}
		return false;
	}

	public List<Polygon> CellGenericComplexRings(int cIdx, Func<int, Polyline> getArcPoints, out HashSet<int> faceVisited)
	{
		List<Polygon> polygons = new List<Polygon> { getCellPolygon(cIdx) };
		HashSet<Point> outerPoints = new HashSet<Point>(polygons[0].Points);
		if (polygons[0].PointCount < 3)
		{
			throw new StructuredException(MessageCodes.GetInvalidRegion(CellRegionMetadata[cIdx].ControlPointIndex, cIdx, "Region boundary has <3 vertices. Check cell size of region and arcs"));
		}
		UpdateIslandFaceCells(cIdx);
		faceVisited = new HashSet<int>();
		foreach (int cellIslandCellIndex in CellIslandCells(cIdx))
		{
			foreach (HalfEdge cellFace in CellFaces[cellIslandCellIndex])
			{
				if (faceVisited.Contains(cellFace.fIdx))
				{
					break;
				}
				if (HalfEdgeToOppositeCell(cellFace) != cIdx)
				{
					continue;
				}
				List<Point> points = new List<Point>();
				HalfEdge halfEdge = HalfEdgeFlip(cellFace);
				HalfEdge halfEdge2 = halfEdge;
				for (int faceCountOffset = 1 + FaceCount; faceCountOffset > 0; faceCountOffset--)
				{
					if (HalfEdgeToCell(halfEdge2) == HalfEdgeToOppositeCell(halfEdge2))
					{
						halfEdge2 = HalfEdgeNextByNode(HalfEdgeFlip(halfEdge2));
					}
					else
					{
						faceVisited.Add(halfEdge2.fIdx);
						Polyline polyline = getArcPoints(halfEdge2.fIdx);
						int intervalIndex = 0;
						int sign = 1;
						if (halfEdge2.orient == 1)
						{
							intervalIndex = polyline.Count - 1;
							sign = -1;
						}
						for (int i = 1; i < polyline.Count; i++)
						{
							points.Add(polyline[intervalIndex + sign * i]);
						}
						List<HalfEdge> nodeToSortedHalfEdges = NodeToSortedHalfEdges(HalfEdgeNodeForward(halfEdge2), HalfEdgeFlip(halfEdge2));
						for (int j = 1; j < nodeToSortedHalfEdges.Count; j++)
						{
							halfEdge2 = nodeToSortedHalfEdges[j];
							if (HalfEdgeToCell(halfEdge2) == cIdx)
							{
								break;
							}
						}
					}
					if (halfEdge2 == halfEdge)
					{
						break;
					}
				}
				points.Reverse();
				Polygon polygon = new Polygon(points);
				if (polygon.PointCount < 3)
				{
					throw new DynamicMeshException($"Boundary of island including region {cellIslandCellIndex} has <3 vertices. Check arc discretization");
				}
				if (!allowTangentIslands)
				{
					polygons[0].Contains(polygon);
				}
				else
				{
					ContainsIsland(polygons[0], polygon, ref outerPoints);
				}
				polygons.Add(polygon);
			}
		}
		return polygons;
		Polygon getCellPolygon(int cIdx2)
		{
			return CellGenericPolygon(cIdx2, getArcPoints);
		}
	}

	public List<int> CellBreaklines(int cIdx)
	{
		List<int> intList = new List<int>();
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator].A == cIdx && FaceCells[faceIterator].B == cIdx)
			{
				intList.Add(faceIterator);
			}
		}
		return intList;
	}

	public new static ConceptualMesh ReadJson(string filename)
	{
		using FileStream utf8Json = File.OpenRead(filename);
		return FromSimpleRepr(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(utf8Json, DynamicMesh.JsonOptions()));
	}

	public new static ConceptualMesh FromSimpleRepr(Dictionary<string, JsonElement> simpleRepr)
	{
		ConceptualMesh conceptualMesh = new ConceptualMesh();
		conceptualMesh.UpdateFromJson(simpleRepr);
		return conceptualMesh;
	}

	public double CellNodeArea(int cIdx)
	{
		return CellNodePolygon(cIdx).Area;
	}

	public double CellSmoothArea(int cIdx)
	{
		return CellSmoothPolygon(cIdx).Area;
	}

	public void CheckMeshDuplicateNodes(List<DynamicMeshError> errors, double tolerance = 0.0)
	{
		SpatialIndex<int> pointIndex = PointIndex();
		HashSet<int> intSet = new HashSet<int>();
		foreach (int faceIterator in FaceIterator())
		{
			foreach (int duplicatE_POINTIds in FaceNodes[faceIterator])
			{
				if (intSet.Contains(duplicatE_POINTIds))
				{
					continue;
				}
				intSet.Add(duplicatE_POINTIds);
				foreach (int node in FindNodes(NodePoints[duplicatE_POINTIds], ref pointIndex, tolerance))
				{
					if (node == duplicatE_POINTIds)
					{
						continue;
					}
					using IEnumerator<int> nodeToEdges = NodeToEdges(node).GetEnumerator();
					if (nodeToEdges.MoveNext())
					{
						_ = nodeToEdges.Current;
						errors.Add(new DynamicMeshError(DynamicMeshErrorType.DUPLICATE_POINT, duplicatE_POINTIds));
					}
				}
			}
		}
	}

	public override bool CheckMesh(out List<DynamicMeshError> errors)
	{
		base.CheckMesh(out errors);
		List<DynamicMeshError> cmErrors = new List<DynamicMeshError>();
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceRawLine[faceIterator] != null)
			{
				CheckArc(faceIterator, FaceRawLine[faceIterator]);
			}
			if (FaceNodeString[faceIterator] != null)
			{
				CheckArc(faceIterator, FaceNodeString[faceIterator]);
			}
		}
		foreach (DynamicMeshError error in errors)
		{
			if (error.errorType == DynamicMeshErrorType.CELL_NONPOSITIVE_AREA)
			{
				continue;
			}
			if (error.errorType == DynamicMeshErrorType.FACE_INVALID_NODE)
			{
				Duple<int> duple = FaceNodes[error.element];
				if (duple.A == duple.B)
				{
					continue;
				}
			}
			if (error.errorType == DynamicMeshErrorType.FACE_CELL_MISMATCH)
			{
				int element = error.element;
				if (FaceCells[element][0] == FaceCells[element][1] && FaceCells[element][0] >= 0)
				{
					continue;
				}
				int cIdx = FaceCells[element][error.orient];
				if (CellIslandCells(cIdx).Contains(FaceCells[element][1 - error.orient]))
				{
					continue;
				}
			}
			cmErrors.Add(error);
		}
		for (int i = 0; i < CellCount; i++)
		{
			if (!CellDeleted[i] && CellSmoothArea(i) <= 0.0)
			{
				cmErrors.Add(new DynamicMeshError
				{
					element = i,
					errorType = DynamicMeshErrorType.CELL_NONPOSITIVE_AREA
				});
			}
		}
		CheckMeshDuplicateNodes(cmErrors);
		errors = cmErrors;
		return errors.Count == 0;
		void CheckArc(int fIdx, Polyline pline)
		{
			if (1 == 0 || !(pline.First() == NodePoints[FaceNodes[fIdx].A]) || !(pline.Last() == NodePoints[FaceNodes[fIdx].B]) || pline.HasConsecutiveIdenticalPoints())
			{
				cmErrors.Add(new DynamicMeshError
				{
					element = fIdx,
					errorType = DynamicMeshErrorType.FACE_GEOMETRY_INVALID
				});
			}
		}
	}

	public List<Polyline> ContiguousPolylines()
	{
		List<Polyline> polylines = new List<Polyline>();
		HashSet<int> intSet = new HashSet<int>();
		for (int i = 0; i < FaceCount; i++)
		{
			if (FaceDeleted[i] || intSet.Contains(i))
			{
				continue;
			}
			List<Polyline> polylinesList = new List<Polyline>();
			for (byte b = 0; b < 2; b++)
			{
				HalfEdge he = new HalfEdge(i, b);
				do
				{
					if (!intSet.Contains(he.fIdx))
					{
						Polyline polyline = FaceSmoothLine[he.fIdx];
						if (he.orient != b)
						{
							polyline = polyline.Clone();
							polyline.Reverse();
						}
						polylinesList.Add(polyline);
						intSet.Add(he.fIdx);
					}
					if (NodeDegree(HalfEdgeNodeForward(he)) != 2)
					{
						break;
					}
					he = HalfEdgeNext(he);
				}
				while (!intSet.Contains(he.fIdx));
				polylinesList.Reverse();
			}
			polylines.Add(Polyline.Concatenate(polylinesList));
		}
		return polylines;
	}

	public void CoalesceFaces()
	{
		for (int i = 0; i < NodeCount; i++)
		{
			if (!NodeDeleted[i] && NodeDegree(i) == 2)
			{
				CoalesceFaces(i);
			}
		}
		CheckMesh(out var _);
	}

	public void CoalesceFaces(int nIdx)
	{
		List<HalfEdge> nodeToHalfEdges = NodeToHalfEdges(nIdx);
		HalfEdge heA = HalfEdgeFlip(nodeToHalfEdges[0]);
		HalfEdge heB = nodeToHalfEdges[1];
		int fNew = AddFace(HalfEdgeNodeBack(heA), HalfEdgeNodeForward(heB), HalfEdgeToCell(heA), HalfEdgeToOppositeCell(heA));
		CoalesceLines(FaceRawLine);
		CoalesceLines(FaceNodeString);
		for (int i = 0; i < 2; i++)
		{
			int halfEdgeToCell = HalfEdgeToCell(heB);
			if (halfEdgeToCell >= 0)
			{
				List<HalfEdge> cellFace = CellFaces[halfEdgeToCell];
				cellFace.Remove(heB);
				for (int j = 0; j < cellFace.Count; j++)
				{
					if (cellFace[j].fIdx != heA.fIdx)
					{
						continue;
					}
					byte b = 0;
					if (HalfEdgeToCell(heA) == halfEdgeToCell)
					{
						b = 0;
					}
					else
					{
						if (HalfEdgeToOppositeCell(heA) != halfEdgeToCell)
						{
							throw new Exception("Mesh topology is broken");
						}
						b = 1;
					}
					HalfEdge halfEdge = new HalfEdge(fNew, b);
					cellFace[j] = halfEdge;
					break;
				}
			}
			heB.orient = (byte)(1 - heB.orient);
		}
		FaceDiscretization[fNew] = CoalesceFaceDiscretization(heA, heB);
		DeleteFace(heA.fIdx, checkCells: false);
		DeleteFace(heB.fIdx, checkCells: false);
		void CoalesceLines(List<Polyline> polylines)
		{
			List<Point> pointsList = new List<Point>(polylines[heA.fIdx].Points);
			if (heA.orient == 1)
			{
				pointsList.Reverse();
			}
			pointsList.RemoveAt(pointsList.Count - 1);
			IList<Point> points = polylines[heB.fIdx].Points;
			if (heB.orient == 0)
			{
				pointsList.AddRange(points);
			}
			else
			{
				pointsList.AddRangeReverse(points);
			}
			polylines[fNew] = new Polyline(pointsList);
		}
	}

	public DiscretizeLine CoalesceFaceDiscretization(HalfEdge heA, HalfEdge heB)
	{
		DiscretizeLine discretizeLine = FaceDiscretization[heA.fIdx];
		DiscretizeLine discretizeLine2 = FaceDiscretization[heB.fIdx];
		if (discretizeLine == null || discretizeLine2 == null)
		{
			return null;
		}
		double length = FaceSmoothLine[heA.fIdx].Length;
		double length2 = FaceSmoothLine[heB.fIdx].Length;
		double startSize;
		double endSize;
		if (discretizeLine == null)
		{
			discretizeLine2.StartEndSize(out startSize, out endSize, FaceSmoothLine[heB.fIdx]);
			if (heB.orient == 1)
			{
				double startSize3 = startSize;
				startSize = endSize;
				endSize = startSize3;
			}
			return DiscretizeLine.BySizes(startSize, endSize, length + length2);
		}
		double startSize2;
		double endSize2;
		if (discretizeLine2 == null)
		{
			discretizeLine.StartEndSize(out startSize2, out endSize2, FaceSmoothLine[heA.fIdx]);
			if (heA.orient == 1)
			{
				double startSize22 = startSize2;
				startSize2 = endSize2;
				endSize2 = startSize22;
			}
			return DiscretizeLine.BySizes(startSize2, endSize2, length + length2);
		}
		if (discretizeLine.Mode == SizeFunctionMode.CountWithBias && discretizeLine2.Mode == SizeFunctionMode.CountWithBias)
		{
			double bias = 0.5 * (((heB.orient == 0) ? discretizeLine2.bias : (1.0 / discretizeLine2.bias)) + ((heA.orient == 0) ? discretizeLine.bias : (1.0 / discretizeLine.bias)));
			return new DiscretizeLine(discretizeLine.count + discretizeLine2.count, bias);
		}
		discretizeLine.StartEndSize(out startSize2, out endSize2, FaceSmoothLine[heA.fIdx]);
		discretizeLine2.StartEndSize(out startSize, out endSize, FaceSmoothLine[heB.fIdx]);
		if (heA.orient == 1)
		{
			startSize2 = endSize2;
		}
		if (heB.orient == 1)
		{
			endSize = startSize;
		}
		return DiscretizeLine.BySizes(startSize2, endSize, length + length2);
	}

	public Extent GetExtent()
	{
		Extent result = Extent.NullExtent;
		foreach (int faceIterator in FaceIterator())
		{
			result = result.Union(FaceSmoothLine[faceIterator].Extent);
		}
		return result;
	}

	public TinSizeFunction GetGlobalSizeFunction()
	{
		return GetGlobalSizeFunction(new OperationResult());
	}

	public TinSizeFunction GetGlobalSizeFunction(OperationResult opRes)
	{
		if (_globalSizeFunction == null)
		{
			OperationResult operationResult = TriangulateConceptualMesh(out var dynTin, out var triangleLabels);
			if (!operationResult.Result)
			{
				throw new StructuredException(operationResult.Messages);
			}
			bool condition = false;
			foreach (int cellIterator in CellIterator())
			{
				PatchType patchType = CellRegionMetadata[cellIterator].PatchType;
				if (patchType != PatchType.OneD && patchType != PatchType.Empty)
				{
					condition = true;
					break;
				}
			}
			if (!condition)
			{
				for (int i = 0; i < dynTin.Tin.PointCount; i++)
				{
					dynTin.Tin.Z[i] = 1000000.0;
				}
			}
			_globalSizeFunction = GetGlobalSizeFunction(dynTin, triangleLabels, opRes);
		}
		return _globalSizeFunction;
	}

	private TinSizeFunction GetGlobalSizeFunction(DynamicTINFactory<NativePredicates> dynTin, List<int> triangleLabels, OperationResult opRes)
	{
		Tin labelTin = dynTin.Tin.Clone();
		TinSizeFunction.NodeState[] nodeStates = new TinSizeFunction.NodeState[dynTin.Tin.PointCount];
		for (int i = 0; i < dynTin.Tin.PointCount; i++)
		{
			if (i < 8)
			{
				nodeStates[i] = TinSizeFunction.NodeState.FREE;
			}
			else
			{
				nodeStates[i] = (TinSizeFunction.NodeState)dynTin.Tin.M[i];
			}
		}
		List<SizeParams> regionSizeParams = null;
		if (!_globalSizeFnUsesDiffusion)
		{
			regionSizeParams = new List<SizeParams>();
			for (int j = 0; j < CellCount; j++)
			{
				SizeParams sizeParams = new SizeParams(10000000000.0, -1.0, j);
				if (!CellDeleted[j])
				{
					MeshRegionMetadata meshRegionMetadata = CellRegionMetadata[j];
					if (meshRegionMetadata.PatchType == PatchType.Quad)
					{
						sizeParams.stretchRate = meshRegionMetadata.StretchRate;
						sizeParams.target = QuadSizeFunction.InferNominalCellSize(this, j);
					}
					else if (meshRegionMetadata.PatchType == PatchType.TrianglePaver)
					{
						sizeParams.target = meshRegionMetadata.ActualPrimaryCellSize;
						sizeParams.stretchRate = meshRegionMetadata.StretchRate;
					}
					else if (meshRegionMetadata.PatchType == PatchType.Cartesian)
					{
						sizeParams.target = Math.Sqrt(meshRegionMetadata.ActualPrimaryCellSize * meshRegionMetadata.ActualSecondaryConstantSize);
						sizeParams.stretchRate = meshRegionMetadata.StretchRate;
					}
					else if (meshRegionMetadata.PatchType == PatchType.OneD)
					{
						sizeParams.target = 1000000.0;
						sizeParams.stretchRate = 1000000.0;
					}
				}
				regionSizeParams.Add(sizeParams);
			}
		}
		int labelTriangle = 0;
		List<int> intList = new List<int>();
		dynTin.AddInteriorFill(PointToSizeParameters, setSizeInFill: false, intList);
		if (_globalSizeFnUsesDiffusion && _globalSizeFnUsesRegionSize)
		{
			int t = -1;
			foreach (int cellIterator in CellIterator())
			{
				MeshRegionMetadata meshRegionMetadata2 = CellRegionMetadata[cellIterator];
				if (meshRegionMetadata2.UserConstantSize <= 0f || float.IsNaN(meshRegionMetadata2.UserConstantSize))
				{
					continue;
				}
				float userConstantSizeMeshRegionMetadata2Term;
				if (meshRegionMetadata2.PatchType == PatchType.TrianglePaver)
				{
					userConstantSizeMeshRegionMetadata2Term = meshRegionMetadata2.UserConstantSize;
				}
				else
				{
					if (meshRegionMetadata2.PatchType != PatchType.Cartesian)
					{
						continue;
					}
					userConstantSizeMeshRegionMetadata2Term = MathF.Sqrt(meshRegionMetadata2.UserConstantSize * meshRegionMetadata2.ActualSecondaryConstantSize);
				}
				Point bestLabelPoint = CellSmoothComplexPolygon(cellIterator).BestLabelPoint;
				dynTin.Tin.FindTriangleAndNearestPointIndex(bestLabelPoint, ref t, out var p);
				if (dynTin.Tin[p].DistanceSquaredTo(bestLabelPoint) > 0.01 * (double)userConstantSizeMeshRegionMetadata2Term * (double)userConstantSizeMeshRegionMetadata2Term && !dynTin.DynInsertPoint(bestLabelPoint, ref t, translatePoint: false, out p))
				{
					throw new Exception("Why would insertion fail? Point on edge?");
				}
				dynTin.Tin.Z[p] = userConstantSizeMeshRegionMetadata2Term;
				dynTin.Tin.M[p] = 1.0;
			}
		}
		Tin tin = dynTin.Tin;
		TinSizeFunction tinSizeFunction = null;
		if (_globalSizeFnUsesDiffusion)
		{
			tinSizeFunction = new TinSizeFunction(tin, 0.0);
		}
		else
		{
			tinSizeFunction = new TinSizeFunction(tin, PointToSizeParameters, nodeStates);
			List<double> cellCount = new List<double>(CellCount);
			List<double> cellCountList = new List<double>(CellCount);
			for (int k = 0; k < CellCount; k++)
			{
				cellCount.Add(0.0);
				cellCountList.Add(3.4028234663852886E+38);
			}
			for (int l = 0; l < tin.PointCount; l++)
			{
				int ordinal = intList[l];
				if (ordinal >= 0)
				{
					if (tin.Z[l] < cellCountList[ordinal])
					{
						cellCountList[ordinal] = tin.Z[l];
					}
					if (tin.Z[l] > cellCount[ordinal])
					{
						cellCount[ordinal] = tin.Z[l];
					}
				}
			}
			for (int m = 0; m < CellCount; m++)
			{
				MeshRegionMetadata meshRegionMetadata3 = CellRegionMetadata[m];
				if (meshRegionMetadata3.PatchType == PatchType.TrianglePaver && meshRegionMetadata3.HasPrimaryCellSize)
				{
					float userConstantSize = meshRegionMetadata3.UserConstantSize;
					if (cellCountList[m] > 1.01 * (double)userConstantSize || cellCount[m] < 0.99 * (double)userConstantSize)
					{
						opRes.AddMessage(MessageCodes.GetTargetSizeNotReached(meshRegionMetadata3.ControlPointIndex, userConstantSize, cellCountList[m], cellCount[m]));
					}
				}
				else if (meshRegionMetadata3.PatchType == PatchType.Cartesian)
				{
					_ = meshRegionMetadata3.HasPrimaryCellSize;
				}
			}
		}
		return tinSizeFunction;
		SizeParams PointToSizeParameters(Point point)
		{
			if (_globalSizeFnUsesDiffusion)
			{
				return new SizeParams(double.NaN, double.NaN, -1);
			}
			labelTin.FindTriangleContainingPoint(point, ref labelTriangle);
			int triangleLabel = triangleLabels[labelTriangle];
			if (triangleLabel >= 0)
			{
				return regionSizeParams[triangleLabel];
			}
			if (_globalSizeFnUsesDiffusion)
			{
				return new SizeParams(double.NaN, double.NaN, triangleLabel);
			}
			return new SizeParams(10000000000.0, double.NaN, triangleLabel);
		}
	}

	public OperationResult TriangulateConceptualMesh(out DynamicTINFactory<NativePredicates> dynTin, out List<int> triangleLabels, bool forSizeFunction = true, bool errorOnRevisitFace = true, List<(ConstrainedEdge, ConstrainedEdge)> conflicts = null, int regionIdx = -1)
	{
		Dictionary<Segment, Tuple<int, int>> valueTupleBySegment = new Dictionary<Segment, Tuple<int, int>>();
		double ratio = 5.0;
		TriangulationParameters triangulationParameters = null;
		triangulationParameters = new TriangulationParameters();
		triangulationParameters.EnforcedMBR = GetExtent().RatioAroundCenter(ratio);
		dynTin = new DynamicTINFactory<NativePredicates>(triangulationParameters);
		dynTin.errorOnInvalidBoundaries = errorOnRevisitFace;
		if (conflicts != null)
		{
			dynTin.OnEdgeOverConstrained = (ConstrainedEdge edgeA, ConstrainedEdge edgeB) =>
			{
				conflicts.Add((edgeA, edgeB));
			};
		}
		HashSet<int> intSet = new HashSet<int>();
		Polyline[] allSections = new Polyline[FaceCount];
		double[] NodeZ = new double[NodeCount];
		double[] NodeM = new double[NodeCount];
		if (forSizeFunction)
		{
			for (int intervalIndex = 0; intervalIndex < NodeCount; intervalIndex++)
			{
				NodeZ[intervalIndex] = double.NaN;
			}
		}
		else
		{
			for (int nodeM = 0; nodeM < NodeCount; nodeM++)
			{
				NodeZ[nodeM] = -1.0;
				NodeM[nodeM] = nodeM;
			}
		}
		foreach (int faceIterator in FaceIterator())
		{
			if (regionIdx >= 0 && FaceCells[faceIterator].A != regionIdx && FaceCells[faceIterator].B != regionIdx)
			{
				continue;
			}
			Polyline polyline;
			if (forSizeFunction)
			{
				polyline = GetArcForTinSizeFunction(faceIterator);
			}
			else
			{
				polyline = FaceNodeString[faceIterator]?.Clone() ?? FaceSimpleGeometry(faceIterator);
				polyline.TrackZ();
				polyline.TrackM();
				int count = polyline.PointCount - 1;
				polyline.Z[0] = -1.0;
				polyline.M[0] = FaceNodes[faceIterator].A;
				polyline.Z[count] = -1.0;
				polyline.M[count] = FaceNodes[faceIterator].B;
				for (int unity = 1; unity < count; unity++)
				{
					polyline.M[unity] = unity;
					polyline.Z[unity] = faceIterator;
				}
			}
			allSections[faceIterator] = polyline;
			int unity2 = FaceCells[faceIterator].A;
			int unity3 = FaceCells[faceIterator].B;
			if (unity2 >= 0 && CellRegionMetadata[unity2].PatchType == PatchType.Empty)
			{
				unity2 = -1;
			}
			if (unity3 >= 0 && CellRegionMetadata[unity3].PatchType == PatchType.Empty)
			{
				unity3 = -1;
			}
			if (polyline.PointCount == 0)
			{
				throw new Exception("Error triangulating Conceptual Mesh.");
			}
			if (polyline.PointCount == 1)
			{
				throw new StructuredException(Message.GenericError("Error triangulating Conceptual Mesh near {Coordinate}.", polyline.Points[0]));
			}
			valueTupleBySegment[polyline.Segments[0]] = new Tuple<int, int>(unity2, unity3);
		}
		if (forSizeFunction)
		{
			double computed = 1000000.0;
			double unity4 = 1.0;
			double tolerance = 1E-06;
			foreach (int nodeIterator in NodeIterator())
			{
				double computed2 = 0.0;
				double computed3 = 0.0;
				double computed4 = 0.0;
				foreach (HalfEdge nodeToHalfEdge in NodeToHalfEdges(nodeIterator))
				{
					int a = FaceCells[nodeToHalfEdge.fIdx].A;
					int b = FaceCells[nodeToHalfEdge.fIdx].B;
					PatchType patchType = ((a >= 0) ? CellRegionMetadata[a].PatchType : PatchType.Empty);
					PatchType patchType2 = ((b >= 0) ? CellRegionMetadata[b].PatchType : PatchType.Empty);
					Polyline polyline2 = allSections[nodeToHalfEdge.fIdx];
					double orientNodeToHalfEdgeTerm = ((nodeToHalfEdge.orient == 0) ? polyline2.Z[0] : polyline2.Z.Last());
					double orientNodeToHalfEdgeTerm2 = ((nodeToHalfEdge.orient == 0) ? polyline2.M[0] : polyline2.M.Last());
					if (!double.IsNaN(orientNodeToHalfEdgeTerm))
					{
						bool patchTypeQuadTerm = (patchType != PatchType.Quad && patchType != PatchType.Empty) || (patchType2 != PatchType.Quad && patchType2 != PatchType.Empty);
						bool hasFaceNodeString = FaceNodeString[nodeToHalfEdge.fIdx] != null;
						double hasFaceNodeStringTerm;
						if (patchTypeQuadTerm)
						{
							hasFaceNodeStringTerm = ((!hasFaceNodeString) ? unity4 : computed);
						}
						else
						{
							hasFaceNodeStringTerm = tolerance;
						}
						computed2 += hasFaceNodeStringTerm;
						computed3 += hasFaceNodeStringTerm * orientNodeToHalfEdgeTerm;
						computed4 += hasFaceNodeStringTerm * orientNodeToHalfEdgeTerm2;
					}
				}
				if (computed2 == 0.0)
				{
					NodeZ[nodeIterator] = double.NaN;
					NodeM[nodeIterator] = double.NaN;
				}
				else
				{
					NodeZ[nodeIterator] = computed3 / computed2;
					NodeM[nodeIterator] = ((computed4 / computed2 > 0.5) ? 1.0 : 0.0);
				}
			}
		}
		foreach (int faceIterator2 in FaceIterator())
		{
			if ((regionIdx >= 0 && FaceCells[faceIterator2].A != regionIdx && FaceCells[faceIterator2].B != regionIdx) || intSet.Contains(faceIterator2))
			{
				continue;
			}
			HalfEdge halfEdge = new HalfEdge(faceIterator2, 0);
			if (!IsBoundary(halfEdge))
			{
				halfEdge.orient = 1;
			}
			else if (IsBoundary(HalfEdgeFlip(halfEdge)))
			{
				intSet.Add(faceIterator2);
				continue;
			}
			if (!IsBoundary(halfEdge))
			{
				intSet.Add(faceIterator2);
				Polyline polyline3 = GetOrientedSection(halfEdge);
				if (polyline3 != null)
				{
					dynTin.AddBreakline(polyline3);
				}
				continue;
			}
			Polygon polygon = new Polygon();
			HalfEdge halfEdge2 = halfEdge;
			for (int ordinal = 0; ordinal < 2 * FaceCount; ordinal++)
			{
				HalfEdge he = HalfEdgeFlip(halfEdge);
				if (IsBoundary(he))
				{
					intSet.Add(halfEdge.fIdx);
					halfEdge = HalfEdgeNext(he);
					if (halfEdge == halfEdge2)
					{
						break;
					}
					continue;
				}
				Polyline polyline4 = GetOrientedSection(halfEdge);
				polyline4.RemoveAt(polyline4.Points.Count - 1);
				polygon.AddRange(polyline4);
				if (intSet.Contains(halfEdge.fIdx))
				{
					triangleLabels = null;
					return OperationResult.Fail(MessageCodes.GetVisitedFaceTwice(faceIterator2, polygon.Last()));
				}
				intSet.Add(halfEdge.fIdx);
				halfEdge = HalfEdgeNext(halfEdge);
				if (halfEdge == halfEdge2)
				{
					break;
				}
			}
			_ = halfEdge != halfEdge2;
			dynTin.AddPerimeter(polygon);
		}
		dynTin.ComputeTIN();
		return dynTin.TryLabelTrianglesFromConstraints(valueTupleBySegment, out triangleLabels);
		Polyline GetOrientedSection(HalfEdge halfEdge3)
		{
			Polyline polyline5 = allSections[halfEdge3.fIdx];
			if (polyline5 != null)
			{
				if (forSizeFunction)
				{
					polyline5.Z[0] = NodeZ[FaceNodes[halfEdge3.fIdx].A];
					polyline5.M[0] = NodeM[FaceNodes[halfEdge3.fIdx].A];
					polyline5.Z[polyline5.Count - 1] = NodeZ[FaceNodes[halfEdge3.fIdx].B];
					polyline5.M[polyline5.Count - 1] = NodeM[FaceNodes[halfEdge3.fIdx].B];
				}
				if (halfEdge3.orient == 1)
				{
					polyline5 = polyline5.Clone();
					polyline5.Reverse();
				}
			}
			return polyline5;
		}
		bool IsBoundary(HalfEdge he2)
		{
			int halfEdgeToCell = HalfEdgeToCell(he2);
			if (halfEdgeToCell < 0)
			{
				return true;
			}
			if (CellRegionMetadata[halfEdgeToCell].PatchType == PatchType.Empty)
			{
				return true;
			}
			return false;
		}
	}

	public OperationResult TriangulateRegion(out DynamicTINFactory<NativePredicates> dynTin, int regionIdx, Func<int, Polyline> FaceGeometry, double expandMBR = 0.2, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		List<Polygon> cellGenericComplexRings = CellGenericComplexRings(regionIdx, FaceGeometry, out var _);
		TriangulationParameters triangulationParameters = null;
		if (expandMBR > 1.0)
		{
			triangulationParameters = new TriangulationParameters();
			triangulationParameters.EnforcedMBR = cellGenericComplexRings[0].Extent.RatioAroundCenter(expandMBR);
		}
		dynTin = new DynamicTINFactory<NativePredicates>(triangulationParameters);
		foreach (Polygon cellGenericComplexRing in cellGenericComplexRings)
		{
			dynTin.AddPerimeter(cellGenericComplexRing);
		}
		List<Polyline> breaklines = CellBreaklines(regionIdx).Select(FaceGeometry).ToList();
		dynTin.AddBreaklines(breaklines);
		dynTin.ComputeTIN(reporter);
		return new OperationResult();
	}

	public override double CycleArea(List<HalfEdge> cycle, bool simpleGeometry)
	{
		if (simpleGeometry)
		{
			return base.CycleArea(cycle, simpleGeometry);
		}
		Polygon polygon = new Polygon(cycle.Count);
		foreach (HalfEdge cycle2 in cycle)
		{
			Polyline polyline = FaceSmoothLine[cycle2.fIdx];
			if (cycle2.orient == 0)
			{
				polygon.AddRange(polyline);
			}
			else
			{
				polygon.AddRangeReverse(polyline);
			}
			polygon.RemoveAt(polygon.Count - 1);
		}
		return polygon.Area;
	}

	private Polyline GetArcForTinSizeFunction(int fIdx)
	{
		PatchType patchType = ((FaceCells[fIdx].A >= 0) ? CellRegionMetadata[FaceCells[fIdx].A].PatchType : PatchType.Empty);
		PatchType patchType2 = ((FaceCells[fIdx].B >= 0) ? CellRegionMetadata[FaceCells[fIdx].B].PatchType : PatchType.Empty);
		HalfEdgeIsInterior(new HalfEdge(fIdx, 0));
		HalfEdgeIsInterior(new HalfEdge(fIdx, 1));
		bool condition = false;
		HalfEdge he = new HalfEdge(fIdx, 0);
		bool geometryFromNodes = _globalSizeFnUsesNodes & condition;
		Polyline polyline = OrientedArcWithScale(he, condition, geometryFromNodes);
		polyline.TrackM();
		DiscretizeLine discretizeLine = FaceDiscretization[fIdx];
		double unity = 0.0;
		if ((patchType == PatchType.OneD || patchType == PatchType.Empty) && (patchType2 == PatchType.OneD || patchType2 == PatchType.Empty))
		{
			unity = 0.0;
			for (int i = 0; i < polyline.PointCount; i++)
			{
				polyline.Z[i] = double.NaN;
			}
		}
		else if (discretizeLine.ConstrainMode == ConstrainStretchingOptions.Constrained)
		{
			unity = 1.0;
		}
		else if (condition)
		{
			unity = (((patchType != PatchType.Quad || patchType2 != PatchType.Empty) && (patchType2 != PatchType.Quad || patchType != PatchType.Empty)) ? 1.0 : 0.0);
		}
		else if (patchType == PatchType.Quad || patchType2 == PatchType.Quad)
		{
			unity = 1.0;
		}
		for (int j = 0; j < polyline.PointCount; j++)
		{
			polyline.M[j] = unity;
		}
		return polyline;
	}

	public int HashRegion(int cIdx)
	{
		int combine = 0;
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			Polyline polyline = FaceRawLine[cellFace.fIdx];
			for (int i = 0; i < polyline.Count; i++)
			{
				combine = HashCode.Combine(combine, polyline[i].GetHashCode());
			}
			combine = HashCode.Combine(combine, FaceDiscretization[cellFace.fIdx]?.GetHashCode());
		}
		return HashCode.Combine(combine, CellRegionMetadata[cIdx].TINResolution);
	}

	public List<PatchSide> PatchSides(int cIdx, VirtualConstraints constraints = null, bool useSize = false)
	{
		return PatchSide.FromConceptualMesh(this, cIdx, constraints, useSize);
	}

	public bool IsEmpty(int cIdx)
	{
		if (cIdx >= 0)
		{
			return CellRegionMetadata[cIdx].PatchType == PatchType.Empty;
		}
		return true;
	}

	public QuadTransformLaplacian GetQuadTransform(int regionIdx, ProgressReporter cancelReporter = null)
	{
		List<PatchSide> patchSides = PatchSides(regionIdx);
		(Point, Point, Point, Point) corners = (patchSides[0].Smooth[0], patchSides[1].Smooth[0], patchSides[2].Smooth[0], patchSides[3].Smooth[0]);
		QuadTransformLaplacian quadTransformLaplacian = null;
		if (cache != null)
		{
			quadTransformLaplacian = cache.TryGetQuadTransform(this, regionIdx, corners);
		}
		if (quadTransformLaplacian == null)
		{
			quadTransformLaplacian = new QuadTransformLaplacian(this, regionIdx, patchSides, cancelReporter);
			if (cache != null)
			{
				cache.SetQuadTransform(this, regionIdx, corners, quadTransformLaplacian);
			}
		}
		return quadTransformLaplacian;
	}

	public override double HalfEdgeBearing(HalfEdge he)
	{
		Polyline polyline = FaceSmoothLine[he.fIdx];
		Point point;
		Point point2;
		if (he.orient == 0)
		{
			point = polyline[0];
			point2 = polyline[1];
		}
		else
		{
			point = polyline[polyline.Count - 1];
			point2 = polyline[polyline.Count - 2];
		}
		return (point2 - point).MathBearing;
	}

	public override HalfEdge HalfEdgeNextByCell(HalfEdge he)
	{
		try
		{
			return base.HalfEdgeNextByCell(he);
		}
		catch (DynamicMeshException)
		{
			int cIdx = HalfEdgeToCell(he);
			if (CellIslandCells(cIdx).Contains(HalfEdgeToOppositeCell(he)))
			{
				return HalfEdgeNextByNode(he);
			}
			throw;
		}
	}

	public Polyline OrientedArcWithScale(HalfEdge he, bool scaleFromNodes, bool geometryFromNodes, bool upsample = true)
	{
		Polyline polyline = null;
		Polyline polyline2 = ((!geometryFromNodes) ? FaceSmoothLine[he.fIdx] : FaceNodeString[he.fIdx]);
		if (scaleFromNodes && !geometryFromNodes)
		{
			polyline = FaceNodeString[he.fIdx];
		}
		polyline2 = polyline2.Clone();
		DiscretizeLine discretizeLine = null;
		double[] cumulativeSegmentLengths = null;
		double r = 1.0;
		double startSize = -1.0;
		if (!scaleFromNodes)
		{
			discretizeLine = FaceDiscretization[he.fIdx];
			cumulativeSegmentLengths = polyline2.CumulativeSegmentLengths();
			double length = polyline2.Length;
			if (!_globalSizeFnUsesDiffusion && (discretizeLine == null || !discretizeLine.ValidSize()))
			{
				double maxValue = double.MaxValue;
				for (byte b = 0; b < 2; b++)
				{
					int faceCell = FaceCells[he.fIdx][b];
					if (faceCell < 0)
					{
						continue;
					}
					MeshRegionMetadata meshRegionMetadata = CellRegionMetadata[faceCell];
					double unity = -1.0;
					if (meshRegionMetadata.PatchType == PatchType.TrianglePaver && double.IsFinite(meshRegionMetadata.UserConstantSize))
					{
						unity = meshRegionMetadata.UserConstantSize;
					}
					else
					{
						if (meshRegionMetadata.PatchType != PatchType.Cartesian || !double.IsFinite(meshRegionMetadata.UserConstantSize))
						{
							continue;
						}
						unity = Math.Sqrt(meshRegionMetadata.UserConstantSize * meshRegionMetadata.ActualSecondaryConstantSize);
					}
					maxValue = Math.Min(maxValue, unity);
				}
				if (maxValue != double.MaxValue)
				{
					discretizeLine = DiscretizeLine.BySize(maxValue, 1.0, length);
				}
			}
			if (discretizeLine != null && discretizeLine.ValidSize())
			{
				_ = discretizeLine.bias;
				if (discretizeLine.Mode != SizeFunctionMode.Tin)
				{
					discretizeLine.StartEndSize(out startSize, out var _, polyline2, out r);
				}
			}
		}
		polyline2.TrackZ();
		int ordinal = 0;
		double distanceAlongFast = 0.0;
		float distanceTo = 0f;
		for (int i = 0; i < polyline2.Count; i++)
		{
			float naN = float.NaN;
			if (geometryFromNodes & scaleFromNodes)
			{
				if (i != 0)
				{
					naN = ((i + 1 != polyline2.Count) ? (0.5f * (float)polyline2[i - 1].DistanceTo(polyline2[i + 1])) : ((float)polyline2[i - 1].DistanceTo(polyline2[i])));
				}
				else
				{
					naN = (float)polyline2[0].DistanceTo(polyline2[1]);
				}
			}
			else if (!scaleFromNodes)
			{
				if (discretizeLine != null)
				{
					if (discretizeLine.Mode == SizeFunctionMode.Tin)
					{
						naN = (float)discretizeLine.sizeFunction.Query(polyline2[i]);
					}
					else if (discretizeLine.Mode == SizeFunctionMode.Unset)
					{
						naN = float.NaN;
					}
					else
					{
						double cumulativeSegmentLengthsStartSizeTerm = ((i > 0) ? cumulativeSegmentLengths[i - 1] : 0.0) - 0.5 * startSize;
						naN = (float)(startSize + (r - 1.0) * cumulativeSegmentLengthsStartSizeTerm);
					}
				}
			}
			else
			{
				double cumulativeSegmentLength = ((i != 0) ? cumulativeSegmentLengths[i - 1] : 0.0);
				if ((i == 0 || cumulativeSegmentLength > distanceAlongFast) && ordinal + 1 < polyline.Count)
				{
					ordinal++;
					PointOnPolyline ptOnPoly = polyline2.NearestPointOnPolyline(polyline[ordinal]);
					distanceAlongFast = polyline2.DistanceAlongFast(ptOnPoly, ref cumulativeSegmentLengths);
					distanceTo = (float)polyline[ordinal - 1].DistanceTo(polyline[ordinal]);
				}
				naN = distanceTo;
			}
			polyline2.Z[i] = naN;
		}
		if (!geometryFromNodes & upsample)
		{
			Polyline polyline3 = new Polyline();
			polyline3.TrackZ();
			polyline3.Add(polyline2[0], polyline2.Z[0]);
			for (int j = 0; j < polyline2.Count; j++)
			{
				if (j > 0)
				{
					_ = polyline2.Z[j];
					DiscretizeLine.UpsampleSegment(polyline2[j - 1], polyline2[j], polyline2.Z[j - 1], polyline2.Z[j], polyline3);
				}
			}
			polyline2 = polyline3;
		}
		if (he.orient == 1)
		{
			polyline2.Reverse();
		}
		return polyline2;
	}

	public int SplitArc(Point point, double tolerance)
	{
		double maxValue = double.MaxValue;
		int faceIteratorTerm = -1;
		PointOnPolyline point2 = PointOnPolyline.NullPointOnPolyline;
		foreach (int faceIterator in FaceIterator())
		{
			PointOnPolyline pointOnPolyline = FaceRawLine[faceIterator].NearestPointOnPolyline(point, out var actualDistance);
			if (actualDistance < maxValue && actualDistance < tolerance)
			{
				maxValue = actualDistance;
				faceIteratorTerm = faceIterator;
				point2 = pointOnPolyline;
			}
		}
		if (faceIteratorTerm < 0)
		{
			return -1;
		}
		int ordinal = faceIteratorTerm;
		int faceCell = FaceCells[ordinal][0];
		int faceCell2 = FaceCells[ordinal][1];
		Polyline polyline = FaceRawLine[ordinal];
		if (point2.Point == polyline.First() || point2.Point == polyline.Last())
		{
			return -1;
		}
		int addNode = AddNode(point2.Point);
		int nodeB = FaceNodes[ordinal][1];
		SetFaceNode(ordinal, 1, addNode);
		FaceRawLine[ordinal] = polyline.SubsectionBefore(point2);
		FaceNodeString[ordinal] = FaceSimpleGeometry(ordinal);
		int addFace = AddFace(addNode, nodeB);
		FaceRawLine[addFace] = polyline.SubsectionAfter(point2);
		FaceNodeString[addFace] = FaceSimpleGeometry(addFace);
		if (FaceDiscretization[ordinal] != null)
		{
			DiscretizeLine discretizeLine = FaceDiscretization[ordinal];
			if (discretizeLine.sizeFunction != null)
			{
				FaceDiscretization[addFace] = new DiscretizeLine(discretizeLine.sizeFunction);
			}
			else
			{
				double length = FaceRawLine[ordinal].Length;
				double length2 = FaceRawLine[addFace].Length;
				int countDiscretizeLineTerm = 1 + (int)((double)(discretizeLine.count - 1) * length / (length + length2));
				_ = discretizeLine.count;
				FaceDiscretization[ordinal] = new DiscretizeLine(Math.Max(countDiscretizeLineTerm, 2), discretizeLine.bias);
				FaceDiscretization[addFace] = new DiscretizeLine(Math.Max(countDiscretizeLineTerm, 2), discretizeLine.bias);
			}
		}
		if (faceCell >= 0)
		{
			List<HalfEdge> cellFaces = new List<HalfEdge>();
			foreach (HalfEdge cellFace in CellFaces[faceCell])
			{
				if (cellFace.fIdx != ordinal)
				{
					cellFaces.Add(cellFace);
					continue;
				}
				cellFaces.Add(cellFace);
				cellFaces.Add(new HalfEdge(addFace, 0));
			}
			CellFaces[faceCell] = cellFaces;
			SetFaceCell(addFace, 0, faceCell);
		}
		if (faceCell2 >= 0)
		{
			List<HalfEdge> cellFacesList = new List<HalfEdge>();
			foreach (HalfEdge cellFace2 in CellFaces[faceCell2])
			{
				if (cellFace2.fIdx != ordinal)
				{
					cellFacesList.Add(cellFace2);
					continue;
				}
				cellFacesList.Add(new HalfEdge(addFace, 1));
				cellFacesList.Add(cellFace2);
			}
			CellFaces[faceCell2] = cellFacesList;
			SetFaceCell(addFace, 1, faceCell2);
		}
		return addNode;
	}

	public VirtualConstraints.ConstraintMode ArcRigidity(int fIdx)
	{
		if (FaceDiscretization[fIdx] != null && FaceDiscretization[fIdx].ConstrainMode == ConstrainStretchingOptions.Constrained)
		{
			return VirtualConstraints.ConstraintMode.FIXED;
		}
		Duple<int> duple = FaceCells[fIdx];
		if (duple.A < 0 || duple.B < 0)
		{
			return VirtualConstraints.ConstraintMode.SLIDE_ADD_REMOVE;
		}
		if (duple.A == duple.B)
		{
			return VirtualConstraints.ConstraintMode.SLIDE_ADD_REMOVE;
		}
		if (IsEmpty(duple.A) || IsEmpty(duple.B))
		{
			return VirtualConstraints.ConstraintMode.SLIDE_ADD_REMOVE;
		}
		return VirtualConstraints.ConstraintMode.FIXED;
	}

	public List<int> GetQuadRegions()
	{
		List<int> quadRegions = new List<int>();
		for (int i = 0; i < CellCount; i++)
		{
			if (CellRegionMetadata[i].PatchType == PatchType.Quad)
			{
				quadRegions.Add(i);
			}
		}
		return quadRegions;
	}

	public List<Point> RegionCentroids(List<int> regionSubset = null)
	{
		IReadOnlyCollection<int> readOnlyCollection = ((regionSubset != null) ? regionSubset : Enumerable.Range(0, CellCount).ToList());
		List<Point> points = new List<Point>(readOnlyCollection.Count);
		foreach (int readOnlyCollection2 in readOnlyCollection)
		{
			try
			{
				Point nullPoint = CellNodePolygon(readOnlyCollection2)?.ComputeCentroid() ?? Point.NullPoint;
				points.Add(nullPoint);
			}
			catch
			{
				points.Add(Point.NullPoint);
			}
		}
		return points;
	}

	public int RegionFarthestFromAverageCentroid(List<int> regionSubset = null)
	{
		List<int> regionSubsetList = ((regionSubset != null) ? regionSubset : Enumerable.Range(0, CellCount).ToList());
		if (regionSubsetList == null || regionSubsetList.Count == 0)
		{
			return -1;
		}
		List<Point> regionCentroids = RegionCentroids(regionSubsetList);
		if (regionCentroids == null || regionCentroids.Count == 0)
		{
			return -1;
		}
		double computed = 0.0;
		double computed2 = 0.0;
		int ordinal = 0;
		for (int i = 0; i < regionCentroids.Count; i++)
		{
			Point point = regionCentroids[i];
			if (point.IsValid())
			{
				computed += point.X;
				computed2 += point.Y;
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			return -1;
		}
		Point other = new Point(computed / (double)ordinal, computed2 / (double)ordinal);
		double negativeInfinity = double.NegativeInfinity;
		int ordinal2 = -1;
		for (int j = 0; j < regionCentroids.Count; j++)
		{
			Point point2 = regionCentroids[j];
			if (point2.IsValid())
			{
				double distanceSquaredTo = point2.DistanceSquaredTo(other);
				if (distanceSquaredTo > negativeInfinity)
				{
					negativeInfinity = distanceSquaredTo;
					ordinal2 = j;
				}
			}
		}
		if (ordinal2 < 0)
		{
			return -1;
		}
		return regionSubsetList[ordinal2];
	}

	public override void DumpToJson(string filename = "C:\\Temp\\cmd.json")
	{
		List<object> localValue = new List<object>
		{
			new List<object> { "SET", "cg_raw", this },
			new List<object> { "PYTHON", "\r\ncg=json_to_mesh(cg_raw)\r\nfig,ax = plt.subplots(num = 2, clear = 1,figsize=(11,9))\r\nax.set_adjustable('datalim')\r\nax.set_position([0,0,1,1])\r\nif 'rawStringXY' in cg.edges.dtype.names:\r\n    cg.plot_edges(ax = ax, color = 'k', lw = 0.8, alpha = 0.6, subedges='rawStringXY')\r\nelse:\r\n    cg.plot_edges(ax = ax, color = 'k', lw = 0.8, alpha = 0.6)\r\nax.axis('equal')\r\n" }
		};
		using FileStream utf8Json = File.Create(filename);
		JsonSerializer.Serialize(utf8Json, localValue, new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		});
	}
}

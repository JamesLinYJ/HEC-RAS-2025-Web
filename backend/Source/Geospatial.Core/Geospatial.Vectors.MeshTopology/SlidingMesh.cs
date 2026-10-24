using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.DataStructures;
using Geospatial.Transformers;
using Geospatial.Vectors.Topology;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

[JsonConverter(typeof(SlidingMeshJsonConverter))]
public class SlidingMesh : DynamicMesh
{
	public enum CellType
	{
		CELL_2D,
		CELL_1D_REACH,
		CELL_1D_JUNCTION,
		CELL_STORAGE
	}

	public class Cell1DSide
	{
		public List<int> SideUpstream;

		public List<int> SideDownstream;

		public List<int> SideLeft;

		public List<int> SideRight;
	}

	public class SlidingMeshJsonConverter : JsonConverter<SlidingMesh>
	{
		public override SlidingMesh Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, SlidingMesh obj, JsonSerializerOptions options)
		{
			string json = JsonSerializer.Serialize((object)obj.SimpleRepr(), options);
			writer.WriteRawValue(json);
		}
	}

	public class CollapseFacePreflightInfo
	{
		public HalfEdge addTo;

		public List<Polygon> newSimpleCells = new List<Polygon>();

		public List<Polygon> newSmoothCells = new List<Polygon>();

		public List<HalfEdge> spokes;

		public Polyline plineAE;

		public CollapseFacePreflightInfo(List<HalfEdge> spokes, HalfEdge addTo)
		{
			this.spokes = spokes;
			this.addTo = addTo;
		}
	}

	public enum ClipMode
	{
		RaggedCells,
		RaggedWithScale
	}

	public enum RaggedClipMode
	{
		SMOOTH,
		NODES,
		SMOOTH_AND_NODES
	}

	private const ConstraintState MinNodeConstraint = ConstraintState.FIXED;

	private const ConstraintState MinFaceConstraint = ConstraintState.FREE;

	public List<ConstraintState> NodeConstraints;

	public List<PointOnPolyline> NodePointOnConstraint;

	public static double DefaultCircumcenterClearance = 1.0;

	public List<ConstraintState> FaceConstraints;

	public List<float> FaceTargetLength;

	public List<Polyline> FaceSmoothLine;

	public List<Cell1DSide> CellSides;

	public List<CellType> CellTypes;

	public VirtualConstraints constraints;

	public ISpatialTransformer transform;

	public override int AddNode(Point point)
	{
		int result = base.AddNode(point);
		NodeConstraints.Add(ConstraintState.FREE);
		NodePointOnConstraint.Add(PointOnPolyline.NullPointOnPolyline);
		return result;
	}

	public override int AddFace(int nodeA, int nodeB, int cellA = -99, int cellB = -99)
	{
		int result = base.AddFace(nodeA, nodeB, cellA, cellB);
		FaceConstraints.Add(ConstraintState.FREE);
		FaceTargetLength.Add(-1f);
		FaceSmoothLine.Add(null);
		return result;
	}

	public override int AddCell()
	{
		int result = base.AddCell();
		CellTypes.Add(CellType.CELL_2D);
		CellSides.Add(null);
		return result;
	}

	public override void DeleteCell(int cIdx)
	{
		CellTypes[cIdx] = CellType.CELL_2D;
		CellSides[cIdx] = null;
		base.DeleteCell(cIdx);
	}

	public SlidingMesh(DynamicMesh dMesh)
		: base(dMesh)
	{
		NodeConstraints = Enumerable.Repeat(ConstraintState.FREE, NodeCount).ToList();
		NodePointOnConstraint = Enumerable.Repeat(PointOnPolyline.NullPointOnPolyline, NodeCount).ToList();
		FaceConstraints = Enumerable.Repeat(ConstraintState.FREE, FaceCount).ToList();
		FaceTargetLength = Enumerable.Repeat(-1f, FaceCount).ToList();
		FaceSmoothLine = Enumerable.Repeat<Polyline>(null, FaceCount).ToList();
		CellSides = new List<Cell1DSide>(new Cell1DSide[CellCount]);
		CellTypes = Enumerable.Repeat(CellType.CELL_2D, CellCount).ToList();
		constraints = new VirtualConstraints();
	}

	public SlidingMesh(Mesh mesh)
		: this()
	{
		for (int i = 0; i < mesh.NodeCount; i++)
		{
			AddNode(mesh.FacePoints[i].Point);
		}
		for (int j = 0; j < mesh.FaceCount; j++)
		{
			Face face = mesh.Faces[j];
			AddFace(face.fpA, face.fpB);
			FaceSmoothLine[j] = mesh.FacePolyline(j);
		}
		for (int k = 0; k < mesh.CellCount; k++)
		{
			AddCell();
			Span<int> faces = mesh.Cells[k].Faces.Span;
			for (int l = 0; l < faces.Length; l++)
			{
				int faceIndex = faces[l];
				int cellAFacesTerm = ((mesh.Faces[faceIndex].cellA != k) ? 1 : 0);
				CellAddFace(k, faceIndex, (byte)cellAFacesTerm);
			}
		}
	}

	public SlidingMesh()
	{
		NodeConstraints = new List<ConstraintState>();
		NodePointOnConstraint = new List<PointOnPolyline>();
		FaceConstraints = new List<ConstraintState>();
		FaceTargetLength = new List<float>();
		FaceSmoothLine = new List<Polyline>();
		CellSides = new List<Cell1DSide>();
		CellTypes = new List<CellType>();
		constraints = new VirtualConstraints();
	}

	public List<SlidingMesh> Split(bool splitTangents)
	{
		List<SlidingMesh> slidingMeshes = new List<SlidingMesh>();
		bool[] nodeVisited = null;
		bool[] faceVisited = null;
		Dictionary<int, int> nodeMap = new Dictionary<int, int>();
		Dictionary<int, int> faceMap = new Dictionary<int, int>();
		Dictionary<int, int> cellMap = new Dictionary<int, int>();
		HashSet<int> nodeQueue = new HashSet<int>();
		HashSet<int> faceQueue = new HashSet<int>();
		HashSet<int> cellQueue = new HashSet<int>();
		SlidingMesh subset = null;
		if (!splitTangents)
		{
			nodeVisited = new bool[NodeCount];
			foreach (int nodeIterator in NodeIterator())
			{
				if (nodeVisited[nodeIterator])
				{
					continue;
				}
				subset = new SlidingMesh
				{
					constraints = constraints
				};
				nodeMap.Clear();
				faceMap.Clear();
				cellMap.Clear();
				faceQueue.Clear();
				cellQueue.Clear();
				nodeQueue.Add(nodeIterator);
				while (nodeQueue.Count > 0)
				{
					int nodeQueueTerm = nodeQueue.First();
					nodeQueue.Remove(nodeQueueTerm);
					if (!nodeVisited[nodeQueueTerm])
					{
						CopyNode(nodeQueueTerm);
					}
				}
				foreach (int faceQueue2 in faceQueue)
				{
					CopyFace(faceQueue2);
				}
				foreach (int cellQueue2 in cellQueue)
				{
					CopyCell(cellQueue2);
				}
				slidingMeshes.Add(subset);
				subset = null;
			}
		}
		else
		{
			faceVisited = new bool[FaceCount];
			foreach (int faceIterator in FaceIterator())
			{
				if (faceVisited[faceIterator])
				{
					continue;
				}
				subset = new SlidingMesh
				{
					constraints = constraints
				};
				nodeMap.Clear();
				faceMap.Clear();
				cellMap.Clear();
				faceQueue.Clear();
				cellQueue.Clear();
				faceQueue.Add(faceIterator);
				while (faceQueue.Count > 0)
				{
					int faceQueueTerm = faceQueue.First();
					faceQueue.Remove(faceQueueTerm);
					if (!faceVisited[faceQueueTerm])
					{
						CopyFace(faceQueueTerm);
					}
				}
				foreach (int cellQueue3 in cellQueue)
				{
					CopyCell(cellQueue3);
				}
				slidingMeshes.Add(subset);
				subset = null;
			}
		}
		return slidingMeshes;
		void CopyCell(int cIdx)
		{
			int cellMap2 = subset.AddCell();
			foreach (HalfEdge cellFace in CellFaces[cIdx])
			{
				int fIdx = faceMap[cellFace.fIdx];
				subset.CellAddFace(cellMap2, fIdx, cellFace.orient);
			}
			subset.CellTypes[cellMap2] = CellTypes[cIdx];
			subset.CellSides[cellMap2] = CellSides[cIdx];
			cellMap[cIdx] = cellMap2;
		}
		int CopyFace(int fIdx)
		{
			int copyNodeFaceNodesTerm;
			int copyNodeFaceNodesTerm2;
			if (splitTangents)
			{
				faceVisited[fIdx] = true;
				if (!nodeMap.TryGetValue(FaceNodes[fIdx].A, out copyNodeFaceNodesTerm))
				{
					copyNodeFaceNodesTerm = CopyNode(FaceNodes[fIdx].A);
				}
				if (!nodeMap.TryGetValue(FaceNodes[fIdx].B, out copyNodeFaceNodesTerm2))
				{
					copyNodeFaceNodesTerm2 = CopyNode(FaceNodes[fIdx].B);
				}
			}
			else
			{
				copyNodeFaceNodesTerm = nodeMap[FaceNodes[fIdx].A];
				copyNodeFaceNodesTerm2 = nodeMap[FaceNodes[fIdx].B];
			}
			int faceMap2 = subset.AddFace(copyNodeFaceNodesTerm, copyNodeFaceNodesTerm2);
			subset.FaceTargetLength[faceMap2] = FaceTargetLength[fIdx];
			subset.FaceConstraints[faceMap2] = FaceConstraints[fIdx];
			subset.FaceSmoothLine[faceMap2] = FaceSmoothLine[fIdx];
			faceMap[fIdx] = faceMap2;
			if (splitTangents)
			{
				for (byte b = 0; b < 2; b++)
				{
					int faceCell = FaceCells[fIdx][b];
					if (faceCell < 0)
					{
						subset.SetFaceCell(faceMap2, b, faceCell);
					}
					else if (!cellQueue.Contains(faceCell))
					{
						cellQueue.Add(faceCell);
						foreach (HalfEdge cellFace2 in CellFaces[faceCell])
						{
							if (!faceVisited[cellFace2.fIdx])
							{
								faceQueue.Add(cellFace2.fIdx);
							}
						}
					}
				}
			}
			else
			{
				if (FaceCells[fIdx].A >= 0)
				{
					cellQueue.Add(FaceCells[fIdx].A);
				}
				if (FaceCells[fIdx].B >= 0)
				{
					cellQueue.Add(FaceCells[fIdx].B);
				}
			}
			return faceMap2;
		}
		int CopyNode(int nIdx)
		{
			int nodeMap2 = subset.AddNode(NodePoints[nIdx]);
			nodeMap[nIdx] = nodeMap2;
			subset.NodeConstraints[nodeMap2] = NodeConstraints[nIdx];
			subset.NodePointOnConstraint[nodeMap2] = NodePointOnConstraint[nIdx];
			if (!splitTangents)
			{
				nodeVisited[nIdx] = true;
				foreach (int nodeToEdge in NodeToEdges(nIdx))
				{
					foreach (int faceNode in FaceNodes[nodeToEdge])
					{
						if (!nodeVisited[faceNode])
						{
							nodeQueue.Add(faceNode);
						}
					}
					if (!faceMap.ContainsKey(nodeToEdge))
					{
						faceQueue.Add(nodeToEdge);
					}
				}
			}
			return nodeMap2;
		}
	}

	public SlidingMesh SubsetMesh(int nIdx, out List<int> nodeMap, out List<int> faceMap, out List<int> cellMap)
	{
		SlidingMesh slidingMesh = new SlidingMesh();
		List<HalfEdge> nodeToSortedHalfEdges = NodeToSortedHalfEdges(nIdx);
		nodeMap = new List<int>(1 + 2 * nodeToSortedHalfEdges.Count);
		faceMap = new List<int>(3 * nodeToSortedHalfEdges.Count);
		cellMap = new List<int>(nodeToSortedHalfEdges.Count);
		slidingMesh.AddNode(NodePoints[nIdx]);
		nodeMap.Add(nIdx);
		int unity = -1;
		for (int i = 0; i < nodeToSortedHalfEdges.Count; i++)
		{
			HalfEdge he = nodeToSortedHalfEdges[i];
			int nodeA;
			if (unity < 0)
			{
				int halfEdgeNodeForward = HalfEdgeNodeForward(he);
				nodeA = nodeMap.Count;
				nodeMap.Add(halfEdgeNodeForward);
				slidingMesh.AddNode(NodePoints[halfEdgeNodeForward]);
				unity = faceMap.Count;
				faceMap.Add(he.fIdx);
				slidingMesh.AddFace(nodeA, 0);
			}
			else
			{
				nodeA = slidingMesh.FaceNodes[unity].A;
			}
			int halfEdgeToCell = HalfEdgeToCell(he);
			if (halfEdgeToCell < 0)
			{
				unity = -1;
				continue;
			}
			int count = cellMap.Count;
			cellMap.Add(halfEdgeToCell);
			slidingMesh.AddCell();
			slidingMesh.CellAddFace(count, unity, 1);
			for (int ordinal = 8; ordinal >= 0; ordinal--)
			{
				he = HalfEdgeNextByCell(he);
				int halfEdgeNodeForward2 = HalfEdgeNodeForward(he);
				int nodeA2;
				if (halfEdgeNodeForward2 == nIdx)
				{
					nodeA2 = 0;
				}
				else if (i > 0 && halfEdgeNodeForward2 == nodeMap[1])
				{
					nodeA2 = 1;
				}
				else
				{
					nodeA2 = nodeMap.Count;
					nodeMap.Add(halfEdgeNodeForward2);
					slidingMesh.AddNode(NodePoints[halfEdgeNodeForward2]);
				}
				if (nodeA2 != 0 || i + 1 < nodeToSortedHalfEdges.Count)
				{
					unity = faceMap.Count;
					faceMap.Add(he.fIdx);
					slidingMesh.AddFace(nodeA, nodeA2);
				}
				else
				{
					unity = 0;
				}
				if (nodeA2 != nIdx)
				{
					slidingMesh.FaceConstraints[unity] = ConstraintState.FIXED;
				}
				slidingMesh.CellAddFace(count, unity, 0);
				nodeA = nodeA2;
				if (halfEdgeNodeForward2 == nIdx)
				{
					break;
				}
			}
		}
		slidingMesh.constraints = constraints;
		for (int j = 0; j < slidingMesh.FaceCount; j++)
		{
			int index = faceMap[j];
			if (slidingMesh.FaceConstraints[j] != ConstraintState.FIXED)
			{
				slidingMesh.FaceConstraints[j] = FaceConstraints[index];
			}
			slidingMesh.FaceTargetLength[j] = FaceTargetLength[index];
			slidingMesh.FaceSmoothLine[j] = FaceSmoothLine[index];
		}
		for (int k = 0; k < slidingMesh.NodeCount; k++)
		{
			int index2 = nodeMap[k];
			if (k != 0)
			{
				slidingMesh.NodeConstraints[k] = ConstraintState.FIXED;
			}
			slidingMesh.NodePointOnConstraint[k] = NodePointOnConstraint[index2];
		}
		return slidingMesh;
	}

	public static OperationResult Merge(IList<SlidingMesh> inputs, out SlidingMesh m, double nodeTolerance = 0.0001, ConceptualMesh cmesh = null)
	{
		HashSet<int> intSet = null;
		OperationResult operationResult = new OperationResult();
		m = new SlidingMesh();
		Dictionary<int, int> arcToConstraint = null;
		for (int i = 0; i < inputs.Count; i++)
		{
			if ((intSet != null && intSet.Contains(i)) || inputs[i] == null)
			{
				continue;
			}
			try
			{
				m.MergeMesh(inputs[i], ref arcToConstraint, nodeTolerance);
			}
			catch (Exception)
			{
				if (intSet == null)
				{
					intSet = new HashSet<int>();
				}
				intSet.Add(i);
				int ctrlPtIdx = cmesh?.CellRegionMetadata[i].ControlPointIndex ?? (-1);
				operationResult.AddFailure(MeshGenerationMessageCodes.GetErrorMergingRegionMeshes(ctrlPtIdx, i));
				m = new SlidingMesh();
				arcToConstraint.Clear();
				i = 0;
			}
		}
		return operationResult;
	}

	public override Dictionary<string, object> SimpleRepr()
	{
		Dictionary<string, object> simpleRepr = base.SimpleRepr();
		simpleRepr["constraints"] = constraints.SimpleRepr();
		return simpleRepr;
	}

	public override void UpdateFromJson(Dictionary<string, JsonElement> simpleRepr)
	{
		constraints.UpdateFromJson(simpleRepr["constraints"]);
		base.UpdateFromJson(simpleRepr);
	}

	public override void UpdateFaceFromJson(int fIdx, JsonElement rec)
	{
		base.UpdateFaceFromJson(fIdx, rec);
		if (rec.TryGetProperty("constraint", out var localValue))
		{
			FaceConstraints[fIdx] = (ConstraintState)localValue.GetInt32();
		}
		if (rec.TryGetProperty("targetLength", out localValue))
		{
			FaceTargetLength[fIdx] = localValue.GetSingle();
		}
	}

	public override Dictionary<string, object> FaceSimpleRepr(int fIdx)
	{
		Dictionary<string, object> faceSimpleRepr = base.FaceSimpleRepr(fIdx);
		faceSimpleRepr["constraint"] = FaceConstraints[fIdx];
		faceSimpleRepr["targetLength"] = FaceTargetLength[fIdx];
		Polyline faceSmoothGeometry = GetFaceSmoothGeometry(fIdx);
		faceSimpleRepr["smoothStringXY"] = DynamicMesh.SerializePolyline(faceSmoothGeometry);
		return faceSimpleRepr;
	}

	public override void UpdateNodeFromJson(int nIdx, JsonElement rec)
	{
		base.UpdateNodeFromJson(nIdx, rec);
		NodeConstraints[nIdx] = (ConstraintState)rec.GetProperty("constraint").GetInt32();
		NodePointOnConstraint[nIdx] = constraints[(int)NodeConstraints[nIdx]].Polyline.NearestPointOnPolyline(NodePoints[nIdx]);
	}

	public override Dictionary<string, object> NodeSimpleRepr(int nIdx)
	{
		Dictionary<string, object> nodeSimpleRepr = base.NodeSimpleRepr(nIdx);
		nodeSimpleRepr["constraint"] = NodeConstraints[nIdx];
		return nodeSimpleRepr;
	}

	public override Dictionary<string, object> CellSimpleRepr(int cIdx)
	{
		Dictionary<string, object> cellSimpleRepr = base.CellSimpleRepr(cIdx);
		cellSimpleRepr["cellType"] = CellTypes[cIdx];
		return cellSimpleRepr;
	}

	public override void UpdateCellFromJson(int cIdx, JsonElement rec)
	{
		base.UpdateCellFromJson(cIdx, rec);
		if (rec.TryGetProperty("cellType", out var localValue))
		{
			CellTypes[cIdx] = (CellType)localValue.GetInt32();
		}
	}

	public new bool CheckMesh(out List<DynamicMeshError> errors)
	{
		base.CheckMesh(out errors);
		for (int i = 0; i < NodeCount; i++)
		{
			if (!NodeDeleted[i])
			{
				ConstraintState constraintState = NodeConstraints[i];
				if (constraintState < ConstraintState.FIXED || (int)constraintState >= constraints.Count)
				{
					errors.Add(new DynamicMeshError
					{
						errorType = DynamicMeshErrorType.NODE_CONSTRAINT,
						element = i
					});
				}
			}
		}
		HashSet<(int, int)> valueTuples = new HashSet<(int, int)>();
		for (int j = 0; j < FaceCount; j++)
		{
			if (FaceDeleted[j])
			{
				continue;
			}
			ConstraintState constraintState2 = FaceConstraints[j];
			if (constraintState2 < ConstraintState.FREE || (int)constraintState2 >= constraints.Count)
			{
				errors.Add(new DynamicMeshError
				{
					errorType = DynamicMeshErrorType.FACE_CONSTRAINT,
					element = j
				});
			}
			if (constraintState2 >= (ConstraintState)0)
			{
				foreach (int faceNode in FaceNodes[j])
				{
					if (NodeConstraints[faceNode] != ConstraintState.FIXED && constraintState2 != NodeConstraints[faceNode])
					{
						errors.Add(new DynamicMeshError
						{
							errorType = DynamicMeshErrorType.FACE_CONSTRAINT,
							element = j
						});
					}
				}
			}
			(int, int) faceNodesTerm = (FaceNodes[j].A, FaceNodes[j].B);
			if (faceNodesTerm.Item1 > faceNodesTerm.Item2)
			{
				int ordinal = faceNodesTerm.Item1;
				faceNodesTerm.Item1 = faceNodesTerm.Item2;
				faceNodesTerm.Item2 = ordinal;
			}
			if (valueTuples.Contains(faceNodesTerm))
			{
				errors.Add(new DynamicMeshError
				{
					errorType = DynamicMeshErrorType.DUPLICATE_FACE
				});
			}
			else
			{
				valueTuples.Add(faceNodesTerm);
			}
		}
		CheckMesh1DCells(errors);
		return errors.Count == 0;
	}

	protected void CheckMesh1DCells(List<DynamicMeshError> errors)
	{
		foreach (int cellIterator in CellIterator())
		{
			if (CellTypes[cellIterator] == CellType.CELL_2D)
			{
				if (CellSides[cellIterator] != null)
				{
					errors.Add(new DynamicMeshError
					{
						errorType = DynamicMeshErrorType.BAD_SIDES,
						element = cellIterator
					});
				}
			}
			else if (CellTypes[cellIterator] == CellType.CELL_1D_REACH)
			{
				if (CellSides[cellIterator] == null)
				{
					errors.Add(new DynamicMeshError
					{
						errorType = DynamicMeshErrorType.BAD_SIDES,
						element = cellIterator
					});
					continue;
				}
				Cell1DSide cell1DSide = CellSides[cellIterator];
				if (cell1DSide.SideUpstream == null || cell1DSide.SideUpstream.Count == 0 || cell1DSide.SideDownstream == null || cell1DSide.SideDownstream.Count == 0 || cell1DSide.SideLeft == null || cell1DSide.SideLeft.Count == 0 || cell1DSide.SideRight == null || cell1DSide.SideRight.Count == 0)
				{
					errors.Add(new DynamicMeshError
					{
						errorType = DynamicMeshErrorType.BAD_SIDES,
						element = cellIterator
					});
				}
			}
			else if (CellTypes[cellIterator] != CellType.CELL_1D_JUNCTION)
			{
				errors.Add(new DynamicMeshError
				{
					errorType = DynamicMeshErrorType.BAD_CELL_TYPE,
					element = cellIterator
				});
			}
		}
	}

	public void SnapToConceptualMesh(ConceptualMesh cmesh, double tolerance = 1E-07)
	{
		for (int i = 0; i < cmesh.FaceCount; i++)
		{
			if (!cmesh.FaceDeleted[i])
			{
				constraints.Add(cmesh.FaceSmoothLine[i], i);
			}
		}
		SnapToConstraints(tolerance);
	}

	public void SnapToConstraints(double tolerance = 1E-07)
	{
		for (int i = 0; i < NodeCount; i++)
		{
			if (!NodeDeleted[i] && NodeConstraints[i] >= (ConstraintState)0)
			{
				NodeConstraints[i] = ConstraintState.FREE;
			}
		}
		for (int j = 0; j < FaceCount; j++)
		{
			if (!FaceDeleted[j] && FaceConstraints[j] >= (ConstraintState)0)
			{
				FaceConstraints[j] = ConstraintState.FREE;
			}
		}
		HashSet<int> intSet = new HashSet<int>();
		for (int k = 0; k < constraints.Count; k++)
		{
			Polyline polyline = constraints[k].Polyline;
			double toleranceTimesLength = tolerance * polyline.Length;
			Extent extent = polyline.Extent;
			Extent ext = new Extent(extent.MinX - toleranceTimesLength, extent.MaxX + toleranceTimesLength, extent.MinY - toleranceTimesLength, extent.MaxY + toleranceTimesLength);
			foreach (int selectNodesByExtent in SelectNodesByExtent(ext))
			{
				PointOnPolyline nodePointOnConstraint = polyline.NearestPointOnPolyline(NodePoints[selectNodesByExtent], out var actualDistance);
				if (!(actualDistance < toleranceTimesLength))
				{
					continue;
				}
				if (polyline.First() == NodePoints[selectNodesByExtent] || polyline.Last() == NodePoints[selectNodesByExtent])
				{
					NodeConstraints[selectNodesByExtent] = ConstraintState.FIXED;
				}
				else
				{
					NodeConstraints[selectNodesByExtent] = (ConstraintState)k;
					NodePointOnConstraint[selectNodesByExtent] = nodePointOnConstraint;
				}
				foreach (int nodeToEdge in NodeToEdges(selectNodesByExtent))
				{
					intSet.Add(nodeToEdge);
				}
			}
			foreach (int ordinal in intSet)
			{
				int a = FaceNodes[ordinal].A;
				int b = FaceNodes[ordinal].B;
				ConstraintState constraintState = NodeConstraints[a];
				ConstraintState constraintState2 = NodeConstraints[b];
				if (constraintState >= (ConstraintState)0 && (constraintState2 == ConstraintState.FIXED || constraintState2 == constraintState))
				{
					k = (int)constraintState;
				}
				else
				{
					if (constraintState2 < (ConstraintState)0 || constraintState != ConstraintState.FIXED)
					{
						continue;
					}
					k = (int)constraintState2;
				}
				_ = constraints[k].Polyline;
				double faceLength = FaceLength(ordinal);
				GetPairNodePointOnConstraint(FaceNodes[ordinal], k, out var pops);
				if (Math.Abs(constraints.DistanceAlong(k, pops.A) - constraints.DistanceAlong(k, pops.B)) < 1.2 * faceLength)
				{
					FaceConstraints[ordinal] = (ConstraintState)k;
				}
			}
		}
	}

	public void UpdatePointOnPolylineMANY(List<int> nodes = null)
	{
		if (nodes == null)
		{
			nodes = ValidNodes();
		}
		foreach (int node in nodes)
		{
			UpdatePointOnPolyline(node);
		}
	}

	public void UpdatePointOnPolyline(int nIdx)
	{
		if (NodeDeleted[nIdx] || NodeConstraints[nIdx] < (ConstraintState)0)
		{
			NodePointOnConstraint[nIdx] = PointOnPolyline.NullPointOnPolyline;
		}
		else
		{
			NodePointOnConstraint[nIdx] = constraints[(int)NodeConstraints[nIdx]].Polyline.NearestPointOnPolyline(NodePoints[nIdx]);
		}
	}

	public PointOnPolyline GetNodePointOnConstraint(int nIdx, int constraint, double tolerance = 0.001)
	{
		if (NodeConstraints[nIdx] == (ConstraintState)constraint)
		{
			return NodePointOnConstraint[nIdx];
		}
		if (NodeConstraints[nIdx] == ConstraintState.FIXED)
		{
			PointOnPolyline result = constraints[constraint].Polyline.NearestPointOnPolyline(NodePoints[nIdx], out var actualDistance);
			if (actualDistance <= tolerance)
			{
				return result;
			}
		}
		return PointOnPolyline.NullPointOnPolyline;
	}

	public void GetPairNodePointOnConstraint(Duple<int> nodes, int constraint, out Duple<PointOnPolyline> pops)
	{
		pops = default;
		for (int i = 0; i < 2; i++)
		{
			PointOnPolyline pointOnPolyline = default;
			if (NodeConstraints[nodes[i]] == (ConstraintState)constraint)
			{
				pointOnPolyline = NodePointOnConstraint[nodes[i]];
			}
			else
			{
				VirtualConstraints.Constraint constraint2 = constraints[constraint];
				if (constraint2.Polyline.First() == constraint2.Polyline.Last() && constraint2.Polyline.First().DistanceSquaredTo(NodePoints[nodes[i]]) < 1E-10)
				{
					PointOnPolyline pop = constraint2.Polyline.NearestPointOnPolyline(NodePoints[nodes[1 - i]]);
					pointOnPolyline = ((!(constraints.DistanceAlong(constraint, pop) / constraint2.Polyline.Length < 0.5)) ? constraint2.Polyline.LastPointOnPolyline() : constraint2.Polyline.FirstPointOnPolyline());
				}
				else
				{
					pointOnPolyline = constraint2.Polyline.NearestPointOnPolyline(NodePoints[nodes[i]]);
				}
			}
			if (DynamicMesh.EnhancedMeshConsistencyChecks && NodePoints[nodes[i]].DistanceSquaredTo(pointOnPolyline.Point) >= 1E-16)
			{
				Point point = NodePoints[nodes[i]];
				throw new StructuredException(Message.GenericError("Could not resolve Node points for constraint near ({Coordinate})", point));
			}
			pointOnPolyline.Point = NodePoints[nodes[i]];
			pops[i] = pointOnPolyline;
		}
	}

	public int FaceConstraintOrientation(int fIdx)
	{
		GetPairNodePointOnConstraint(FaceNodes[fIdx], (int)FaceConstraints[fIdx], out var pops);
		if (pops.B > pops.A)
		{
			return 0;
		}
		return 1;
	}

	private IEnumerable<int> SelectNodesByExtent(Extent ext)
	{
		for (int nIdx = 0; nIdx < NodeCount; nIdx++)
		{
			if (ext.Contains(NodePoints[nIdx]))
			{
				yield return nIdx;
			}
		}
	}

	public List<int> SlideNodes()
	{
		List<int> intList = new List<int>();
		foreach (int validNode in ValidNodes())
		{
			if (NodeConstraints[validNode] >= (ConstraintState)0)
			{
				intList.Add(validNode);
			}
		}
		return intList;
	}

	public List<int> MovableNodes()
	{
		List<int> intList = new List<int>();
		foreach (int validNode in ValidNodes())
		{
			if (!NodeIsFixed(validNode))
			{
				intList.Add(validNode);
			}
		}
		return intList;
	}

	public bool NodeIsFixed(int nIdx)
	{
		ConstraintState constraintState = NodeConstraints[nIdx];
		if (constraintState != ConstraintState.FIXED)
		{
			if (constraintState >= (ConstraintState)0)
			{
				return constraints[(int)constraintState].mode == VirtualConstraints.ConstraintMode.FIXED;
			}
			return false;
		}
		return true;
	}

	public List<Polyline> ContiguousPolylines(ConceptualMesh cmesh)
	{
		List<Polyline> polylines = new List<Polyline>();
		HashSet<int> intSet = new HashSet<int>();
		for (int i = 0; i < cmesh.FaceCount; i++)
		{
			if (cmesh.FaceDeleted[i] || intSet.Contains(i))
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
						Polyline polyline = cmesh.FaceSmoothLine[he.fIdx];
						if (he.orient != b)
						{
							polyline = polyline.Clone();
							polyline.Reverse();
						}
						polylinesList.Add(polyline);
						intSet.Add(he.fIdx);
					}
					if (cmesh.NodeDegree(HalfEdgeNodeForward(he)) != 2)
					{
						break;
					}
					he = cmesh.HalfEdgeNext(he);
				}
				while (!intSet.Contains(he.fIdx));
				polylinesList.Reverse();
			}
			polylines.Add(Polyline.Concatenate(polylinesList));
		}
		return polylines;
	}

	public bool MoveOrSlideNode(int nIdx, Point p, bool check = true)
	{
		ConstraintState constraintState = NodeConstraints[nIdx];
		if (constraintState >= (ConstraintState)0)
		{
			return SlideNode(nIdx, p, check);
		}
		if (constraintState == ConstraintState.FREE)
		{
			return MoveNode(nIdx, p, check);
		}
		return false;
	}

	public bool SlideNode(int nIdx, Point p, bool check = true)
	{
		if (p == NodePoints[nIdx])
		{
			return false;
		}
		PointOnPolyline pop = constraints[(int)NodeConstraints[nIdx]].Polyline.NearestPointOnPolyline(p);
		return SlideNode(nIdx, pop, check);
	}

	public bool SlideNode(int nIdx, PointOnPolyline pop, bool check = true)
	{
		double bufferRelative = 0.1;
		if (pop.Point == NodePoints[nIdx])
		{
			return false;
		}
		if (check)
		{
			if (!CheckValidMove(nIdx, pop.Point))
			{
				return false;
			}
			SlideNodeBounds(nIdx, out var lowerDist, out var upperDist, bufferRelative);
			if (lowerDist == upperDist)
			{
				return false;
			}
			int i = (int)NodeConstraints[nIdx];
			double distanceAlong = constraints.DistanceAlong(i, pop);
			if (distanceAlong < lowerDist || distanceAlong > upperDist)
			{
				return false;
			}
		}
		NodePointOnConstraint[nIdx] = pop;
		MoveNode(nIdx, pop.Point, check: false);
		return true;
	}

	public void SlideNodeBounds(int nIdx, out double lowerDist, out double upperDist, double bufferRelative = 0.1, double bufferAbsolute = -1.0)
	{
		int nodeConstraint = (int)NodeConstraints[nIdx];
		Polyline polyline = constraints[nodeConstraint].Polyline;
		PointOnPolyline pop = NodePointOnConstraint[nIdx];
		double distanceAlong = constraints.DistanceAlong(nodeConstraint, pop);
		lowerDist = -1.0;
		upperDist = -1.0;
		double faceTargetLengthSum = 0.0;
		foreach (int nodeToEdge in NodeToEdges(nIdx))
		{
			if (FaceConstraints[nodeToEdge] != NodeConstraints[nIdx])
			{
				continue;
			}
			faceTargetLengthSum += 0.5 * (double)FaceTargetLength[nodeToEdge];
			int index = ((FaceNodes[nodeToEdge].A == nIdx) ? FaceNodes[nodeToEdge].B : FaceNodes[nodeToEdge].A);
			PointOnPolyline pop2 = ((NodeConstraints[index] == NodeConstraints[nIdx]) ? NodePointOnConstraint[index] : polyline.NearestPointOnPolyline(NodePoints[index]));
			double distanceAlong2 = constraints.DistanceAlong(nodeConstraint, pop2);
			if (distanceAlong2 < distanceAlong)
			{
				lowerDist = distanceAlong2;
				continue;
			}
			if (distanceAlong2 > distanceAlong)
			{
				upperDist = distanceAlong2;
				continue;
			}
			throw new Exception("Point along arc have slid on top of each other");
		}
		if (bufferAbsolute < 0.0)
		{
			bufferAbsolute = 0.0;
		}
		bool condition = true;
		bool condition2 = true;
		if (lowerDist < 0.0)
		{
			condition = false;
			lowerDist = 0.0;
		}
		if (upperDist < 0.0)
		{
			condition2 = false;
			upperDist = polyline.Length;
		}
		if (condition)
		{
			double bufferRelativeLowerDistTerm = (1.0 - bufferRelative) * lowerDist + bufferRelative * Math.Min(upperDist, distanceAlong);
			double lowerDistPlusBufferAbsolute = lowerDist + bufferAbsolute;
			lowerDist = Math.Min(distanceAlong, Math.Max(bufferRelativeLowerDistTerm, lowerDistPlusBufferAbsolute));
		}
		if (condition2)
		{
			double bufferRelativeUpperDistTerm = (1.0 - bufferRelative) * upperDist + bufferRelative * Math.Max(lowerDist, distanceAlong);
			double upperDistAboveBufferAbsolute = upperDist - bufferAbsolute;
			upperDist = Math.Max(distanceAlong, Math.Min(bufferRelativeUpperDistTerm, upperDistAboveBufferAbsolute));
		}
	}

	public override bool MoveNode(int nIdx, Point p, bool check = true)
	{
		if (!base.MoveNode(nIdx, p, check))
		{
			return false;
		}
		foreach (HalfEdge nodeToHalfEdge in NodeToHalfEdges(nIdx))
		{
			if (FaceConstraints[nodeToHalfEdge.fIdx] >= (ConstraintState)0)
			{
				FaceSmoothLine[nodeToHalfEdge.fIdx] = ComputeSubFaceGeometry(nodeToHalfEdge.fIdx);
				continue;
			}
			Polyline polyline = FaceSmoothLine[nodeToHalfEdge.fIdx];
			if (polyline != null)
			{
				if (nodeToHalfEdge.orient == 0)
				{
					polyline.Warp(p, Point.NullPoint);
				}
				else
				{
					polyline.Warp(Point.NullPoint, p);
				}
			}
		}
		return true;
	}

	public override bool CheckValidMove(int nIdx, Point pnt)
	{
		List<HalfEdge> nodeToSortedHalfEdges = NodeToSortedHalfEdges(nIdx);
		for (int i = 0; i < nodeToSortedHalfEdges.Count; i++)
		{
			int halfEdgeToCell = HalfEdgeToCell(nodeToSortedHalfEdges[i]);
			if (halfEdgeToCell < 0)
			{
				continue;
			}
			int index = (i + 1) % nodeToSortedHalfEdges.Count;
			int n = HalfEdgeNodeForward(nodeToSortedHalfEdges[i]);
			int n2 = HalfEdgeNodeForward(nodeToSortedHalfEdges[index]);
			if (!CheckLeftOfLine(n, n2))
			{
				return false;
			}
			if (CellNumSides(halfEdgeToCell) > 3)
			{
				int n3 = HalfEdgeNodeForward(HalfEdgeNext(nodeToSortedHalfEdges[i]));
				if (!CheckLeftOfLine(n, n3))
				{
					return false;
				}
				int n4 = HalfEdgeNodeBack(HalfEdgePrev(HalfEdgeFlip(nodeToSortedHalfEdges[index])));
				if (!CheckLeftOfLine(n4, n2))
				{
					return false;
				}
			}
		}
		return true;
		bool CheckLeftOfLine(int index2, int index3)
		{
			Point b = NodePoints[index2];
			Point c = NodePoints[index3];
			return NativePredicates.CounterClockwise(pnt, b, c) > 0.0;
		}
	}

	public override int SplitFace(int fIdx, int nIdx, out int newFace)
	{
		base.SplitFace(fIdx, nIdx, out newFace);
		Polyline polyline = FaceSmoothLine[fIdx];
		if (polyline != null)
		{
			PointOnPolyline point = polyline.NearestPointOnPolyline(NodePoints[nIdx]);
			FaceSmoothLine[fIdx] = polyline.SubsectionBefore(point);
			FaceSmoothLine[newFace] = polyline.SubsectionAfter(point);
			FaceSmoothLine[fIdx].Points[FaceSmoothLine[fIdx].PointCount - 1] = NodePoints[nIdx];
			FaceSmoothLine[newFace].Points[0] = NodePoints[nIdx];
			FaceConstraints[newFace] = FaceConstraints[fIdx];
		}
		else if (FaceConstraints[fIdx] >= (ConstraintState)0)
		{
			throw new Exception("Apparently need to allow for constrained face that doesn't have smooth line set");
		}
		return nIdx;
	}

	public void ExpandTriangleVector(int nIdx, int cIdx, out Vector vec, out double rate, out double area)
	{
		List<int> cellToNodes = CellToNodes(cIdx);
		if (cellToNodes.Count != 3)
		{
			throw new ArgumentException("Only triangles supported");
		}
		Point a = NodePoints[nIdx];
		Point point;
		Point point2;
		if (cellToNodes[0] == nIdx)
		{
			point = NodePoints[cellToNodes[1]];
			point2 = NodePoints[cellToNodes[2]];
		}
		else if (cellToNodes[1] == nIdx)
		{
			point = NodePoints[cellToNodes[2]];
			point2 = NodePoints[cellToNodes[0]];
		}
		else
		{
			point = NodePoints[cellToNodes[0]];
			point2 = NodePoints[cellToNodes[1]];
		}
		vec = (point2 - point).Perpendicular(clockwise: false);
		rate = 0.5 * point2.DistanceTo(point);
		area = Triangle.ComputeSignedArea(a, point, point2);
	}

	public bool CollapseFace(HalfEdge he)
	{
		return CollapseFace(in he, HalfEdge.NullHalfEdge);
	}

	public bool CollapseFace(in HalfEdge he, HalfEdge addTo)
	{
		CollapseFacePreflightInfo preflight = null;
		if (!CollapseFacePreflight(in he, addTo, ref preflight))
		{
			return false;
		}
		DoCollapseFace(preflight);
		return true;
	}

	public void DoCollapseFace(CollapseFacePreflightInfo preflight)
	{
		List<HalfEdge> spokes = preflight.spokes;
		int nIdx = HalfEdgeNodeBack(spokes[0]);
		int halfEdgeNodeForward = HalfEdgeNodeForward(spokes[0]);
		HalfEdge halfEdge = spokes[0];
		HalfEdge addTo = preflight.addTo;
		Duple<int> duple = FaceCells[halfEdge.fIdx];
		for (int i = 0; i < spokes.Count; i++)
		{
			HalfEdge halfEdge2 = spokes[i];
			int halfEdgeToCell = HalfEdgeToCell(halfEdge2);
			int halfEdgeToOppositeCell = HalfEdgeToOppositeCell(halfEdge2);
			if (HalfEdgeNodeForward(halfEdge2) == halfEdgeNodeForward)
			{
				if (halfEdgeToCell >= 0)
				{
					CellRemoveFace(halfEdgeToCell, halfEdge2.fIdx);
				}
				if (halfEdgeToOppositeCell >= 0)
				{
					CellRemoveFace(halfEdgeToOppositeCell, halfEdge2.fIdx);
				}
				continue;
			}
			SetFaceNode(halfEdge2.fIdx, halfEdge2.orient, halfEdgeNodeForward);
			if (halfEdge2 == addTo)
			{
				Polyline plineAE = preflight.plineAE;
				if (halfEdge2.orient == 0)
				{
					plineAE.Reverse();
				}
				FaceSmoothLine[halfEdge2.fIdx] = plineAE;
				preflight.plineAE = null;
			}
			else
			{
				FaceSmoothLine[halfEdge2.fIdx] = FaceSimpleGeometry(halfEdge2.fIdx);
			}
		}
		for (int j = 0; j < 2; j++)
		{
			int duple2 = duple[(halfEdge.orient + j) % 2];
			if (duple2 < 0 || CellNumSides(duple2) > 2)
			{
				continue;
			}
			foreach (HalfEdge cellFace in CellFaces[duple2])
			{
				if (FaceConstraints[cellFace.fIdx] == ConstraintState.FREE && HalfEdgeToOppositeCell(cellFace) >= 0)
				{
					MergeCells(cellFace.fIdx);
					break;
				}
			}
		}
		DeleteFace(halfEdge.fIdx);
		DeleteNode(nIdx);
	}

	public bool CollapseFacePreflight(in HalfEdge he, HalfEdge addTo, ref CollapseFacePreflightInfo preflight)
	{
		preflight = null;
		int halfEdgeNodeBack = HalfEdgeNodeBack(he);
		int halfEdgeNodeForward = HalfEdgeNodeForward(he);
		if (NodeIsFixed(halfEdgeNodeBack))
		{
			return false;
		}
		if (NodeConstraints[halfEdgeNodeBack] >= (ConstraintState)0 && FaceConstraints[he.fIdx] != NodeConstraints[halfEdgeNodeBack])
		{
			return false;
		}
		List<HalfEdge> nodeToSortedHalfEdges = NodeToSortedHalfEdges(halfEdgeNodeBack, he);
		if (!addTo.IsNull() && HalfEdgeNodeBack(addTo) != halfEdgeNodeBack)
		{
			addTo = HalfEdgeFlip(addTo);
		}
		preflight = new CollapseFacePreflightInfo(nodeToSortedHalfEdges, addTo)
		{
			plineAE = null
		};
		if (!addTo.IsNull())
		{
			preflight.plineAE = GetFaceSmoothGeometry(HalfEdgeFlip(addTo), copy: true);
			preflight.plineAE.RemoveAt(preflight.plineAE.Count - 1);
			preflight.plineAE.AddRange(GetFaceSmoothGeometry(nodeToSortedHalfEdges[0], copy: false));
		}
		Polygon polygon = new Polygon(4);
		Polygon polygon2 = new Polygon(4);
		int ordinal = 0;
		for (int i = 0; i < nodeToSortedHalfEdges.Count; i++)
		{
			HalfEdge he2 = nodeToSortedHalfEdges[i];
			int halfEdgeToCell = HalfEdgeToCell(he2);
			if (halfEdgeToCell < 0)
			{
				continue;
			}
			polygon.Clear();
			polygon2.Clear();
			int ordinal2 = 0;
			int ordinal3 = 0;
			foreach (HalfEdge cellFace in CellFaces[halfEdgeToCell])
			{
				int halfEdgeNodeBack2 = HalfEdgeNodeBack(cellFace);
				int halfEdgeNodeForward2 = HalfEdgeNodeForward(cellFace);
				if (cellFace.fIdx == he.fIdx)
				{
					continue;
				}
				if (cellFace.fIdx == addTo.fIdx)
				{
					if (cellFace.orient == addTo.orient)
					{
						polygon.Add(NodePoints[halfEdgeNodeForward]);
						polygon2.AddRangeReverse(preflight.plineAE);
						polygon2.RemoveAt(polygon2.Count - 1);
					}
					else
					{
						polygon.Add(NodePoints[halfEdgeNodeBack2]);
						polygon2.AddRange(preflight.plineAE);
						polygon2.RemoveAt(polygon2.Count - 1);
					}
				}
				else if (halfEdgeNodeBack2 == halfEdgeNodeBack)
				{
					polygon.Add(NodePoints[halfEdgeNodeForward]);
					HalfEdge he3 = NodesToHalfEdge(halfEdgeNodeForward, halfEdgeNodeForward2);
					if (!he3.IsNull())
					{
						polygon2.AddRange(GetFaceSmoothGeometry(he3, copy: false));
						polygon2.RemoveAt(polygon2.Count - 1);
					}
					else
					{
						polygon2.Add(NodePoints[halfEdgeNodeForward]);
					}
				}
				else if (halfEdgeNodeForward2 == halfEdgeNodeBack)
				{
					polygon.Add(NodePoints[halfEdgeNodeBack2]);
					HalfEdge he4 = NodesToHalfEdge(halfEdgeNodeBack2, halfEdgeNodeForward);
					if (!he4.IsNull())
					{
						polygon2.AddRange(GetFaceSmoothGeometry(he4, copy: false));
						polygon2.RemoveAt(polygon2.Count - 1);
					}
					else
					{
						polygon2.Add(NodePoints[halfEdgeNodeBack2]);
					}
				}
				else
				{
					polygon.Add(NodePoints[halfEdgeNodeBack2]);
					polygon2.AddRange(GetFaceSmoothGeometry(cellFace, copy: false));
					polygon2.RemoveAt(polygon2.Count - 1);
				}
				if (FaceConstraints[cellFace.fIdx] == ConstraintState.FREE)
				{
					ordinal3++;
				}
				ordinal2++;
			}
			if (ordinal2 < 3)
			{
				ordinal++;
				if (ordinal3 == 0)
				{
					return false;
				}
				continue;
			}
			if (polygon.SelfIntersects() || polygon2.SelfIntersects())
			{
				return false;
			}
			double lengthSquared = Math.Pow(polygon.Length, 2.0);
			if ((0.0 - polygon.Area) / lengthSquared <= 1E-06)
			{
				return false;
			}
			if (0.0 - polygon2.Area <= 0.0)
			{
				return false;
			}
			preflight.newSimpleCells.Add(polygon.Clone());
			preflight.newSmoothCells.Add(polygon2.Clone());
		}
		if (ordinal > preflight.newSimpleCells.Count)
		{
			return false;
		}
		return true;
	}

	public static void DumpToJson(Mesh mesh, ConceptualMesh cmesh = null, List<object> commands = null)
	{
		new SlidingMesh(mesh).DumpToJson(cmesh, "C:\\Temp\\cmd.json", commands);
	}

	public override void DumpToJson(string filename = "C:\\Temp\\cmd.json")
	{
		DumpToJson(filename, null);
	}

	public void DumpToJson(ConceptualMesh cmesh, string filename = "C:\\Temp\\cmd.json", List<object> commands = null)
	{
		DumpToJson(filename, commands, cmesh);
	}

	public void DumpToJson(string cmdFile, List<object> commands, ConceptualMesh cmesh = null)
	{
		using FileStream utf8Json = File.Create(cmdFile);
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		if (commands == null)
		{
			commands = new List<object>();
		}
		commands.Add(new List<object> { "SET", "g_raw", this });
		commands.Add(new List<object> { "PYTHON", "\r\ng=json_to_mesh(g_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\n#g.plot_nodes(ax = ax)\r\ng.plot_edges(ax = ax, color = 'm', subedges='smoothStringXY')\r\ng.plot_cells(ax = ax, color = 'orange', subedges='smoothStringXY', alpha = 0.4, zorder = -1)\r\n" });
		if (cmesh != null)
		{
			commands.Add(new List<object> { "SET", "cg_raw", cmesh });
			commands.Add(new List<object> { "PYTHON", "\r\ncg=json_to_mesh(cg_raw)\r\ncg.plot_edges(lw=0.7,color='k',ax=ax,subedges='rawStringXY')" });
		}
		JsonSerializer.Serialize(utf8Json, commands, options);
	}

	public void EnforcePerimeterFaceInvariant()
	{
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator].A < 0)
			{
				ReverseFace(faceIterator);
			}
		}
	}

	public void ReverseFace(int fIdx)
	{
		FaceNodes[fIdx] = FaceNodes[fIdx].Reverse();
		FaceCells[fIdx] = FaceCells[fIdx].Reverse();
		if (FaceSmoothLine[fIdx] != null)
		{
			FaceSmoothLine[fIdx].Reverse();
		}
		for (int i = 0; i < 2; i++)
		{
			int faceCell = FaceCells[fIdx][i];
			if (faceCell < 0)
			{
				continue;
			}
			for (int j = 0; j < CellFaces[faceCell].Count; j++)
			{
				if (CellFaces[faceCell][j].fIdx == fIdx)
				{
					CellFaces[faceCell][j] = CellFaces[faceCell][j].Flipped();
					break;
				}
			}
		}
	}

	public OperationResult ToMesh(out Mesh mesh, ConceptualMesh cmesh)
	{
		OperationResult operationResult = new OperationResult();
		int cellCount = CellCount;
		int faceCount = FaceCount;
		int nodeCount = NodeCount;
		CellIndex();
		mesh = new Mesh();
		mesh.Cells = new Cell[cellCount];
		mesh.CellCenters = new Point[cellCount];
		mesh.Faces = new Face[faceCount];
		mesh.Faces.Fill(Face.NullFace);
		mesh.FacePoints = new FacePoint[nodeCount];
		mesh.FaceInternalPoints = new Memory<Point>[faceCount];
		int count = 0;
		for (int i = 0; i < faceCount; i++)
		{
			Polyline polyline = FaceSmoothLine[i];
			if (polyline != null && polyline.Count > 2)
			{
				count += polyline.Count - 2;
			}
		}
		Memory<Point> points = Memory<Point>.Empty;
		if (count > 0)
		{
			points = new Point[count];
		}
		for (int j = 0; j < cellCount; j++)
		{
			List<HalfEdge> cellFace = CellFaces[j];
			Cell cell = new Cell();
			int[] faces = new int[cellFace.Count];
			cell.Faces = faces;
			for (int k = 0; k < cellFace.Count; k++)
			{
				faces[k] = cellFace[k].fIdx;
			}
			mesh.Cells[j] = cell;
		}
		for (int l = 0; l < faceCount; l++)
		{
			Face face = new Face();
			Duple<int> duple = FaceCells[l];
			Duple<int> duple2 = FaceNodes[l];
			Polyline polyline2 = FaceSmoothLine[l];
			if (polyline2 != null && polyline2.Count > 2)
			{
				int countOffset = polyline2.Count - 2;
				Memory<Point> faceInternalPoints = points.Slice(0, countOffset);
				points = points.Slice(countOffset);
				polyline2.GetPointBufferUnsafe().Slice(1, countOffset).CopyTo(faceInternalPoints);
				mesh.FaceInternalPoints[l] = faceInternalPoints;
			}
			if (duple.A < 0)
			{
				operationResult.AddMessage("Unexpected reversed face while converting to computationl mesh");
				face.cellB = -1;
				face.cellA = duple.B;
				face.fpA = duple2.B;
				face.fpB = duple2.A;
				Memory<Point> faceInternalPoint = mesh.FaceInternalPoints[l];
				if (!faceInternalPoint.IsEmpty)
				{
					faceInternalPoint.Span.Reverse();
				}
			}
			else
			{
				face.cellA = duple.A;
				face.cellB = NoDataConv(duple.B);
				face.fpA = duple2.A;
				face.fpB = duple2.B;
			}
			mesh.Faces[l] = face;
		}
		for (int m = 0; m < nodeCount; m++)
		{
			FacePoint facePoint = new FacePoint(NodePoints[m]);
			facePoint.Faces = new List<int>(NodeToEdges(m));
			mesh.FacePoints[m] = facePoint;
		}
		for (int n = 0; n < nodeCount; n++)
		{
			FacePoint facePoint2 = mesh.FacePoints[n];
			List<HalfEdge> orderBy = NodeToHalfEdges(n).OrderBy(HalfEdgeBearing).ToList();
			int ordinal = 0;
			for (int intervalIndex = 0; intervalIndex < orderBy.Count; intervalIndex++)
			{
				if (HalfEdgeToOppositeCell(orderBy[intervalIndex]) < 0)
				{
					ordinal = intervalIndex;
					break;
				}
			}
			int ordinal2 = 0;
			int ordinal3 = ordinal;
			while (ordinal3 < orderBy.Count)
			{
				facePoint2.Faces[ordinal2] = orderBy[ordinal3].fIdx;
				ordinal3++;
				ordinal2++;
			}
			int ordinal4 = 0;
			while (ordinal4 < ordinal)
			{
				facePoint2.Faces[ordinal2] = orderBy[ordinal4].fIdx;
				ordinal4++;
				ordinal2++;
			}
			CheckFaceNodeConsistency(this, facePoint2, n);
			mesh.FacePoints[n] = facePoint2;
		}
		HashSet<int> intSet = new HashSet<int>();
		for (int intervalIndex2 = 0; intervalIndex2 < cellCount; intervalIndex2++)
		{
			Cell cell2 = mesh.Cells[intervalIndex2];
			if (mesh.CellSimpleArea(intervalIndex2) < 0.0)
			{
				cell2.Faces.Span.Reverse();
				for (int intervalIndex3 = 0; intervalIndex3 < cell2.Faces.Span.Length; intervalIndex3++)
				{
					int faceIndex = cell2.Faces.Span[intervalIndex3];
					intSet.Add(faceIndex);
				}
			}
			mesh.CellCenters[intervalIndex2] = ComputeCellCenter(intervalIndex2);
		}
		foreach (int ordinal5 in intSet)
		{
			Face face2 = mesh.Faces[ordinal5];
			int fpA = face2.fpA;
			face2.fpA = face2.fpB;
			face2.fpB = fpA;
			mesh.Faces[ordinal5] = face2;
		}
		OperationResult operationResult2 = TryExtractBoundaryComplexPolygon(out var result);
		operationResult2.CopyMessagesTo(operationResult);
		Polygon polygon = null;
		polygon = ((!operationResult2) ? Polygon.FromExtent(Extent.Union(NodePoints)) : result.OuterRing.Polygon);
		polygon.EnsureCounterClockwise();
		mesh.Perimeter = polygon;
		mesh.StreamwiseData = ComputeStreamwiseData(cmesh);
		return operationResult;
		static int NoDataConv(int v)
		{
			if (v >= 0)
			{
				return v;
			}
			return -1;
		}
	}

	private StreamwiseData ComputeStreamwiseData(ConceptualMesh cmesh)
	{
		StreamwiseData streamwiseData = new StreamwiseData();
		LabelMesh(cmesh, out var cellRegions, out var _);
		ISpatialTransformer xf = null;
		int cellRegionTerm = -1;
		List<Vector> vectors = new List<Vector>();
		List<Point> points = new List<Point>();
		for (int i = 0; i < CellCount; i++)
		{
			int cellRegion = cellRegions[i];
			if (cellRegion >= 0 && cmesh.CellRegionMetadata[cellRegion].PatchType == PatchType.Quad)
			{
				if (cellRegion != cellRegionTerm)
				{
					xf = cmesh.GetQuadTransform(cellRegion);
					cellRegionTerm = cellRegion;
				}
				List<HalfEdge> cellFace = CellFaces[i];
				vectors.Clear();
				points.Clear();
				for (int j = 0; j < cellFace.Count; j++)
				{
					int index = HalfEdgeNodeBack(cellFace[j]);
					points.Add(xf.ReverseTransform(NodePoints[index]));
				}
				for (int k = 0; k < cellFace.Count; k++)
				{
					int index2 = (k + 1) % cellFace.Count;
					Vector perpendicular = (points[index2] - points[k]).Perpendicular(cellFace[k].orient == 0);
					vectors.Add(perpendicular);
				}
				streamwiseData.AddCellData(i, vectors);
			}
		}
		return streamwiseData;
	}

	private static void CheckFaceNodeConsistency(SlidingMesh sm, FacePoint fp, int fpIdx)
	{
		int ordinal = 0;
		foreach (int face in fp.Faces)
		{
			Duple<int> duple = sm.FaceCells[face];
			if (duple.A < 0)
			{
				if (duple.B < 0)
				{
					throw new StructuredException(Message.GenericError("Node/Face connectivity error for F[{FaceIndex}] -> N[{FacePointIndex}], near ({Coordinate}).", face, fpIdx, fp.Point));
				}
				ordinal++;
			}
			else if (duple.B < 0)
			{
				ordinal++;
			}
		}
		if (ordinal % 2 != 0)
		{
			throw new StructuredException(Message.GenericError("Node/Face connectivity error near ({Coordinate}).", fp.Point));
		}
	}

	public OperationResult LabelMesh(ConceptualMesh cmesh, out List<int> cellRegions, out List<int> faceArcs)
	{
		OperationResult operationResult = new OperationResult();
		faceArcs = new List<int>(FaceCount);
		Queue<int> cellQueue = new Queue<int>();
		cellRegions = new List<int>(CellCount);
		List<int> locCellRegions = cellRegions;
		for (int i = 0; i < CellCount; i++)
		{
			cellRegions.Add(-1);
		}
		cmesh.PointIndex();
		for (int j = 0; j < FaceCount; j++)
		{
			if (FaceConstraints[j] < (ConstraintState)0)
			{
				faceArcs.Add(-1);
				continue;
			}
			VirtualConstraints.Constraint constraint = constraints[(int)FaceConstraints[j]];
			faceArcs.Add(constraint.ArcIdx);
			int popsTerm = -1;
			GetPairNodePointOnConstraint(FaceNodes[j], (int)FaceConstraints[j], out var pops);
			popsTerm = ((!(pops.B > pops.A)) ? 1 : 0);
			for (int k = 0; k < 2; k++)
			{
				int idx = (k + popsTerm) % 2;
				if (FaceCells[j][k] >= 0)
				{
					if (cmesh.FaceCells[constraint.ArcIdx][idx] < 0)
					{
						throw new DynamicMeshException("SlidingMesh cell not owned by a ConceptualMesh region");
					}
					QueueCell(FaceCells[j][k], cmesh.FaceCells[constraint.ArcIdx][idx]);
				}
			}
		}
		while (cellQueue.Count > 0)
		{
			int index = cellQueue.Dequeue();
			int regionIdx = cellRegions[index];
			foreach (HalfEdge cellFace in CellFaces[index])
			{
				if (faceArcs[cellFace.fIdx] < 0)
				{
					int halfEdgeToOppositeCell = HalfEdgeToOppositeCell(cellFace);
					if (halfEdgeToOppositeCell >= 0)
					{
						QueueCell(halfEdgeToOppositeCell, regionIdx);
					}
				}
			}
		}
		int ordinal = 0;
		int ordinal2 = 0;
		for (int l = 0; l < CellCount; l++)
		{
			if (cellRegions[l] < 0)
			{
				if (CellFaces[l].Count == 0)
				{
					ordinal++;
				}
				else
				{
					ordinal2++;
				}
				cellRegions[l] = 0;
			}
		}
		if (ordinal > 0)
		{
			operationResult.AddFailure(new Message("Internal error in mesh gen. Output includes degenerate cell(s)"));
		}
		if (ordinal2 > 0)
		{
			operationResult.AddFailure(new Message("Internal error in mesh gen. Output includes cell(s) that were within a region"));
		}
		return operationResult;
		void QueueCell(int cIdx, int locCellRegions2)
		{
			if (locCellRegions[cIdx] < 0)
			{
				locCellRegions[cIdx] = locCellRegions2;
				cellQueue.Enqueue(cIdx);
			}
		}
	}

	public Point ComputeCellCenter(int cIdx)
	{
		if (CellTypes[cIdx] == CellType.CELL_1D_JUNCTION || CellTypes[cIdx] == CellType.CELL_1D_REACH)
		{
			return ComputeCellSmoothCenter(cIdx);
		}
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			Polyline polyline = FaceSmoothLine[cellFace.fIdx];
			if (polyline != null && polyline.Count > 2)
			{
				return ComputeCellSmoothCenter(cIdx);
			}
		}
		return CellNudgedCircumcenter(cIdx, DefaultCircumcenterClearance);
	}

	public Point ComputeCellSmoothCenter(int cIdx)
	{
		Polygon polygon = CellSmoothPolygon(cIdx);
		List<HalfEdge> cellFace = CellFaces[cIdx];
		Point point4;
		if (cellFace.Count == 4)
		{
			Polyline faceSmoothGeometry = GetFaceSmoothGeometry(cellFace[0].fIdx);
			Polyline faceSmoothGeometry2 = GetFaceSmoothGeometry(cellFace[1].fIdx);
			Polyline faceSmoothGeometry3 = GetFaceSmoothGeometry(cellFace[2].fIdx);
			Polyline faceSmoothGeometry4 = GetFaceSmoothGeometry(cellFace[3].fIdx);
			Point point = 0.5 * (faceSmoothGeometry.PointAtFraction(0.5).Point + faceSmoothGeometry3.PointAtFraction(0.5).Point);
			Point point2 = 0.5 * (faceSmoothGeometry2.PointAtFraction(0.5).Point + faceSmoothGeometry4.PointAtFraction(0.5).Point);
			Point point3 = 0.25 * (faceSmoothGeometry.First() + faceSmoothGeometry.Last() + faceSmoothGeometry3.First() + faceSmoothGeometry3.Last());
			point4 = point + point2 + -1.0 * point3;
		}
		else if (CellFaces[cIdx].Count == 3)
		{
			double computed = 0.33333333333333;
			Polyline faceSmoothGeometry5 = GetFaceSmoothGeometry(cellFace[0].fIdx);
			Polyline faceSmoothGeometry6 = GetFaceSmoothGeometry(cellFace[1].fIdx);
			Polyline faceSmoothGeometry7 = GetFaceSmoothGeometry(cellFace[2].fIdx);
			Point point5;
			Point point6;
			if (cellFace[0].orient == 0)
			{
				point5 = faceSmoothGeometry5.First();
				point6 = faceSmoothGeometry5.Last();
			}
			else
			{
				point5 = faceSmoothGeometry5.Last();
				point6 = faceSmoothGeometry5.First();
			}
			Point point7 = ((cellFace[1].orient != 0) ? faceSmoothGeometry6.First() : faceSmoothGeometry6.Last());
			Point point8 = computed * point5 + (1.0 - computed) * faceSmoothGeometry6.PointAtFraction(0.5).Point;
			Point point9 = computed * point6 + (1.0 - computed) * faceSmoothGeometry7.PointAtFraction(0.5).Point;
			Point point10 = computed * point7 + (1.0 - computed) * faceSmoothGeometry5.PointAtFraction(0.5).Point;
			Point point11 = computed * (point5 + point6 + point7);
			point4 = point8 + point9 + point10 + -2.0 * point11;
			if (!polygon.Contains(point4))
			{
				point4 = polygon.ComputeCentroid();
			}
		}
		else
		{
			point4 = polygon.ComputeCentroid();
		}
		return point4;
	}

	public void MergeMesh(SlidingMesh other, double nodeTolerance = 0.001)
	{
		Dictionary<int, int> arcToConstraint = null;
		MergeMesh(other, out var _, out var _, out var _, ref arcToConstraint, nodeTolerance);
	}

	public void MergeMesh(SlidingMesh other, ref Dictionary<int, int> arcToConstraint, double nodeTolerance = 0.001)
	{
		MergeMesh(other, out var _, out var _, out var _, ref arcToConstraint, nodeTolerance);
	}

	public void MergeMesh(SlidingMesh other, out int[] nodeMap, out HalfEdge[] faceMap, out int[] cellMap, ref Dictionary<int, int> arcToConstraint, double nodeTolerance = 0.001)
	{
		MergeMeshConstraints(other, out var constraintMap, ref arcToConstraint);
		int nodeCount = NodeCount;
		MergeMeshNodes(other, nodeTolerance, constraintMap, out nodeMap);
		MergeMeshFaces(other, nodeCount, constraintMap, nodeMap, out faceMap);
		MergeMeshCells(other, faceMap, out cellMap);
	}

	public void RelaxConstraintMode()
	{
		for (int i = 0; i < constraints.Count; i++)
		{
			if (constraints[i].mode == VirtualConstraints.ConstraintMode.FIXED)
			{
				constraints.SetMode(i, VirtualConstraints.ConstraintMode.SLIDE_ADD_REMOVE);
			}
		}
	}

	protected void MergeMeshConstraints(SlidingMesh other, out int[] constraintMap, ref Dictionary<int, int> arcToConstraint)
	{
		constraintMap = new int[other.constraints.Count];
		if (arcToConstraint == null)
		{
			arcToConstraint = new Dictionary<int, int>();
			for (int i = 0; i < constraints.Count; i++)
			{
				arcToConstraint[constraints[i].ArcIdx] = i;
			}
		}
		if (constraints == other.constraints)
		{
			for (int j = 0; j < other.constraints.Count; j++)
			{
				constraintMap[j] = j;
			}
			return;
		}
		for (int k = 0; k < other.constraints.Count; k++)
		{
			int arcIdx = other.constraints[k].ArcIdx;
			if (arcToConstraint.TryGetValue(arcIdx, out var constraintsOtherTerm))
			{
				Polyline polyline = other.constraints[k].Polyline;
				_ = constraints[constraintsOtherTerm].Polyline;
				for (int l = 0; l < polyline.Count; l++)
				{
				}
			}
			else
			{
				constraintsOtherTerm = constraints.Add(other.constraints[k]);
				arcToConstraint[arcIdx] = constraintsOtherTerm;
			}
			constraintMap[k] = constraintsOtherTerm;
		}
	}

	public void MergeMeshNodes(SlidingMesh other, double nodeTolerance, int[] constraintMap, out int[] nodeMap)
	{
		nodeMap = new int[other.NodeCount];
		SpatialIndex<int> spatialIndex = PointIndex();
		for (int i = 0; i < other.NodeCount; i++)
		{
			if (other.NodeDeleted[i])
			{
				nodeMap[i] = -1;
				continue;
			}
			List<int> elements = spatialIndex.GetElements(other.NodePoints[i], nodeTolerance);
			double nodeTolerance2 = nodeTolerance;
			int nodeMap2 = -1;
			foreach (int element in elements)
			{
				double distanceTo = other.NodePoints[i].DistanceTo(NodePoints[element]);
				if (distanceTo <= nodeTolerance2)
				{
					nodeMap2 = element;
					nodeTolerance2 = distanceTo;
				}
			}
			if (nodeMap2 >= 0)
			{
				nodeMap[i] = nodeMap2;
				continue;
			}
			nodeMap2 = AddNode(other.NodePoints[i]);
			nodeMap[i] = nodeMap2;
			if (other.NodeConstraints[i] >= (ConstraintState)0)
			{
				NodeConstraints[nodeMap2] = (ConstraintState)constraintMap[(int)other.NodeConstraints[i]];
				NodePointOnConstraint[nodeMap2] = other.NodePointOnConstraint[i];
			}
			else
			{
				NodeConstraints[nodeMap2] = other.NodeConstraints[i];
			}
		}
	}

	public void MergeMeshFaces(SlidingMesh other, int origNodeCount, int[] constraintMap, int[] nodeMap, out HalfEdge[] faceMap)
	{
		faceMap = new HalfEdge[other.FaceCount];
		HalfEdge halfEdge = default;
		for (int i = 0; i < other.FaceCount; i++)
		{
			if (other.FaceDeleted[i])
			{
				faceMap[i] = new HalfEdge(-1, 0);
				continue;
			}
			int nodeMap2 = nodeMap[other.FaceNodes[i][0]];
			int nodeMap3 = nodeMap[other.FaceNodes[i][1]];
			if (nodeMap2 <= origNodeCount && nodeMap3 <= origNodeCount)
			{
				halfEdge = NodesToHalfEdge(nodeMap2, nodeMap3);
				if (!halfEdge.IsNull())
				{
					faceMap[i] = halfEdge;
					if (FaceTargetLength[halfEdge.fIdx] < 0f)
					{
						FaceTargetLength[halfEdge.fIdx] = other.FaceTargetLength[i];
					}
					else if (!(other.FaceTargetLength[i] < 0f))
					{
						float faceTargetLength = 0.5f * (FaceTargetLength[halfEdge.fIdx] + other.FaceTargetLength[i]);
						_ = Math.Abs(FaceTargetLength[halfEdge.fIdx] - other.FaceTargetLength[i]) / faceTargetLength;
						FaceTargetLength[halfEdge.fIdx] = faceTargetLength;
					}
					continue;
				}
			}
			halfEdge.fIdx = AddFace(nodeMap2, nodeMap3);
			halfEdge.orient = 0;
			FaceTargetLength[halfEdge.fIdx] = other.FaceTargetLength[i];
			if (other.FaceConstraints[i] >= (ConstraintState)0)
			{
				FaceConstraints[halfEdge.fIdx] = (ConstraintState)constraintMap[(int)other.FaceConstraints[i]];
			}
			else
			{
				FaceConstraints[halfEdge.fIdx] = other.FaceConstraints[i];
			}
			FaceSmoothLine[halfEdge.fIdx] = other.FaceSmoothLine[i];
			faceMap[i] = halfEdge;
		}
	}

	public void MergeMeshCells(SlidingMesh other, HalfEdge[] faceMap, out int[] cellMap)
	{
		cellMap = new int[other.CellCount];
		for (int i = 0; i < other.CellCount; i++)
		{
			if (other.CellDeleted[i])
			{
				cellMap[i] = -1;
				continue;
			}
			int cellMap2 = AddCell();
			foreach (HalfEdge cellFace in other.CellFaces[i])
			{
				HalfEdge halfEdge = faceMap[cellFace.fIdx];
				CellAddFace(cellMap2, halfEdge.fIdx, (byte)(halfEdge.orient ^ cellFace.orient));
			}
			CellTypes[cellMap2] = other.CellTypes[i];
			CellSides[cellMap2] = other.CellSides[i];
			cellMap[i] = cellMap2;
		}
	}

	public void UpdateConceptualMesh(ConceptualMesh cmesh)
	{
		double tolerance = 1E-06;
		HashSet<int> intSet = new HashSet<int>();
		foreach (int nodeIterator in NodeIterator())
		{
			if (NodeConstraints[nodeIterator] != ConstraintState.FIXED)
			{
				continue;
			}
			foreach (HalfEdge nodeToHalfEdge in NodeToHalfEdges(nodeIterator))
			{
				ConstraintState constraintState = FaceConstraints[nodeToHalfEdge.fIdx];
				if (constraintState < (ConstraintState)0 || intSet.Contains(nodeToHalfEdge.fIdx))
				{
					continue;
				}
				List<int> traverseArc = TraverseArc(nodeToHalfEdge, intSet);
				if (traverseArc.Count != 2)
				{
					int arcIdx = constraints[(int)constraintState].ArcIdx;
					Polyline polyline = new Polyline(traverseArc.Select((int nIdx) => NodePoints[nIdx]));
					Polyline polyline2 = cmesh.FaceNodeString[arcIdx];
					if ((!(polyline.First().DistanceTo(polyline2.First()) < tolerance) || !(polyline.Last().DistanceTo(polyline2.Last()) < tolerance)) && polyline.Last().DistanceTo(polyline2.First()) < tolerance && polyline.First().DistanceTo(polyline2.Last()) < tolerance)
					{
						polyline.Reverse();
					}
					cmesh.FaceNodeString[arcIdx] = polyline;
				}
			}
		}
	}

	public virtual void DeleteNakedFaces(bool deleteConstrained)
	{
		foreach (int faceIterator in FaceIterator())
		{
			if ((deleteConstrained || FaceConstraints[faceIterator] < (ConstraintState)0) && FaceCells[faceIterator][0] < 0 && FaceCells[faceIterator][1] < 0)
			{
				DeleteFace(faceIterator);
			}
		}
	}

	public IEnumerable<int> UnconstrainedFaces()
	{
		for (int fIdx = 0; fIdx < FaceDeleted.Count; fIdx++)
		{
			if (!FaceDeleted[fIdx] && FaceConstraints[fIdx] < (ConstraintState)0)
			{
				yield return fIdx;
			}
		}
	}

	protected List<int> TraverseArc(HalfEdge he, HashSet<int> visitedFaces)
	{
		int halfEdgeNodeBack = HalfEdgeNodeBack(he);
		List<int> intList = new List<int> { halfEdgeNodeBack };
		_ = FaceConstraints[he.fIdx];
		for (int i = 0; i < NodeCount; i++)
		{
			int halfEdgeNodeForward = HalfEdgeNodeForward(he);
			intList.Add(halfEdgeNodeForward);
			visitedFaces.Add(he.fIdx);
			if (NodeConstraints[halfEdgeNodeForward] == ConstraintState.FIXED || halfEdgeNodeForward == halfEdgeNodeBack)
			{
				break;
			}
			int fIdx = he.fIdx;
			he = HalfEdge.NullHalfEdge;
			foreach (HalfEdge nodeToHalfEdge in NodeToHalfEdges(halfEdgeNodeForward))
			{
				if (nodeToHalfEdge.fIdx != fIdx && FaceConstraints[nodeToHalfEdge.fIdx] >= (ConstraintState)0)
				{
					he = nodeToHalfEdge;
					break;
				}
			}
		}
		return intList;
	}

	public override bool FlipFace(int fIdx)
	{
		if (FaceConstraints[fIdx] >= (ConstraintState)0)
		{
			return false;
		}
		Duple<int> duple = FaceCells[fIdx];
		if (duple.A < 0 || duple.B < 0)
		{
			return false;
		}
		if (CellNumSides(duple.A) != 3 || CellNumSides(duple.B) != 3)
		{
			return false;
		}
		HalfEdge he = new HalfEdge(fIdx, 0);
		int index = HalfEdgeNodeBack(he);
		int index2 = HalfEdgeNodeForward(he);
		int index3 = HalfEdgeNodeForward(HalfEdgeNextByCell(he));
		int index4 = HalfEdgeNodeForward(HalfEdgeNextByCell(HalfEdgeFlip(he)));
		if (NativePredicates.CounterClockwise(NodePoints[index], NodePoints[index4], NodePoints[index3]) <= 0.0)
		{
			return false;
		}
		if (NativePredicates.CounterClockwise(NodePoints[index3], NodePoints[index4], NodePoints[index2]) <= 0.0)
		{
			return false;
		}
		if (!base.FlipFace(fIdx))
		{
			return false;
		}
		FaceSmoothLine[fIdx] = null;
		return true;
	}

	public override bool SplitCell(int cIdx, int nodeA, int nodeB, out int newFaceIndex)
	{
		Polyline plineA = new Polyline(2)
		{
			NodePoints[nodeA],
			NodePoints[nodeB]
		};
		newFaceIndex = -1;
		for (int i = 0; i < CellFaces[cIdx].Count; i++)
		{
			Polyline polyline = FaceSmoothLine[CellFaces[cIdx][i].fIdx];
			if (polyline != null && Polyline.Conflict(plineA, polyline))
			{
				return false;
			}
			int halfEdgeNodeForward = HalfEdgeNodeForward(CellFaces[cIdx][i]);
			Vector vec;
			if (halfEdgeNodeForward == nodeA)
			{
				vec = NodePoints[nodeB] - NodePoints[nodeA];
			}
			else
			{
				if (halfEdgeNodeForward != nodeB)
				{
					continue;
				}
				vec = NodePoints[nodeA] - NodePoints[nodeB];
			}
			Vector vec2 = HalfEdgeVector(HalfEdgeFlip(CellFaces[cIdx][i]));
			Vector vector = HalfEdgeVector(CellFaces[cIdx][(i + 1) % CellFaces[cIdx].Count]);
			double angleBetweenRads = vector.AngleBetweenRads(in vec2);
			double angleBetweenRads2 = vector.AngleBetweenRads(in vec);
			if (angleBetweenRads < 0.0)
			{
				angleBetweenRads += Math.PI * 2.0;
			}
			if (angleBetweenRads2 < 0.0)
			{
				angleBetweenRads2 += Math.PI * 2.0;
			}
			if (angleBetweenRads <= angleBetweenRads2)
			{
				return false;
			}
		}
		return base.SplitCell(cIdx, nodeA, nodeB, out newFaceIndex);
	}

	public override int MergeFaces(int nIdx)
	{
		return MergeFaces(nIdx, useSmooth: true);
	}

	public int MergeFaces(int nIdx, bool useSmooth)
	{
		if (NodeConstraints[nIdx] == ConstraintState.FIXED)
		{
			return -1;
		}
		List<HalfEdge> nodeToHalfEdges = NodeToHalfEdges(nIdx);
		if (nodeToHalfEdges.Count != 2)
		{
			throw new DynamicMeshException("JoinFaces requires degree-2 node");
		}
		int halfEdgeNodeForward = HalfEdgeNodeForward(nodeToHalfEdges[0]);
		Polyline polyline = null;
		if (useSmooth)
		{
			polyline = GetFaceSmoothGeometry(HalfEdgeFlip(nodeToHalfEdges[0])).Clone();
			polyline.RemoveAt(polyline.PointCount - 1);
			polyline.AddRange(GetFaceSmoothGeometry(nodeToHalfEdges[1]));
		}
		float faceTargetLength = Math.Max(FaceTargetLength[nodeToHalfEdges[0].fIdx], FaceTargetLength[nodeToHalfEdges[1].fIdx]);
		ConstraintState faceConstraint = FaceConstraints[nodeToHalfEdges[0].fIdx];
		int mergeFaces = base.MergeFaces(nIdx);
		FaceTargetLength[mergeFaces] = faceTargetLength;
		FaceConstraints[mergeFaces] = faceConstraint;
		if (polyline == null || FaceNodes[mergeFaces].A == halfEdgeNodeForward)
		{
			FaceSmoothLine[mergeFaces] = polyline;
		}
		else
		{
			if (FaceNodes[mergeFaces].B != halfEdgeNodeForward)
			{
				throw new DynamicMeshException("Topology corrupt");
			}
			polyline.Reverse();
			FaceSmoothLine[mergeFaces] = polyline;
		}
		return mergeFaces;
	}

	public void SetMultipointFaces(bool multipoint = true)
	{
		foreach (int faceIterator in FaceIterator())
		{
			FaceSmoothLine[faceIterator] = ComputeSubFaceGeometry(faceIterator, constrained: true, multipoint);
		}
	}

	public Polyline GetFaceSmoothGeometry(int fIdx)
	{
		return FaceSmoothLine[fIdx] ?? FaceSimpleGeometry(fIdx);
	}

	public Polyline GetFaceSmoothGeometry(HalfEdge he)
	{
		return GetFaceSmoothGeometry(he, copy: false);
	}

	public Polyline GetFaceSmoothGeometry(HalfEdge he, bool copy)
	{
		Polyline polyline = FaceSmoothLine[he.fIdx];
		if (polyline == null)
		{
			polyline = FaceSimpleGeometry(he.fIdx);
			if (he.orient == 1)
			{
				polyline.Reverse();
			}
		}
		else
		{
			if (he.orient == 1)
			{
				polyline = polyline.Clone();
				polyline.Reverse();
				return polyline;
			}
			if (copy)
			{
				polyline = polyline.Clone();
			}
		}
		return polyline;
	}

	public Polygon GetCellSmoothSubset(HalfEdge he, int nodeStop)
	{
		return GetCellSmoothSubset(HalfEdgeToCell(he), HalfEdgeNodeBack(he), nodeStop);
	}

	public Polygon GetCellSmoothSubset(int cIdx, int nodeStart, int nodeStop)
	{
		return GetCellSubsetGeneric(cIdx, nodeStart, nodeStop, GetFaceSmoothGeometry);
	}

	public Polygon GetCellSimpleSubset(int cIdx, int nodeStart, int nodeStop)
	{
		return GetCellSubsetGeneric(cIdx, nodeStart, nodeStop, FaceSimpleGeometry);
	}

	public Polygon GetCellSubsetGeneric(int cIdx, int nodeStart, int nodeStop, Func<HalfEdge, Polyline> getFaceGeom)
	{
		List<HalfEdge> cellFace = CellFaces[cIdx];
		int i;
		for (i = 0; i < cellFace.Count && HalfEdgeNodeBack(cellFace[i]) != nodeStart; i++)
		{
		}
		Polygon polygon = new Polygon(getFaceGeom(cellFace[i]));
		for (int j = 1; j < cellFace.Count; j++)
		{
			int index = (i + j) % cellFace.Count;
			if (HalfEdgeNodeBack(cellFace[index]) == nodeStop)
			{
				return polygon;
			}
			polygon.RemoveAt(polygon.PointCount - 1);
			polygon.AddRange(getFaceGeom(cellFace[index]));
		}
		throw new Exception("Should not get here");
	}

	public Polygon CellSmoothPolygon(int cIdx)
	{
		return CellGenericPolygon(cIdx, (int fIdx) => GetFaceSmoothGeometry(fIdx));
	}

	public double CellSmoothArea(int cIdx)
	{
		return CellSmoothPolygon(cIdx).Area;
	}

	public Polyline ComputeValidSubFaceGeometry(int fIdx, bool constrained = true, bool interior = true)
	{
		Polyline polyline = ComputeSubFaceGeometry(fIdx, constrained, interior);
		if (polyline.PointCount > 2 && !CheckSubfaceGeometry(fIdx, polyline))
		{
			return FaceSimpleGeometry(fIdx);
		}
		return polyline;
	}

	public Polyline ComputeSubFaceGeometry(int fIdx, bool constrained = true, bool interior = true)
	{
		Polyline polyline;
		if (constrained && FaceConstraints[fIdx] >= (ConstraintState)0)
		{
			polyline = ComputeConstrainedSubFaceGeometry(fIdx);
		}
		else if (interior && transform != null && transform.IsReversible)
		{
			polyline = ComputeInteriorSubFaceGeometry(fIdx);
			if (polyline == null)
			{
				polyline = FaceSimpleGeometry(fIdx);
			}
		}
		else
		{
			polyline = FaceSimpleGeometry(fIdx);
		}
		return polyline;
	}

	public bool CheckSubfaceGeometry(int fIdx, Polyline result)
	{
		for (int i = 0; i < 2; i++)
		{
			int faceCell = FaceCells[fIdx][i];
			if (faceCell >= 0)
			{
				foreach (HalfEdge cellFace in CellFaces[faceCell])
				{
					if (cellFace.fIdx != fIdx && Polyline.Conflict(result, GetFaceSmoothGeometry(cellFace.fIdx)))
					{
						return false;
					}
				}
				continue;
			}
			foreach (int nodeToEdge in NodeToEdges(FaceNodes[fIdx].A))
			{
				if (nodeToEdge != fIdx && (FaceCells[nodeToEdge].A < 0 || FaceCells[nodeToEdge].B < 0) && Polyline.Conflict(result, GetFaceSmoothGeometry(nodeToEdge)))
				{
					return false;
				}
			}
		}
		return true;
	}

	private Polyline ComputeConstrainedSubFaceGeometry(int fIdx)
	{
		int faceConstraint = (int)FaceConstraints[fIdx];
		VirtualConstraints.Constraint constraint = constraints[faceConstraint];
		Duple<int> nodes = FaceNodes[fIdx];
		GetPairNodePointOnConstraint(nodes, faceConstraint, out var pops);
		for (int i = 0; i < 2; i++)
		{
			if (pops[i].Fraction == 1.0 && pops[i].SegmentIndex < constraint.Polyline.PointCount - 2)
			{
				PointOnPolyline pop = pops[i];
				pop.Fraction = 0.0;
				pop.SegmentIndex++;
				pops[i] = pop;
			}
		}
		Polyline polyline = constraint.Polyline.SubsectionDirected(pops[0], pops[1]);
		polyline[0] = NodePoints[nodes[0]];
		polyline[polyline.Count - 1] = NodePoints[nodes[1]];
		polyline.InplaceDepupe(1E-07);
		return polyline;
	}

	private Polyline ComputeInteriorSubFaceGeometry(int fIdx)
	{
		if (transform is QuadTransformLaplacian quadTransformLaplacian)
		{
			return quadTransformLaplacian.InterpolateSegment(FaceSegment(fIdx));
		}
		Point[] points = new Point[2]
		{
			NodePoints[FaceNodes[fIdx].A],
			NodePoints[FaceNodes[fIdx].B]
		};
		Point[] reverseTransform = transform.ReverseTransform(points);
		Polyline polyline = new Polyline { points[0] };
		int ordinal = 10;
		for (int i = 1; i < ordinal - 1; i++)
		{
			double computed = (double)i / ((double)ordinal - 1.0);
			polyline.Add(transform.Transform((1.0 - computed) * reverseTransform[0] + computed * reverseTransform[1]));
		}
		polyline.Add(points[1]);
		return polyline;
	}

	public override double HalfEdgeBearing(HalfEdge he)
	{
		return HalfEdgeVector(he).MathBearing;
	}

	public Vector HalfEdgeVector(HalfEdge he)
	{
		Point point = NodePoints[HalfEdgeNodeBack(he)];
		Polyline polyline = FaceSmoothLine[he.fIdx];
		Point point2;
		if (polyline == null)
		{
			point2 = NodePoints[HalfEdgeNodeForward(he)];
		}
		else
		{
			point2 = ((he.orient != 0) ? polyline[polyline.Count - 2] : polyline[1]);
		}
		return point2 - point;
	}

	public void NudgePoint(int nIdx, double nudgeConvex, ref double x, ref double y)
	{
		if (nudgeConvex < 0.0)
		{
			return;
		}
		Point origin = NodePoints[nIdx];
		if (nudgeConvex >= 1.0)
		{
			x = origin.X;
			y = origin.Y;
			return;
		}
		List<HalfEdge> nodeToSortedHalfEdges = NodeToSortedHalfEdges(nIdx);
		Point pnt = new Point(x, y);
		for (int i = 0; i < nodeToSortedHalfEdges.Count; i++)
		{
			int halfEdgeToCell = HalfEdgeToCell(nodeToSortedHalfEdges[i]);
			if (halfEdgeToCell >= 0)
			{
				int index = (i + 1) % nodeToSortedHalfEdges.Count;
				int n = HalfEdgeNodeForward(nodeToSortedHalfEdges[i]);
				int n2 = HalfEdgeNodeForward(nodeToSortedHalfEdges[index]);
				NudgeLeftOfLine(n, n2);
				if (CellNumSides(halfEdgeToCell) > 3)
				{
					HalfEdge he = nodeToSortedHalfEdges[i];
					HalfEdge he2 = HalfEdgeFlip(nodeToSortedHalfEdges[index]);
					int n3 = HalfEdgeNodeForward(HalfEdgeNext(he));
					NudgeLeftOfLine(n, n3);
					int n4 = HalfEdgeNodeBack(HalfEdgePrev(he2));
					NudgeLeftOfLine(n4, n2);
				}
			}
		}
		x = pnt.X;
		y = pnt.Y;
		void NudgeLeftOfLine(int index2, int index3)
		{
			Point point = (1.0 - nudgeConvex) * NodePoints[index2] + nudgeConvex * origin;
			Point point2 = (1.0 - nudgeConvex) * NodePoints[index3] + nudgeConvex * origin;
			if (!(NativePredicates.CounterClockwise(pnt, point, point2) > 0.0))
			{
				if (NativePredicates.CounterClockwise(origin, point, point2) < 0.0)
				{
					pnt = origin;
				}
				else
				{
					Segment.IntersectsLineSegment(pnt, origin, point, point2, out var intersectingPoint);
					pnt = intersectingPoint;
				}
			}
		}
	}

	public void ClipToBoundary(ConceptualMesh cmesh, int regionIdx, ClipMode clipMode, bool useSmoothBoundary = true, bool includeExterior = true, bool includeBreaklines = true)
	{
		SpatialIndex<int> cellIndex = CellIndex();
		ClipToBoundaryRagged(cmesh, regionIdx, (!useSmoothBoundary) ? RaggedClipMode.NODES : RaggedClipMode.SMOOTH, includeExterior, includeBreaklines, clipConstrained: false, cellIndex);
	}

	public void ClipToPolylinesRagged(List<Polyline> lines, bool clipConstrained, bool includesExterior, SpatialIndex<int> cellIndex, bool robust = true)
	{
		List<int> intList = new List<int>();
		List<int> intListList = new List<int>();
		Dictionary<int, double> doubleByInt = null;
		if (!robust)
		{
			doubleByInt = new Dictionary<int, double>();
		}
		List<MeshIntersection> crossings = null;
		foreach (Polyline line in lines)
		{
			int cellToDelete;
			if (line is Polygon)
			{
				if (!robust)
				{
					SliceByRingRagged(line, closed: true, doubleByInt, intList, out cellToDelete, out crossings, cellIndex);
				}
				else
				{
					SliceByRingRaggedRobust(line, closed: true, intList, out cellToDelete, out crossings, cellIndex);
				}
			}
			else if (!robust)
			{
				SliceByRingRagged(line, closed: false, null, intList, out cellToDelete, out crossings, cellIndex);
			}
			else
			{
				SliceByRingRaggedRobust(line, closed: false, intList, out cellToDelete, out crossings, cellIndex);
			}
			if (cellToDelete >= 0)
			{
				intListList.Add(cellToDelete);
			}
		}
		HashSet<HalfEdge> exposedFaces = new HashSet<HalfEdge>();
		foreach (int ordinal in intList)
		{
			if (!FaceDeleted[ordinal] && (clipConstrained || FaceConstraints[ordinal] < (ConstraintState)0))
			{
				DeleteFaceCascade(ordinal, exposedFaces);
			}
		}
		if (!robust)
		{
			List<int> intList2 = new List<int>();
			foreach (KeyValuePair<int, double> localValue in doubleByInt)
			{
				if (!(localValue.Value >= 0.0))
				{
					int key = localValue.Key;
					intList2.AddRange(NodeToCells(key));
				}
			}
			for (int i = 0; i < intList2.Count; i++)
			{
				int ordinal2 = intList2[i];
				if (CellDeleted[ordinal2])
				{
					continue;
				}
				foreach (HalfEdge cellFace in CellFaces[ordinal2])
				{
					int halfEdgeToOppositeCell = HalfEdgeToOppositeCell(cellFace);
					if (halfEdgeToOppositeCell >= 0 && !CellDeleted[halfEdgeToOppositeCell])
					{
						intList2.Add(halfEdgeToOppositeCell);
					}
				}
				DeleteCell(ordinal2);
			}
		}
		else
		{
			List<int> intList3 = null;
			HashSet<int> intSet = new HashSet<int>();
			for (int j = 0; j < NodeCount; j++)
			{
				if (NodeDeleted[j] || intSet.Contains(j))
				{
					continue;
				}
				int areaIsNegativeTerm = 0;
				int ordinal3 = 0;
				foreach (Polyline line2 in lines)
				{
					if (line2 is Polygon polygon)
					{
						bool areaIsNegative = polygon.Area < 0.0;
						if (polygon.Contains(NodePoints[j]))
						{
							areaIsNegativeTerm = ((!areaIsNegative) ? (areaIsNegativeTerm + 1) : (areaIsNegativeTerm - 1));
						}
						ordinal3++;
					}
				}
				if (ordinal3 == 0)
				{
					break;
				}
				if (!includesExterior)
				{
					areaIsNegativeTerm++;
				}
				bool condition = areaIsNegativeTerm > 0;
				if (intList3 == null)
				{
					intList3 = new List<int>();
				}
				intList3.Add(j);
				while (intList3.Count > 0)
				{
					int ordinal4 = intList3.Last();
					intList3.RemoveAt(intList3.Count - 1);
					if ((condition && intSet.Contains(ordinal4)) || (!condition && NodeDeleted[ordinal4]))
					{
						continue;
					}
					foreach (int nodeToNode in NodeToNodes(ordinal4))
					{
						if (!condition || !intSet.Contains(nodeToNode))
						{
							intList3.Add(nodeToNode);
						}
					}
					if (condition)
					{
						intSet.Add(ordinal4);
					}
					else
					{
						DeleteNodeCascade(ordinal4);
					}
				}
			}
		}
		foreach (int ordinal5 in intListList)
		{
			if (!CellDeleted[ordinal5])
			{
				DeleteCell(ordinal5);
			}
		}
		DeleteNakedFaces(clipConstrained);
		DeleteNakedNodes();
	}

	public void ClipToBoundaryRagged(ConceptualMesh cmesh, int regionIdx, RaggedClipMode raggedClipMode, bool includeExterior, bool includeBreaklines, bool clipConstrained, SpatialIndex<int> cellIndex)
	{
		List<Polyline> lines = new List<Polyline>();
		bool raggedClipModeSmoothTerm = raggedClipMode == RaggedClipMode.SMOOTH || raggedClipMode == RaggedClipMode.SMOOTH_AND_NODES;
		bool raggedClipModeNodesTerm = raggedClipMode == RaggedClipMode.NODES || raggedClipMode == RaggedClipMode.SMOOTH_AND_NODES;
		if (raggedClipModeSmoothTerm)
		{
			AddComplexPolygon(cmesh.CellSmoothComplexPolygon(regionIdx));
		}
		if (raggedClipModeNodesTerm)
		{
			AddComplexPolygon(cmesh.CellNodeComplexPolygon(regionIdx));
		}
		if (includeBreaklines)
		{
			foreach (int cellBreakline in cmesh.CellBreaklines(regionIdx))
			{
				if (raggedClipModeSmoothTerm)
				{
					lines.Add(cmesh.FaceSmoothLine[cellBreakline]);
				}
				if (raggedClipModeNodesTerm)
				{
					lines.Add(cmesh.FaceNodeString[cellBreakline]);
				}
			}
		}
		ClipToPolylinesRagged(lines, clipConstrained, includeExterior, cellIndex);
		void AddComplexPolygon(ComplexPolygon boundaryPoly)
		{
			for (int i = 0; i < boundaryPoly.RingCount; i++)
			{
				if (i != 0 || includeExterior)
				{
					lines.Add(boundaryPoly.Rings[i]);
				}
			}
		}
	}

	public void SliceByRingRagged(Polyline ring, bool closed, Dictionary<int, double> nodeBalances, List<int> facesToDelete, out int cellToDelete, out List<MeshIntersection> crossings, SpatialIndex<int> cellIndex)
	{
		crossings = MeshIntersection.RingToCrossings(this, ring, out var crossedCells, cellIndex);
		if (crossedCells.Count > 0)
		{
			cellToDelete = crossedCells[0];
		}
		else
		{
			cellToDelete = -1;
		}
		foreach (MeshIntersection crossing in crossings)
		{
			facesToDelete.Add(crossing.halfEdge.fIdx);
			if (nodeBalances != null)
			{
				int a = FaceNodes[crossing.halfEdge.fIdx].A;
				int b = FaceNodes[crossing.halfEdge.fIdx].B;
				if (!nodeBalances.TryGetValue(a, out var nodeBalances2))
				{
					nodeBalances2 = 0.0;
				}
				if (!nodeBalances.TryGetValue(b, out var faceFractionSum))
				{
					faceFractionSum = 0.0;
				}
				if (crossing.halfEdge.orient == 0)
				{
					nodeBalances2 += 1.0 - crossing.faceFraction;
					faceFractionSum -= crossing.faceFraction;
				}
				else
				{
					nodeBalances2 -= 1.0 - crossing.faceFraction;
					faceFractionSum += crossing.faceFraction;
				}
				nodeBalances[a] = nodeBalances2;
				nodeBalances[b] = faceFractionSum;
			}
		}
	}

	public void SliceByRingRaggedRobust(Polyline pline, bool closed, List<int> facesToDelete, out int cellToDelete, out List<MeshIntersection> crossings, SpatialIndex<int> cellIndex)
	{
		crossings = MeshIntersection.RingToCrossingsRobust(this, pline, cellIndex);
		cellToDelete = -1;
		foreach (MeshIntersection crossing in crossings)
		{
			if (cellToDelete < 0 && crossing.cell >= 0)
			{
				cellToDelete = crossing.cell;
			}
			if (!crossing.halfEdge.IsNull())
			{
				facesToDelete.Add(crossing.halfEdge.fIdx);
			}
			else if (crossing.node >= 0)
			{
				facesToDelete.AddRange(NodeToEdges(crossing.node));
			}
		}
	}

	public bool NextFaceCrossing(Polyline ring, PointOnPolyline popStart, int cell, out MeshIntersection crossing, double tolerance = 1E-06)
	{
		return NextFaceCrossing(ring, popStart, PointOnPolyline.NullPointOnPolyline, cell, out crossing, tolerance);
	}

	public bool NextFaceCrossing(Polyline pline, PointOnPolyline popStart, PointOnPolyline popStop, int cell, out MeshIntersection crossing, double tolerance = 1E-06)
	{
		if (popStop.Fraction == 0.0 && popStop.SegmentIndex > 0)
		{
			popStop.Fraction = 1.0;
			popStop.SegmentIndex--;
		}
		crossing = new MeshIntersection();
		int segmentCount = ((!popStop.IsNull()) ? (popStop.SegmentIndex + 1) : pline.SegmentCount);
		for (int i = popStart.SegmentIndex; i < segmentCount; i++)
		{
			Segment segment = ((popStop.IsNull() || popStop.SegmentIndex != i) ? pline.Segments[i] : new Segment(pline.Points[i], popStop.Point));
			double computed = 1000000.0;
			double x = 0.0;
			double y = 0.0;
			foreach (HalfEdge cellFace in CellFaces[cell])
			{
				Point point = NodePoints[HalfEdgeNodeBack(cellFace)];
				Point point2 = NodePoints[HalfEdgeNodeForward(cellFace)];
				if (!(NativePredicates.CounterClockwise(point, point2, segment.B) > 0.0))
				{
					Segment seg = new Segment(point, point2);
					segment.IntersectPointCheckNodes(seg, out var x2, out var y2, out var t, out var t2);
					if (!double.IsNaN(t) && t < computed)
					{
						computed = t;
						crossing.halfEdge = cellFace;
						crossing.faceFraction = ((cellFace.orient == 0) ? t2 : (1.0 - t2));
						x = x2;
						y = y2;
					}
				}
			}
			if (computed <= 1.0 - tolerance)
			{
				crossing.pop = new PointOnPolyline(x, y, i, computed);
				return true;
			}
		}
		return false;
	}

	public bool NextCrossingOfFaces(Polyline pline, PointOnPolyline popStart, PointOnPolyline popStop, List<int> nodeString, out MeshIntersection crossing, out int hitNode, double relTolerance = 1E-06, double absTolerance = 0.0001)
	{
		new Polyline(nodeString.Select((int nIdx) => NodePoints[nIdx]));
		if (popStop.IsNull())
		{
			popStop = pline.LastPointOnPolyline();
		}
		if (popStop.Fraction == 0.0 && popStop.SegmentIndex > 0)
		{
			popStop.Fraction = 1.0;
			popStop.SegmentIndex--;
		}
		if (popStart.Fraction == 1.0)
		{
			popStart.Fraction = 0.0;
			popStart.SegmentIndex++;
		}
		crossing = new MeshIntersection();
		hitNode = -1;
		int ordinal = -1;
		double faceFraction = 0.0;
		double xComponent = 0.0;
		double yComponent = 0.0;
		int count = popStop.SegmentIndex + 1;
		int segmentIndex;
		for (segmentIndex = popStart.SegmentIndex; segmentIndex < count; segmentIndex++)
		{
			Point ptA = ((segmentIndex == popStart.SegmentIndex) ? popStart.Point : pline.Points[segmentIndex]);
			Point point = ((segmentIndex == popStop.SegmentIndex) ? popStop.Point : pline.Points[segmentIndex + 1]);
			Segment segment = new Segment(ptA, point);
			double computed = 1000000.0;
			Segment segment2 = Segment.NullSegment;
			for (int intervalIndex = 0; intervalIndex < nodeString.Count - 1; intervalIndex++)
			{
				Point ptA2 = NodePoints[nodeString[intervalIndex]];
				Point ptB = NodePoints[nodeString[intervalIndex + 1]];
				Segment segment3 = new Segment(ptA2, ptB);
				if (segment.IntersectPointCheckNodes(segment3, out var x, out var y, out var t, out var t2) && (segmentIndex != popStart.SegmentIndex || (!(t < relTolerance) && !(t * segment.Length < absTolerance))) && t < computed)
				{
					computed = t;
					ordinal = intervalIndex;
					faceFraction = t2;
					segment2 = segment3;
					xComponent = x;
					yComponent = y;
				}
			}
			if (computed > 1.0)
			{
				continue;
			}
			if (computed >= 1.0 - relTolerance)
			{
				if (popStop.SegmentIndex == segmentIndex)
				{
					if (faceFraction <= relTolerance)
					{
						hitNode = nodeString[ordinal];
						return false;
					}
					if (faceFraction >= 1.0 - relTolerance)
					{
						hitNode = nodeString[ordinal + 1];
						return false;
					}
					throw new DynamicMeshException($"Multipoint ends at non-node location near {xComponent},{yComponent}");
				}
				continue;
			}
			if (faceFraction > relTolerance && faceFraction < 1.0 - relTolerance)
			{
				if (segment2.PerpendicularSignedDistance(point) < 0.0)
				{
					break;
				}
				if (!(computed <= relTolerance))
				{
					throw new DynamicMeshException($"Multipoint intersected nodestring from wrong side at {xComponent},{yComponent}");
				}
				continue;
			}
			throw new DynamicMeshException($"Multipoint has strange intersection near {xComponent},{yComponent}");
		}
		if (segmentIndex < count)
		{
			crossing.halfEdge = NodesToHalfEdge(nodeString[ordinal], nodeString[ordinal + 1]);
			crossing.pop = pline.NearestPointOnPolyline(new Point(xComponent, yComponent));
			if (crossing.halfEdge.orient == 0)
			{
				crossing.faceFraction = faceFraction;
			}
			else
			{
				crossing.faceFraction = 1.0 - faceFraction;
			}
			return true;
		}
		return false;
	}

	public bool NextCrossingOfFace(Polyline pline, PointOnPolyline popStart, PointOnPolyline popStop, HalfEdge he, out MeshIntersection crossing, double tolerance = 1E-06)
	{
		int hitNode;
		return NextCrossingOfFaces(pline, popStart, popStop, new List<int>
		{
			HalfEdgeNodeBack(he),
			HalfEdgeNodeForward(he)
		}, out crossing, out hitNode, tolerance);
	}

	public void ClipByScaleAndClearance(ConceptualMesh cmesh, int regionIdx, ISizeFunction sizeFunction, double relTolerance = 0.05, double clearanceFactor = 0.66, bool includeConstrained = true, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		DynamicTINFactory<NativePredicates> dynamicTINFactory = new TrianglePaver(cmesh, regionIdx)
		{
			targetSizeFunction = new ConstantSizeFunction(1000000.0),
			stretchRate = 0.5f
		}.InitializeTin();
		int tri = 0;
		List<HalfEdge> halfEdges = new List<HalfEdge>();
		Stack<int> faceIterator = new Stack<int>(FaceIterator());
		int scaledCount = 3 * faceIterator.Count;
		while (scaledCount >= 0 && faceIterator.Count != 0)
		{
			if (reporter.CancelRequested)
			{
				return;
			}
			int pop = faceIterator.Pop();
			if (!FaceDeleted[pop] && (FaceCells[pop].A < 0 || FaceCells[pop].B < 0) && (includeConstrained || FaceConstraints[pop] < (ConstraintState)0))
			{
				if (FaceCells[pop].A < 0 && FaceCells[pop].B < 0)
				{
					DeleteFace(pop);
				}
				else
				{
					Segment seg = FaceSegment(pop);
					sizeFunction.QueryRange(seg.Midpoint(), checkInside: true, out var maximum, out var minimum);
					bool clearanceDynamicTINFactoryTerm = seg.Length > maximum * (1.0 + relTolerance) || seg.Length < minimum * (1.0 - relTolerance);
					if (!clearanceDynamicTINFactoryTerm)
					{
						clearanceDynamicTINFactoryTerm = dynamicTINFactory.Clearance(seg, ref tri) < seg.Length * clearanceFactor;
					}
					if (clearanceDynamicTINFactoryTerm)
					{
						halfEdges.Clear();
						DeleteFaceCascade(pop, halfEdges);
						foreach (HalfEdge halfEdge in halfEdges)
						{
							faceIterator.Push(halfEdge.fIdx);
						}
					}
				}
			}
			scaledCount--;
		}
		if (scaledCount < 0)
		{
			throw new Exception("Tripped infinite breaker while clearing Cartesian cells");
		}
		DeleteNakedNodes();
	}

	public void ClipByClearance(ConceptualMesh cmesh, int regionIdx, double clearanceFactor = 0.5, bool includeConstrained = true, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		cmesh.TriangulateRegion(out var dynTin, regionIdx, (int fIdx) => cmesh.FaceRawLine[fIdx], 0.2, reporter);
		List<int> intList = new List<int>();
		foreach (int faceIterator in FaceIterator())
		{
			if ((FaceCells[faceIterator].A < 0 || FaceCells[faceIterator].B < 0) && (includeConstrained || FaceConstraints[faceIterator] < (ConstraintState)0))
			{
				intList.Add(faceIterator);
			}
		}
		int tri = -1;
		List<HalfEdge> halfEdges = new List<HalfEdge>();
		while (intList.Count > 0)
		{
			if (reporter.CancelRequested)
			{
				return;
			}
			int ordinal = intList.Last();
			intList.RemoveAt(intList.Count - 1);
			if (FaceDeleted[ordinal] || (!includeConstrained && FaceConstraints[ordinal] >= (ConstraintState)0))
			{
				continue;
			}
			if (FaceCells[ordinal].A >= 0 && FaceCells[ordinal].B >= 0)
			{
				throw new Exception("Clearance processing reached internal edge");
			}
			if (FaceCells[ordinal].A < 0 && FaceCells[ordinal].B < 0)
			{
				DeleteFace(ordinal);
				continue;
			}
			Segment seg = FaceSegment(ordinal);
			if (!(dynTin.Clearance(seg, ref tri) < seg.Length * clearanceFactor))
			{
				continue;
			}
			DeleteFaceCascade(ordinal, halfEdges);
			foreach (HalfEdge halfEdge in halfEdges)
			{
				intList.Add(halfEdge.fIdx);
			}
			halfEdges.Clear();
		}
		DeleteNakedNodes();
	}

	private double SegmentPolygonDistance(Segment seg, Polygon poly, bool invert = false)
	{
		int invertTerm = ((!invert) ? 1 : (-1));
		return Math.Min((double)invertTerm * poly.SignedPointToPolygonDistance(seg.A), (double)invertTerm * poly.SignedPointToPolygonDistance(seg.B));
	}

	public void AddNodeArcsWithConstraints(ConceptualMesh cmesh, int regionIdx, bool includeExterior = true, bool includeBreaklines = true, HashSet<HalfEdge> origPerimeter = null, bool setTargetLength = false)
	{
		Dictionary<int, int> nodeMap = new Dictionary<int, int>();
		if (includeExterior)
		{
			AddRegionNodeArcsWithConstraints(cmesh, regionIdx, regionIdx, nodeMap, origPerimeter, setTargetLength);
		}
		foreach (int cellIslandCellIndex in cmesh.CellIslandCells(regionIdx))
		{
			AddRegionNodeArcsWithConstraints(cmesh, regionIdx, cellIslandCellIndex, nodeMap, origPerimeter, setTargetLength);
		}
		if (!includeBreaklines)
		{
			return;
		}
		foreach (int cellBreakline in cmesh.CellBreaklines(regionIdx))
		{
			AddArcWithConstraint(cmesh, new HalfEdge(cellBreakline, 0), nodeMap, VirtualConstraints.ConstraintMode.NULL, null, setTargetLength);
		}
	}

	public void AddRegionNodeArcsWithConstraints(ConceptualMesh cmesh, int exteriorRegion, int regionToAdd, Dictionary<int, int> nodeMap, HashSet<HalfEdge> origPerimeter, bool setTargetLength = false)
	{
		List<HalfEdge> cellFace = cmesh.CellFaces[regionToAdd];
		for (int i = 0; i < cellFace.Count; i++)
		{
			HalfEdge halfEdge = cellFace[i];
			HalfEdge he = halfEdge;
			if (regionToAdd == exteriorRegion)
			{
				he = halfEdge.Flipped();
			}
			else if (cmesh.HalfEdgeToOppositeCell(halfEdge) != exteriorRegion)
			{
				continue;
			}
			AddArcWithConstraint(cmesh, he, nodeMap, (cmesh.HalfEdgeToOppositeCell(halfEdge) == -1) ? VirtualConstraints.ConstraintMode.SLIDE_ADD_REMOVE : VirtualConstraints.ConstraintMode.FIXED, origPerimeter, setTargetLength);
		}
	}

	public void AddArcWithConstraint(ConceptualMesh cmesh, HalfEdge he, Dictionary<int, int> nodeMap, VirtualConstraints.ConstraintMode constraintMode = VirtualConstraints.ConstraintMode.NULL, ICollection<HalfEdge> origPerimeter = null, bool setTargetLength = false)
	{
		int fIdx = he.fIdx;
		if (constraintMode == VirtualConstraints.ConstraintMode.NULL)
		{
			constraintMode = cmesh.ArcRigidity(he.fIdx);
		}
		Polyline polyline = cmesh.FaceNodeString[fIdx];
		Polyline polyline2 = cmesh.FaceSmoothLine[fIdx];
		DiscretizeLine discretizeLine = cmesh.FaceDiscretization[fIdx];
		if (discretizeLine == null)
		{
			setTargetLength = false;
		}
		int constraintsPolyline2Term = constraints.Add(polyline2, fIdx, constraintMode);
		List<int> intList = new List<int>();
		int key = cmesh.HalfEdgeNodeBack(he);
		int unity = -1;
		if (!nodeMap.TryGetValue(key, out unity))
		{
			unity = (nodeMap[key] = FindOrAddFixedNode((he.orient == 0) ? polyline.First() : polyline.Last()));
		}
		intList.Add(unity);
		NodeConstraints[unity] = ConstraintState.FIXED;
		int addFace;
		for (int i = 1; i < polyline.Count - 1; i++)
		{
			int index = ((he.orient == 0) ? i : (polyline.Count - 1 - i));
			unity = AddNode(polyline[index]);
			intList.Add(unity);
			NodeConstraints[unity] = (ConstraintState)constraintsPolyline2Term;
			NodePointOnConstraint[unity] = polyline2.NearestPointOnPolyline(NodePoints[unity]);
			addFace = AddFace(intList[i - 1], intList[i]);
			origPerimeter?.Add(new HalfEdge(addFace, 0));
			FaceConstraints[addFace] = (ConstraintState)constraintsPolyline2Term;
			if (setTargetLength)
			{
				FaceTargetLength[addFace] = (float)discretizeLine.GetSegmentScale(polyline2, NodePointOnConstraint[unity], out var _, -1.0, following: true);
			}
		}
		key = cmesh.HalfEdgeNodeForward(he);
		if (!nodeMap.TryGetValue(key, out unity))
		{
			unity = (nodeMap[key] = FindOrAddFixedNode((he.orient == 0) ? polyline.Last() : polyline.First()));
		}
		intList.Add(unity);
		NodeConstraints[unity] = ConstraintState.FIXED;
		addFace = AddFace(intList[intList.Count - 2], intList[intList.Count - 1]);
		origPerimeter?.Add(new HalfEdge(addFace, 0));
		FaceConstraints[addFace] = (ConstraintState)constraintsPolyline2Term;
	}

	public HashSet<HalfEdge> ExternalHalfEdges()
	{
		HashSet<HalfEdge> halfEdges = new HashSet<HalfEdge>();
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator].A < 0)
			{
				halfEdges.Add(new HalfEdge(faceIterator, 0));
			}
			if (FaceCells[faceIterator].B < 0)
			{
				halfEdges.Add(new HalfEdge(faceIterator, 1));
			}
		}
		return halfEdges;
	}

	public int FindOrAddFixedNode(Point pnt)
	{
		int orAddFixedNode = -1;
		foreach (int nodeIterator in NodeIterator())
		{
			if (NodeConstraints[nodeIterator] == ConstraintState.FIXED && NodePoints[nodeIterator] == pnt)
			{
				orAddFixedNode = nodeIterator;
				break;
			}
		}
		if (orAddFixedNode < 0)
		{
			orAddFixedNode = AddNode(pnt);
		}
		return orAddFixedNode;
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

	public ConceptualMesh UnmeshedBoundaryGaps(HashSet<HalfEdge> origPerimeter = null)
	{
		Dictionary<int, int> nodeMap = new Dictionary<int, int>();
		ConceptualMesh subMesh = new ConceptualMesh();
		List<int> regionsToPave = new List<int>();
		foreach (CycleTree child in CycleTree.CreateCycleTree(this).children)
		{
			ProcessCW(child, -1);
		}
		foreach (int regionsToPave2 in regionsToPave)
		{
			MeshRegionMetadata cellRegionMetadata = subMesh.CellRegionMetadata[regionsToPave2];
			cellRegionMetadata.PatchType = PatchType.TrianglePaver;
			cellRegionMetadata.StretchRate = 0f;
			subMesh.CellRegionMetadata[regionsToPave2] = cellRegionMetadata;
		}
		subMesh.AddCellsFromFaces();
		return subMesh;
		void AddCycle(ConceptualMesh conceptualMesh, CycleTree cycle, int cellExterior, int cellInterior, bool withLinearFeatures)
		{
			List<int> intList = new List<int>();
			foreach (int node in cycle.nodes)
			{
				if (!nodeMap.TryGetValue(node, out var nodeMap2))
				{
					nodeMap2 = conceptualMesh.AddNode(NodePoints[node]);
					nodeMap[node] = nodeMap2;
				}
				intList.Add(nodeMap2);
			}
			if (cycle.cwArea > 0.0)
			{
				intList.Reverse();
			}
			for (int i = 0; i < intList.Count; i++)
			{
				int ordinal = intList[i];
				int ordinal2 = intList[(i + 1) % intList.Count];
				HalfEdge he = conceptualMesh.NodesToHalfEdge(ordinal, ordinal2);
				if (he.IsNull())
				{
					he = new HalfEdge(conceptualMesh.AddFace(ordinal, ordinal2), 0);
				}
				if (cellInterior >= 0)
				{
					conceptualMesh.CellAddFace(cellInterior, he);
				}
				if (cellExterior >= 0)
				{
					conceptualMesh.SetFaceCell(he.Flipped(), cellExterior);
				}
			}
			if (withLinearFeatures)
			{
				AddLinearFeaturesToCellInterior(conceptualMesh, cellInterior, cycle.linearFeatures);
			}
		}
		void AddLinearFeaturesToCellInterior(ConceptualMesh conceptualMesh, int subCell, List<CycleTree.LinearFeature> features)
		{
			List<int> intListList = new List<int>();
			foreach (CycleTree.LinearFeature feature in features)
			{
				int nodeMap3Term = -1;
				foreach (int node2 in feature.nodes)
				{
					if (!nodeMap.TryGetValue(node2, out var nodeMap3))
					{
						nodeMap3 = conceptualMesh.AddNode(NodePoints[node2]);
						nodeMap[node2] = nodeMap3;
					}
					if (nodeMap3Term >= 0)
					{
						intListList.Add(conceptualMesh.AddFace(nodeMap3Term, nodeMap3, subCell, subCell));
					}
					nodeMap3Term = nodeMap3;
				}
			}
		}
		bool CheckCycle(CycleTree cycle)
		{
			if (origPerimeter == null)
			{
				return true;
			}
			HalfEdge nodesToHalfEdge = NodesToHalfEdge(cycle.nodes[0], cycle.nodes[1]);
			return !origPerimeter.Contains(nodesToHalfEdge);
		}
		void ProcessCCW(CycleTree ccwCycle)
		{
			if (CheckCycle(ccwCycle))
			{
				int addCell = subMesh.AddCell();
				AddCycle(subMesh, ccwCycle, -99, addCell, withLinearFeatures: true);
				foreach (CycleTree child2 in ccwCycle.children)
				{
					ProcessCW(child2, addCell);
				}
				regionsToPave.Add(addCell);
			}
		}
		void ProcessCW(CycleTree cwCycle, int parentCellIdx)
		{
			if (parentCellIdx >= 0)
			{
				AddCycle(subMesh, cwCycle, parentCellIdx, -99, withLinearFeatures: false);
			}
			foreach (CycleTree child3 in cwCycle.children)
			{
				ProcessCCW(child3);
			}
		}
	}

	public void ClearConstraints(bool check = true)
	{
		foreach (int nodeIterator in NodeIterator())
		{
			if (!check)
			{
				NodeConstraints[nodeIterator] = ConstraintState.FREE;
			}
		}
		foreach (int faceIterator in FaceIterator())
		{
			if (!check)
			{
				FaceConstraints[faceIterator] = ConstraintState.FREE;
			}
		}
		constraints.Clear();
	}

	public void FillGaps(ProgressReporter reporter = null, List<int> newFaces = null, HashSet<HalfEdge> origPerimeter = null, bool useScale = true)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		ConceptualMesh conceptualMesh = UnmeshedBoundaryGaps(origPerimeter);
		if (conceptualMesh == null)
		{
			return;
		}
		List<SlidingMesh> slidingMeshes = new List<SlidingMesh>();
		int scaledCount = (int)((double)conceptualMesh.CellDeleted.Count / 100.0);
		if (scaledCount == 0)
		{
			scaledCount = 1;
		}
		int ordinal = 0;
		foreach (int cellIterator in conceptualMesh.CellIterator())
		{
			if (ordinal % scaledCount == 0)
			{
				reporter.ReportProgressFraction((float)ordinal / (float)conceptualMesh.CellDeleted.Count);
			}
			ordinal++;
			if (reporter.CancelRequested)
			{
				break;
			}
			if (conceptualMesh.CellRegionMetadata[cellIterator].PatchType != PatchType.TrianglePaver)
			{
				continue;
			}
			TrianglePaver trianglePaver = new TrianglePaver(conceptualMesh, cellIterator)
			{
				stretchRate = 0f
			};
			ISizeFunction regionSizeFunction = trianglePaver.GetRegionSizeFunction(secondary: false);
			if (useScale)
			{
				trianglePaver.sizeFunction = regionSizeFunction;
			}
			else
			{
				trianglePaver.sizeFunction = new ConstantSizeFunction(10000000000.0);
				trianglePaver.deltaAccept = -1.0;
			}
			SlidingMesh slidingMesh = trianglePaver.Generate();
			if (!useScale)
			{
				foreach (int faceIterator in slidingMesh.FaceIterator())
				{
					slidingMesh.FaceTargetLength[faceIterator] = (float)regionSizeFunction.Query(slidingMesh.FaceMidPoint(faceIterator));
				}
			}
			new MeshSmoother(slidingMesh, slidingMesh.InternalNodes()).Optimize(3);
			slidingMeshes.Add(slidingMesh);
			slidingMesh.ClearConstraints(check: false);
			if (newFaces == null)
			{
				MergeMesh(slidingMesh);
				continue;
			}
			Dictionary<int, int> arcToConstraint = null;
			int faceCount = FaceCount;
			MergeMesh(slidingMesh, out var _, out var faceMap, out var _, ref arcToConstraint);
			HalfEdge[] faceMapArray = faceMap;
			for (int i = 0; i < faceMapArray.Length; i++)
			{
				HalfEdge halfEdge = faceMapArray[i];
				if (halfEdge.fIdx >= faceCount)
				{
					newFaces.Add(halfEdge.fIdx);
				}
			}
		}
	}
}

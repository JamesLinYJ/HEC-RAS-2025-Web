using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.DataStructures;
using Geospatial.Transformers;
using Utility.Extensions;
using Utility.Logging;
using Utility.Progress;
using Utility.Unsafe;

namespace Geospatial.Vectors.MeshTopology;

[DebuggerDisplay("{CellCount} Cells")]
[JsonConverter(typeof(DynMeshJsonConverter))]
public class DynamicMesh
{
	public class DynMeshJsonConverter : JsonConverter<DynamicMesh>
	{
		public override DynamicMesh Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			throw new JsonException();
		}

		public override void Write(Utf8JsonWriter writer, DynamicMesh obj, JsonSerializerOptions options)
		{
			string json = JsonSerializer.Serialize((object)obj.SimpleRepr(), options);
			writer.WriteRawValue(json);
		}
	}

	public static class MessageCodes
	{
		public static readonly MessageClass NoncontiguousMeshError = new MessageClass("DynamicMesh_Noncontiguous", MessageSeverity.Error, "Mesh is not contiguous, may cause errors during simulation");

		public static Message GetNoncontiguousMeshError()
		{
			return NoncontiguousMeshError.Create();
		}
	}

	public const int BOUNDARY = -1;

	public const int CLIPPED = -2;

	public const int MISSING = -99;

	public static bool EnhancedMeshConsistencyChecks;

	public List<Duple<int>> FaceNodes;

	public List<Duple<int>> FaceCells;

	public List<bool> FaceDeleted;

	[JsonInclude]
	public List<List<HalfEdge>> CellFaces;

	public List<bool> CellDeleted;

	public List<Point> NodePoints;

	public List<bool> NodeDeleted;

	private Dictionary<int, List<int>> _nodeToEdges;

	public int CellCount => CellFaces.Count;

	public int FaceCount => FaceNodes.Count;

	public int NodeCount => NodePoints.Count;

	static DynamicMesh()
	{
	}

	public static DynamicMesh ReadJson(string filename)
	{
		using FileStream utf8Json = File.OpenRead(filename);
		return FromSimpleRepr(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(utf8Json, JsonOptions()));
	}

	public static JsonSerializerOptions JsonOptions()
	{
		return new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
	}

	public int CellAddFace(int cIdx, int fIdx, byte orient)
	{
		return CellAddFace(cIdx, new HalfEdge(fIdx, orient));
	}

	public int CellAddFace(int cIdx, HalfEdge he)
	{
		if (FaceCells[he.fIdx][he.orient] != cIdx)
		{
			SetFaceCell(he, cIdx);
		}
		CellFaces[cIdx].Add(he);
		return CellFaces[cIdx].Count - 1;
	}

	public void CellFacesReplace(int cIdx, int fcStart, int fcCount, HalfEdge he)
	{
		for (int i = fcStart; i < fcStart + fcCount; i++)
		{
			int cellFaceCountSum = i;
			if (cellFaceCountSum >= CellFaces[cIdx].Count)
			{
				cellFaceCountSum -= CellFaces[cIdx].Count;
			}
			SetFaceCell(CellFaces[cIdx][cellFaceCountSum], -99);
			if (i == fcStart)
			{
				CellFaces[cIdx][cellFaceCountSum] = he;
				SetFaceCell(he, cIdx);
			}
			else
			{
				CellFaces[cIdx][cellFaceCountSum] = HalfEdge.NullHalfEdge;
			}
		}
		if (fcCount <= 1)
		{
			return;
		}
		int ordinal = 0;
		for (int j = 0; j < CellFaces[cIdx].Count; j++)
		{
			if (!CellFaces[cIdx][j].IsNull())
			{
				if (ordinal < j)
				{
					CellFaces[cIdx][ordinal] = CellFaces[cIdx][j];
				}
				ordinal++;
			}
		}
		CellFaces[cIdx].RemoveRange(ordinal);
	}

	public void CellFaceReplace(int cIdx, int cfIdx, List<HalfEdge> newFaces)
	{
		SetFaceCell(CellFaces[cIdx][cfIdx], -99);
		CellFaces[cIdx][cfIdx] = newFaces[0];
		if (newFaces.Count > 1)
		{
			CellFaces[cIdx].InsertRange(cfIdx + 1, newFaces.GetRange(1, newFaces.Count - 1));
		}
		foreach (HalfEdge newFace in newFaces)
		{
			SetFaceCell(newFace, cIdx);
		}
	}

	public void CellFaceReplace(int cIdx, HalfEdge old, List<HalfEdge> newFaces)
	{
		int indexOf = CellFaces[cIdx].IndexOf(old);
		if (indexOf < 0)
		{
			throw new IndexOutOfRangeException();
		}
		CellFaceReplace(cIdx, indexOf, newFaces);
	}

	public bool CellRemoveFace(int cIdx, int fIdx)
	{
		List<HalfEdge> cellFace = CellFaces[cIdx];
		for (int i = 0; i < cellFace.Count; i++)
		{
			if (cellFace[i].fIdx == fIdx)
			{
				cellFace.RemoveAt(i);
				if (FaceCells[fIdx].A == cIdx)
				{
					SetFaceCell(fIdx, 0, -99);
				}
				else if (FaceCells[fIdx].B == cIdx)
				{
					SetFaceCell(fIdx, 1, -99);
				}
				return true;
			}
		}
		return false;
	}

	public void SetFaceCell(int fIdx, byte orient, int cIdx, bool check = true)
	{
		Duple<int> faceCell = FaceCells[fIdx];
		if (check && faceCell[orient] >= 0 && cIdx >= 0)
		{
			throw new StructuredException(Message.GenericError("Trying to set FaceCell that is already set, near ({Coordinate}).", FaceMidPoint(fIdx)));
		}
		faceCell[orient] = cIdx;
		FaceCells[fIdx] = faceCell;
	}

	public void SetFaceCell(HalfEdge he, int cIdx, bool check = true)
	{
		SetFaceCell(he.fIdx, he.orient, cIdx, check);
	}

	public void SetFaceNode(int fIdx, byte orient, int nIdx, bool updateAdjacency = true)
	{
		Duple<int> faceNode = FaceNodes[fIdx];
		int faceNode2 = faceNode[orient];
		faceNode[orient] = nIdx;
		FaceNodes[fIdx] = faceNode;
		if (!updateAdjacency || _nodeToEdges == null)
		{
			return;
		}
		if (faceNode2 >= 0)
		{
			_nodeToEdges[faceNode2].Remove(fIdx);
		}
		if (nIdx >= 0)
		{
			if (!_nodeToEdges.ContainsKey(nIdx))
			{
				_nodeToEdges[nIdx] = new List<int> { fIdx };
			}
			else
			{
				_nodeToEdges[nIdx].Add(fIdx);
			}
		}
	}

	public int ValidCellCount()
	{
		int ordinal = 0;
		foreach (int cellIterator in CellIterator())
		{
			_ = cellIterator;
			ordinal++;
		}
		return ordinal;
	}

	public int ValidNodeCount()
	{
		int ordinal = 0;
		foreach (int nodeIterator in NodeIterator())
		{
			_ = nodeIterator;
			ordinal++;
		}
		return ordinal;
	}

	public DynamicMesh()
	{
		FaceNodes = new List<Duple<int>>();
		FaceDeleted = new List<bool>();
		FaceCells = new List<Duple<int>>();
		NodePoints = new List<Point>();
		NodeDeleted = new List<bool>();
		CellFaces = new List<List<HalfEdge>>();
		CellDeleted = new List<bool>();
		_nodeToEdges = null;
	}

	public DynamicMesh(DynamicMesh other)
	{
		FaceNodes = other.FaceNodes.ToList();
		FaceDeleted = other.FaceDeleted.ToList();
		FaceCells = other.FaceCells.ToList();
		NodePoints = other.NodePoints.ToList();
		NodeDeleted = other.NodeDeleted.ToList();
		CellFaces = other.CellFaces.ConvertAll((List<HalfEdge> cf) => cf.ToList());
		CellDeleted = other.CellDeleted.ToList();
	}

	public DynamicMesh Clone()
	{
		return new DynamicMesh(this);
	}

	public virtual int AddFace(int nodeA, int nodeB, int cellA = -99, int cellB = -99)
	{
		FaceNodes.Add(new Duple<int>(nodeA, nodeB));
		FaceDeleted.Add(item: false);
		FaceCells.Add(new Duple<int>(cellA, cellB));
		int countOffset = FaceNodes.Count - 1;
		if (_nodeToEdges != null)
		{
			for (int i = 0; i < 2; i++)
			{
				int key = FaceNodes[countOffset][i];
				if (!_nodeToEdges.ContainsKey(key))
				{
					_nodeToEdges[key] = new List<int> { countOffset };
					continue;
				}
				_nodeToEdges[key].Add(countOffset);
				if (nodeA == nodeB)
				{
					break;
				}
			}
		}
		return countOffset;
	}

	public virtual void DeleteFace(int fIdx, bool checkCells = true)
	{
		FaceDeleted[fIdx] = true;
		if (_nodeToEdges == null)
		{
			return;
		}
		foreach (int faceNode in FaceNodes[fIdx])
		{
			_nodeToEdges[faceNode].Remove(fIdx);
			if (FaceNodes[fIdx][0] == FaceNodes[fIdx][1])
			{
				break;
			}
		}
	}

	public void DeleteFaceAndNakedNodes(int fIdx, bool checkCells = true)
	{
		Duple<int> duple = FaceNodes[fIdx];
		DeleteFace(fIdx, checkCells);
		foreach (int duple2 in duple)
		{
			if (NodeDegree(duple2) == 0)
			{
				DeleteNode(duple2);
			}
		}
	}

	public virtual void UndeleteFace(int fIdx)
	{
		FaceDeleted[fIdx] = false;
		if (_nodeToEdges == null)
		{
			return;
		}
		for (int i = 0; i < 2; i++)
		{
			int key = FaceNodes[fIdx][i];
			if (!_nodeToEdges.ContainsKey(key))
			{
				_nodeToEdges[key] = new List<int> { fIdx };
				continue;
			}
			_nodeToEdges[key].Add(fIdx);
			if (FaceNodes[fIdx].A == FaceNodes[fIdx].B)
			{
				break;
			}
		}
	}

	public virtual void DeleteNakedFaces()
	{
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator][0] < 0 && FaceCells[faceIterator][1] < 0)
			{
				DeleteFace(faceIterator);
			}
		}
	}

	public virtual void DeleteNakedFaces(HashSet<int> skipFaces)
	{
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator][0] < 0 && FaceCells[faceIterator][1] < 0 && !skipFaces.Contains(faceIterator))
			{
				DeleteFace(faceIterator);
			}
		}
	}

	public virtual int DeleteNakedNodes()
	{
		return DeleteNakedNodes(NodeIterator());
	}

	public virtual int DeleteNakedNodes(IEnumerable<int> nodes)
	{
		int ordinal = 0;
		foreach (int node in nodes)
		{
			if (NodeToEdges(node).Count == 0)
			{
				ordinal++;
				DeleteNode(node);
			}
		}
		return ordinal;
	}

	public virtual int AddCell()
	{
		CellDeleted.Add(item: false);
		CellFaces.Add(new List<HalfEdge>());
		return CellFaces.Count - 1;
	}

	public virtual int AddCell(List<HalfEdge> faces)
	{
		int addCell = AddCell();
		foreach (HalfEdge face in faces)
		{
			CellAddFace(addCell, face.fIdx, face.orient);
		}
		return addCell;
	}

	public virtual void DeleteCell(int cIdx)
	{
		CellDeleted[cIdx] = true;
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			SetFaceCell(cellFace.fIdx, cellFace.orient, -99, check: false);
		}
	}

	public void DeleteCell(int cIdx, ICollection<HalfEdge> exposedFaces)
	{
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			exposedFaces.Add(cellFace);
		}
		DeleteCell(cIdx);
	}

	public virtual int AddNode(double x, double y)
	{
		return AddNode(new Point(x, y));
	}

	public virtual int AddNode(Point point)
	{
		NodePoints.Add(point);
		NodeDeleted.Add(item: false);
		return NodePoints.Count - 1;
	}

	public virtual void DeleteNode(int nIdx)
	{
		NodeDeleted[nIdx] = true;
	}

	public virtual void UndeleteNode(int nIdx)
	{
		NodeDeleted[nIdx] = false;
	}

	public void DeleteNodeCascade(int nIdx)
	{
		foreach (int nodeToEdge in new List<int>(NodeToEdges(nIdx)))
		{
			DeleteFaceCascade(nodeToEdge);
		}
		DeleteNode(nIdx);
	}

	public void DeleteNodeCascade(int nIdx, out List<int> deletedFaces)
	{
		deletedFaces = new List<int>();
		foreach (int nodeToEdge in new List<int>(NodeToEdges(nIdx)))
		{
			deletedFaces.Add(nodeToEdge);
			DeleteFaceCascade(nodeToEdge);
		}
		DeleteNode(nIdx);
	}

	public void DeleteFaceCascade(int fIdx)
	{
		foreach (int faceCell in FaceCells[fIdx])
		{
			if (faceCell >= 0)
			{
				DeleteCell(faceCell);
			}
		}
		DeleteFace(fIdx);
	}

	public void DeleteFaceCascade(int fIdx, ICollection<HalfEdge> exposedFaces)
	{
		foreach (int faceCell in FaceCells[fIdx])
		{
			if (faceCell >= 0)
			{
				DeleteCell(faceCell, exposedFaces);
			}
		}
		DeleteFace(fIdx);
	}

	public void DeleteFaceAndNakedNodesCascade(int fIdx, ICollection<HalfEdge> exposedFaces)
	{
		Duple<int> duple = FaceNodes[fIdx];
		DeleteFaceCascade(fIdx, exposedFaces);
		foreach (int duple2 in duple)
		{
			if (NodeDegree(duple2) == 0)
			{
				DeleteNode(duple2);
			}
		}
	}

	public IReadOnlyList<int> NodeToEdges(int nIdx)
	{
		if (_nodeToEdges == null)
		{
			BuildNodeToEdges();
		}
		if (_nodeToEdges.ContainsKey(nIdx))
		{
			return _nodeToEdges[nIdx];
		}
		return new List<int>();
	}

	public HalfEdge NodesToHalfEdge(int n1Idx, int n2Idx)
	{
		foreach (int nodeToEdge in NodeToEdges(n1Idx))
		{
			if (FaceNodes[nodeToEdge].B == n2Idx)
			{
				if (EnhancedMeshConsistencyChecks && FaceNodes[nodeToEdge].A != n1Idx)
				{
					throw new StructuredException(Message.GenericError("Mesh generation error detected (Face/Node disagreement) near {Coordinate}.", FaceMidPoint(nodeToEdge)));
				}
				return new HalfEdge(nodeToEdge, 0);
			}
			if (FaceNodes[nodeToEdge].A == n2Idx)
			{
				if (EnhancedMeshConsistencyChecks && FaceNodes[nodeToEdge].B != n1Idx)
				{
					throw new StructuredException(Message.GenericError("Mesh generation error detected (Face/Node disagreement) near {Coordinate}.", FaceMidPoint(nodeToEdge)));
				}
				return new HalfEdge(nodeToEdge, 1);
			}
		}
		return HalfEdge.NullHalfEdge;
	}

	public HalfEdge FindOrAddHalfEdge(int n1, int n2)
	{
		HalfEdge result = NodesToHalfEdge(n1, n2);
		if (result.IsNull())
		{
			result = new HalfEdge(AddFace(n1, n2), 0);
		}
		return result;
	}

	public IEnumerable<int> NodeToCells(int nIdx)
	{
		foreach (HalfEdge nodeToHalfEdge in NodeToHalfEdges(nIdx))
		{
			int halfEdgeToCell = HalfEdgeToCell(nodeToHalfEdge);
			if (halfEdgeToCell >= 0)
			{
				yield return halfEdgeToCell;
			}
		}
	}

	private void BuildNodeToEdges()
	{
		_nodeToEdges = new Dictionary<int, List<int>>();
		for (int i = 0; i < FaceCount; i++)
		{
			if (FaceDeleted[i])
			{
				continue;
			}
			foreach (int faceNode in FaceNodes[i])
			{
				if (!_nodeToEdges.ContainsKey(faceNode))
				{
					_nodeToEdges[faceNode] = new List<int>();
				}
				_nodeToEdges[faceNode].Add(i);
				if (FaceNodes[i][0] == FaceNodes[i][1])
				{
					break;
				}
			}
		}
	}

	public HalfEdge CellFindFace(int cIdx, int fIdx)
	{
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			if (cellFace.fIdx == fIdx)
			{
				return cellFace;
			}
		}
		throw new IndexOutOfRangeException();
	}

	public Polygon CellSimplePolygon(int cIdx)
	{
		List<Point> points = new List<Point>();
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			points.Add(NodePoints[HalfEdgeNodeBack(cellFace)]);
		}
		points.Reverse();
		return new Polygon(points);
	}

	public Polygon CellGenericPolygon(int cIdx, Func<int, Polyline> getFaceGeometry)
	{
		List<Point> points = new List<Point>();
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			Polyline polyline = getFaceGeometry(cellFace.fIdx);
			if (points.Count == 0)
			{
				if (cellFace.orient == 0)
				{
					points.AddRange(polyline.Points);
				}
				else
				{
					points.AddRangeReverse(polyline.Points);
				}
			}
			else if (cellFace.orient == 0)
			{
				points.RemoveAt(points.Count - 1);
				points.AddRange(polyline.Points);
			}
			else
			{
				points.RemoveAt(points.Count - 1);
				points.AddRangeReverse(polyline.Points);
			}
		}
		points.RemoveAt(points.Count - 1);
		points.Reverse();
		return new Polygon(points);
	}

	public double CellSimpleArea(int cIdx)
	{
		List<HalfEdge> cellFace = CellFaces[cIdx];
		int count = cellFace.Count;
		(DynamicMesh, List<HalfEdge>) model = (this, cellFace);
		return -1.0 * Polygon.ComputeArea(model, count, ((DynamicMesh, List<HalfEdge>) mdl, int idx) => mdl.Item1.NodePoints[mdl.Item1.HalfEdgeNodeBack(mdl.Item2[idx])]);
	}

	public bool CheckMesh()
	{
		List<DynamicMeshError> errors;
		return CheckMesh(out errors);
	}

	public virtual bool CheckMesh(out List<DynamicMeshError> errors)
	{
		errors = new List<DynamicMeshError>();
		for (int i = 0; i < NodeCount; i++)
		{
			if (NodeDeleted[i])
			{
				continue;
			}
			Point pt = NodePoints[i];
			if (Point.IsNull(pt))
			{
				errors.Add(new DynamicMeshError(DynamicMeshErrorType.NULL_POINT, i));
			}
			else if (!double.IsFinite(pt.X) || !double.IsFinite(pt.Y))
			{
				errors.Add(new DynamicMeshError(DynamicMeshErrorType.NONFINITE_POINT, i));
			}
			else
			{
				if (_nodeToEdges == null || !_nodeToEdges.TryGetValue(i, out var localValue))
				{
					continue;
				}
				foreach (int ordinal in localValue)
				{
					if (!FaceNodes[ordinal].Contains(i))
					{
						errors.Add(new DynamicMeshError(DynamicMeshErrorType.FACE_NODE_MISMATCH, i));
					}
				}
				if (localValue.Count != localValue.Distinct().Count())
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.NODE_FACE_MISMATCH, i));
				}
			}
		}
		for (int j = 0; j < FaceCount; j++)
		{
			if (FaceDeleted[j])
			{
				continue;
			}
			Duple<int> duple = FaceNodes[j];
			if (duple.A < 0 || duple.B < 0 || duple.A == duple.B)
			{
				errors.Add(new DynamicMeshError(DynamicMeshErrorType.FACE_INVALID_NODE, j));
				continue;
			}
			Duple<int> duple2 = FaceCells[j];
			for (int k = 0; k < 2; k++)
			{
				int duple22 = duple2[k];
				if (duple22 < 0)
				{
					continue;
				}
				if (CellDeleted[duple22])
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.FACE_INVALID_CELL, j));
					continue;
				}
				HalfEdge halfEdge;
				try
				{
					halfEdge = CellFindFace(duple22, j);
				}
				catch (IndexOutOfRangeException)
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.FACE_CELL_MISMATCH, j)
					{
						orient = (sbyte)k
					});
					continue;
				}
				if (halfEdge.orient != k)
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.HALFEDGE_MISMATCH, j));
				}
			}
			if (_nodeToEdges == null)
			{
				continue;
			}
			foreach (int duple3 in duple)
			{
				if (!_nodeToEdges.TryGetValue(duple3, out var localValue2))
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.NODE_FACE_MISMATCH, j));
				}
				else if (!localValue2.Contains(j))
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.NODE_FACE_MISMATCH, j));
				}
			}
		}
		for (int l = 0; l < CellCount; l++)
		{
			if (CellDeleted[l])
			{
				continue;
			}
			foreach (HalfEdge cellFace in CellFaces[l])
			{
				if (FaceDeleted[cellFace.fIdx])
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.CELL_INVALID_FACE, l));
				}
				else if (FaceCells[cellFace.fIdx][cellFace.orient] != l)
				{
					errors.Add(new DynamicMeshError(DynamicMeshErrorType.CELL_FACE_MISMATCH, l));
				}
			}
			if (CellSimpleArea(l) <= 0.0)
			{
				errors.Add(new DynamicMeshError(DynamicMeshErrorType.CELL_NONPOSITIVE_AREA, l));
			}
		}
		return errors.Count == 0;
	}

	public void SetCellNodes(int cIdx, List<int> nodes, ElementMode faceOption = ElementMode.CREATE)
	{
		for (int i = 0; i < nodes.Count; i++)
		{
			int node = nodes[i];
			int node2 = nodes[(i + 1) % nodes.Count];
			HalfEdge halfEdge = NodesToHalfEdge(node, node2);
			if (halfEdge.IsNull())
			{
				if (faceOption == ElementMode.EXISTING)
				{
					throw new Exception("Face was expected to exist, but wasn't found");
				}
				int fIdx = AddFace(node, node2);
				CellAddFace(cIdx, fIdx, 0);
			}
			else
			{
				if (faceOption == ElementMode.CREATE)
				{
					throw new Exception("Face existed but FaceOption set to CREATE");
				}
				CellAddFace(cIdx, halfEdge.fIdx, halfEdge.orient);
			}
		}
	}

	public List<int> CellToNodes(int cIdx, List<int> nodes = null)
	{
		if (nodes == null)
		{
			nodes = new List<int>();
		}
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			nodes.Add(FaceNodes[cellFace.fIdx][cellFace.orient]);
		}
		return nodes;
	}

	public int[,] AddCartesian(int NodesX, int NodesY)
	{
		return AddCartesian(NodesX, NodesY, new Point(0.0, 0.0), new Point(NodesX - 1, NodesY - 1));
	}

	public int[,] AddCartesian(int NodesX, int NodesY, Point ll, Point ur)
	{
		int[,] intValues = new int[2 * NodesX - 1, 2 * NodesY - 1];
		for (int i = 0; i < NodesX; i++)
		{
			double nodesXTerm = (double)i / (double)Math.Max(1, NodesX - 1);
			double x = ll.X * (1.0 - nodesXTerm) + ur.X * nodesXTerm;
			for (int j = 0; j < NodesY; j++)
			{
				double nodesYTerm = (double)j / (double)Math.Max(1, NodesY - 1);
				double y = ll.Y * (1.0 - nodesYTerm) + ur.Y * nodesYTerm;
				intValues[2 * i, 2 * j] = AddNode(new Point(x, y));
			}
		}
		for (int k = 0; k < 2 * NodesX - 1; k++)
		{
			for (int l = 0; l < 2 * NodesY - 1; l++)
			{
				if (k % 2 == 1 && l % 2 == 0)
				{
					intValues[k, l] = AddFace(intValues[k - 1, l], intValues[k + 1, l]);
				}
				else if (k % 2 == 0 && l % 2 == 1)
				{
					intValues[k, l] = AddFace(intValues[k, l - 1], intValues[k, l + 1]);
				}
			}
		}
		for (int m = 1; m < 2 * NodesX - 1; m += 2)
		{
			for (int n = 1; n < 2 * NodesY - 1; n += 2)
			{
				int cIdx = (intValues[m, n] = AddCell());
				CellAddFace(cIdx, intValues[m - 1, n], 1);
				CellAddFace(cIdx, intValues[m, n - 1], 0);
				CellAddFace(cIdx, intValues[m + 1, n], 0);
				CellAddFace(cIdx, intValues[m, n + 1], 1);
			}
		}
		return intValues;
	}

	public static List<int> IdsToBoundaryNodes(int[,] Ids, int sIdx)
	{
		return IdsToBoundaryItems(Ids, sIdx, getNodes: true);
	}

	public static List<int> IdsToBoundaryFaces(int[,] Ids, int sIdx)
	{
		return IdsToBoundaryItems(Ids, sIdx, getNodes: false);
	}

	public static List<int> IdsToBoundaryItems(int[,] Ids, int sIdx, bool getNodes)
	{
		int getNodesTerm = ((!getNodes) ? 1 : 0);
		List<int> intList = new List<int>();
		switch (sIdx)
		{
		case 0:
		{
			for (int j = getNodesTerm; j < Ids.GetLength(0); j += 2)
			{
				intList.Add(Ids[j, 0]);
			}
			break;
		}
		case 2:
		{
			int getLengthIdsTerm = Ids.GetLength(1) - 1;
			for (int k = getNodesTerm; k < Ids.GetLength(0); k += 2)
			{
				intList.Add(Ids[k, getLengthIdsTerm]);
			}
			intList.Reverse();
			break;
		}
		case 1:
		{
			int getLengthIdsTerm2 = Ids.GetLength(0) - 1;
			for (int l = getNodesTerm; l < Ids.GetLength(1); l += 2)
			{
				intList.Add(Ids[getLengthIdsTerm2, l]);
			}
			break;
		}
		case 3:
		{
			for (int i = getNodesTerm; i < Ids.GetLength(1); i += 2)
			{
				intList.Add(Ids[0, i]);
			}
			intList.Reverse();
			break;
		}
		}
		return intList;
	}

	public void WriteJson(string filename)
	{
		using FileStream utf8Json = File.Create(filename);
		JsonSerializer.Serialize(utf8Json, this, JsonOptions());
	}

	public static DynamicMesh FromSimpleRepr(Dictionary<string, JsonElement> simpleRepr)
	{
		DynamicMesh dynamicMesh = new DynamicMesh();
		dynamicMesh.UpdateFromJson(simpleRepr);
		return dynamicMesh;
	}

	public virtual void UpdateFromJson(Dictionary<string, JsonElement> simpleRepr)
	{
		NodesFromJson(simpleRepr["Nodes"]);
		FacesFromJson(simpleRepr["Faces"]);
		CellsFromJson(simpleRepr["Cells"]);
	}

	public void NodesFromJson(JsonElement repr)
	{
		foreach (JsonElement enumerateArray in repr.EnumerateArray())
		{
			Point point = new Point(enumerateArray.GetProperty("x").GetDouble(), enumerateArray.GetProperty("y").GetDouble());
			int addNode = AddNode(point);
			NodeDeleted[addNode] = enumerateArray.GetProperty("deleted").GetBoolean();
			UpdateNodeFromJson(addNode, enumerateArray);
		}
	}

	public virtual void UpdateNodeFromJson(int nIdx, JsonElement rec)
	{
	}

	public void FacesFromJson(JsonElement repr)
	{
		foreach (JsonElement enumerateArray in repr.EnumerateArray())
		{
			List<int> deserialize = enumerateArray.GetProperty("nodes").Deserialize<List<int>>();
			List<int> deserializeList = enumerateArray.GetProperty("cells").Deserialize<List<int>>();
			int addFace = AddFace(deserialize[0], deserialize[1], deserializeList[0], deserializeList[1]);
			FaceDeleted[addFace] = enumerateArray.GetProperty("deleted").GetBoolean();
			UpdateFaceFromJson(addFace, enumerateArray);
		}
	}

	public virtual void UpdateFaceFromJson(int fIdx, JsonElement rec)
	{
	}

	public int AddCellFromCoords(double[,] coords)
	{
		List<int> intList = new List<int>();
		for (int i = 0; i < coords.GetLength(0); i++)
		{
			intList.Add(AddNode(new Point(coords[i, 0], coords[i, 1])));
		}
		return AddCellFromNodes(intList, ElementMode.CREATE);
	}

	public int AddCellFromNodes(List<int> nodes, ElementMode faceMode)
	{
		int addCell = AddCell();
		SetCellNodes(addCell, nodes, faceMode);
		return addCell;
	}

	public int AddCellFromCoords(Polygon polygon, bool reverse = false)
	{
		List<int> intList = new List<int>();
		if (!reverse)
		{
			for (int countOffset = polygon.Points.Count - 1; countOffset >= 0; countOffset--)
			{
				intList.Add(AddNode(polygon.Points[countOffset]));
			}
		}
		else
		{
			for (int i = 0; i < polygon.Points.Count; i++)
			{
				intList.Add(AddNode(polygon.Points[i]));
			}
		}
		int addCell = AddCell();
		SetCellNodes(addCell, intList);
		return addCell;
	}

	public void CellsFromJson(JsonElement repr)
	{
		foreach (JsonElement enumerateArray in repr.EnumerateArray())
		{
			int addCell = AddCell();
			foreach (JsonElement enumerateArray2 in enumerateArray.GetProperty("faces").EnumerateArray())
			{
				CellAddFace(addCell, enumerateArray2.GetProperty("fIdx").GetInt32(), enumerateArray2.GetProperty("orient").GetByte());
			}
			CellDeleted[addCell] = enumerateArray.GetProperty("deleted").GetBoolean();
			UpdateCellFromJson(addCell, enumerateArray);
		}
	}

	public virtual void UpdateCellFromJson(int cIdx, JsonElement rec)
	{
	}

	public virtual Dictionary<string, object> SimpleRepr()
	{
		return new Dictionary<string, object>
		{
			["Cells"] = CellsSimpleRepr(),
			["Faces"] = FacesSimpleRepr(),
			["Nodes"] = NodesSimpleRepr()
		};
	}

	public virtual List<Dictionary<string, object>> CellsSimpleRepr()
	{
		List<Dictionary<string, object>> objectByStringList = new List<Dictionary<string, object>>();
		for (int i = 0; i < CellCount; i++)
		{
			objectByStringList.Add(CellSimpleRepr(i));
		}
		return objectByStringList;
	}

	public virtual List<Dictionary<string, object>> FacesSimpleRepr()
	{
		List<Dictionary<string, object>> objectByStringList = new List<Dictionary<string, object>>();
		for (int i = 0; i < FaceCount; i++)
		{
			objectByStringList.Add(FaceSimpleRepr(i));
		}
		return objectByStringList;
	}

	public virtual List<Dictionary<string, object>> NodesSimpleRepr()
	{
		List<Dictionary<string, object>> objectByStringList = new List<Dictionary<string, object>>();
		for (int i = 0; i < NodeCount; i++)
		{
			objectByStringList.Add(NodeSimpleRepr(i));
		}
		return objectByStringList;
	}

	public virtual Dictionary<string, object> CellSimpleRepr(int cIdx)
	{
		return new Dictionary<string, object>
		{
			{
				"deleted",
				CellDeleted[cIdx]
			},
			{
				"faces",
				CellFaces[cIdx]
			}
		};
	}

	public virtual Dictionary<string, object> FaceSimpleRepr(int fIdx)
	{
		return new Dictionary<string, object>
		{
			{
				"deleted",
				FaceDeleted[fIdx]
			},
			{
				"nodes",
				FaceNodes[fIdx]
			},
			{
				"cells",
				FaceCells[fIdx]
			}
		};
	}

	public virtual Dictionary<string, object> NodeSimpleRepr(int nIdx)
	{
		return new Dictionary<string, object>
		{
			{
				"deleted",
				NodeDeleted[nIdx]
			},
			{
				"x",
				NodePoints[nIdx].X
			},
			{
				"y",
				NodePoints[nIdx].Y
			}
		};
	}

	public static List<List<double>> SerializePolyline(Polyline s)
	{
		if (s == null)
		{
			return null;
		}
		List<List<double>> doubleListList = new List<List<double>>();
		for (int i = 0; i < s.Points.Count; i++)
		{
			doubleListList.Add(new List<double>
			{
				s.Points[i].X,
				s.Points[i].Y
			});
		}
		return doubleListList;
	}

	public static Polyline DeserializePolyline(JsonElement elt)
	{
		List<Point> points = new List<Point>();
		foreach (JsonElement enumerateArray in elt.EnumerateArray())
		{
			List<double> deserialize = enumerateArray.Deserialize<List<double>>();
			points.Add(new Point(deserialize[0], deserialize[1]));
		}
		return new Polyline(points);
	}

	public void Transform(ISpatialTransformer mapping, bool reverse = false)
	{
		TransformSelected(mapping, ValidNodes(returnNullOnFullList: true), reverse);
	}

	public void TransformSelected(ISpatialTransformer mapping, int[,] Ids, bool reverse = false)
	{
		List<int> intList = new List<int>();
		for (int i = 0; i < Ids.GetLength(0); i += 2)
		{
			for (int j = 0; j < Ids.GetLength(1); j += 2)
			{
				intList.Add(Ids[i, j]);
			}
		}
		TransformSelected(mapping, intList, reverse);
	}

	public void TransformSelected(ISpatialTransformer mapping, List<int> nodes, bool reverse = false)
	{
		if (!reverse)
		{
			if (nodes == null || nodes.Count == NodeCount)
			{
				Memory<Point> listBackingBuffer = UnsafeHelpers.GetListBackingBuffer(NodePoints);
				mapping.Transform(listBackingBuffer, listBackingBuffer);
				return;
			}
			Point[] points = new Point[nodes.Count];
			for (int i = 0; i < nodes.Count; i++)
			{
				points[i] = NodePoints[nodes[i]];
			}
			mapping.Transform(points, points);
			for (int j = 0; j < nodes.Count; j++)
			{
				NodePoints[nodes[j]] = points[j];
			}
		}
		else if (nodes == null || nodes.Count == NodeCount)
		{
			Memory<Point> listBackingBuffer2 = UnsafeHelpers.GetListBackingBuffer(NodePoints);
			mapping.ReverseTransform(listBackingBuffer2, listBackingBuffer2);
		}
		else
		{
			Point[] pointsArray = new Point[nodes.Count];
			for (int k = 0; k < nodes.Count; k++)
			{
				pointsArray[k] = NodePoints[nodes[k]];
			}
			mapping.ReverseTransform(pointsArray, pointsArray);
			for (int l = 0; l < nodes.Count; l++)
			{
				NodePoints[nodes[l]] = pointsArray[l];
			}
		}
	}

	public void StitchNodeStringsTriangles(List<int> Left, List<int> Right, List<int> newFaces = null)
	{
		int intervalIndex = 0;
		int ordinal = 0;
		int intervalIndex2 = 0;
		int ordinal2 = 0;
		while (true)
		{
			if (Left[ordinal] != Right[ordinal2])
			{
				if (!NodesToHalfEdge(Left[ordinal], Right[ordinal2]).IsNull())
				{
					if (Left[ordinal] == Right[ordinal2 + 1])
					{
						if (intervalIndex2 != ordinal2 - 1)
						{
							throw new StructuredException(Message.GenericError("Splicing failed during quad generation"));
						}
						int cIdx = AddCell();
						SetCellNodes(cIdx, new List<int>
						{
							Left[ordinal],
							Right[intervalIndex2],
							Right[ordinal2]
						}, ElementMode.EXISTING);
					}
					else
					{
						if (Left[ordinal + 1] != Right[ordinal2])
						{
							throw new StructuredException(Message.GenericError("Splicing failed during quad generation"));
						}
						if (intervalIndex != ordinal - 1)
						{
							throw new StructuredException(Message.GenericError("Splicing failed during quad generation"));
						}
						int cIdx2 = AddCell();
						SetCellNodes(cIdx2, new List<int>
						{
							Left[ordinal],
							Left[intervalIndex],
							Right[ordinal2]
						}, ElementMode.EXISTING);
					}
				}
				else
				{
					int addFace = AddFace(Left[ordinal], Right[ordinal2]);
					newFaces?.Add(addFace);
					List<int> intList = new List<int>
					{
						Right[ordinal2],
						Left[ordinal]
					};
					if (intervalIndex != ordinal)
					{
						intList.Add(Left[intervalIndex]);
					}
					if (intervalIndex2 != ordinal2 && Right[intervalIndex2] != Left[intervalIndex])
					{
						intList.Add(Right[intervalIndex2]);
					}
					if (intList.Count > 2)
					{
						int cIdx3 = AddCell();
						SetCellNodes(cIdx3, intList, ElementMode.EXISTING);
					}
				}
			}
			if (ordinal + 1 == Left.Count && ordinal2 + 1 == Right.Count)
			{
				break;
			}
			intervalIndex = ordinal;
			intervalIndex2 = ordinal2;
			if (ordinal + 1 == Left.Count)
			{
				ordinal2++;
				continue;
			}
			if (ordinal2 + 1 == Right.Count)
			{
				ordinal++;
				continue;
			}
			if (Left[ordinal] == Right[ordinal2])
			{
				ordinal++;
				ordinal2++;
				continue;
			}
			if (Left[ordinal + 1] == Right[ordinal2 + 1])
			{
				int cIdx4 = AddCell();
				List<int> nodes = new List<int>
				{
					Left[ordinal],
					Right[ordinal2],
					Left[ordinal + 1]
				};
				SetCellNodes(cIdx4, nodes, ElementMode.EXISTING);
				ordinal++;
				ordinal2++;
				continue;
			}
			if (Left[ordinal] == Right[ordinal2 + 1])
			{
				ordinal2++;
				continue;
			}
			if (Left[ordinal + 1] == Right[ordinal2])
			{
				ordinal++;
				continue;
			}
			Point point = NodePoints[Left[ordinal]];
			Point point2 = NodePoints[Left[ordinal + 1]];
			Point point3 = NodePoints[Right[ordinal2]];
			Point point4 = NodePoints[Right[ordinal2 + 1]];
			bool counterClockwiseNativePredicatesTerm = NativePredicates.CounterClockwise(point, point3, point2) > 0.0;
			bool counterClockwiseNativePredicatesTerm2 = NativePredicates.CounterClockwise(point, point3, point4) > 0.0;
			if (!counterClockwiseNativePredicatesTerm && !counterClockwiseNativePredicatesTerm2)
			{
				throw new StructuredException(Message.GenericError("Error in quad generation. Check patch corners or sharp, underresolved bends"));
			}
			if (!counterClockwiseNativePredicatesTerm2)
			{
				ordinal++;
			}
			else if (!counterClockwiseNativePredicatesTerm)
			{
				ordinal2++;
			}
			else if (NativePredicates.InCircle(point, point3, point4, point2) >= 0.0)
			{
				ordinal++;
			}
			else
			{
				ordinal2++;
			}
		}
	}

	public void StitchNodeStringsNearest(List<int> Left, List<int> Right, List<int> newFaces = null, bool mergeTriangles = true)
	{
		StitchNodeStringsTriangles(Left, Right, newFaces);
		if (mergeTriangles)
		{
			MergeTriangles(newFaces, checkCenters: false);
		}
	}

	public void StitchNodeStringsMixed(List<int> Left, List<int> Right, List<int> newFaces = null)
	{
		if (Left.Count < Right.Count)
		{
			Left = Left.CloneReversed();
			Right = Right.CloneReversed();
			StitchNodeStringsMixed(Right, Left, newFaces);
			return;
		}
		List<int> intList = ((Left.Count != 1) ? (from li in Enumerable.Range(0, Left.Count)
			select (int)(0.5 + ((double)Right.Count - 1.0) * (double)li / ((double)Left.Count - 1.0))).ToList() : new List<int> { 0 });
		for (int intervalIndex = 0; intervalIndex < Left.Count; intervalIndex++)
		{
			int index = intList[intervalIndex];
			int addFace = AddFace(Left[intervalIndex], Right[index]);
			newFaces?.Add(addFace);
			if (intervalIndex != 0)
			{
				List<int> intListList = new List<int>
				{
					Right[index],
					Left[intervalIndex],
					Left[intervalIndex - 1]
				};
				if (intList[intervalIndex - 1] != intList[intervalIndex])
				{
					intListList.Add(Right[intList[intervalIndex - 1]]);
				}
				int cIdx = AddCell();
				SetCellNodes(cIdx, intListList, ElementMode.EXISTING);
			}
		}
	}

	private (double, Point) MergeInfo(int cIdx)
	{
		List<HalfEdge> cellFace = CellFaces[cIdx];
		Point a = NodePoints[HalfEdgeNodeBack(cellFace[0])];
		Point b = NodePoints[HalfEdgeNodeForward(cellFace[0])];
		Point c = NodePoints[HalfEdgeNodeForward(cellFace[1])];
		double area = Triangle.ComputeArea(a, b, c);
		Point center = Circle.ComputeCenter(a, b, c);
		return (area, center);
	}

	public void MergeTriangles(List<int> faces, bool checkCenters = true, double distanceThreshold = double.MaxValue, double convexThreshold = 0.087, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		double[] doubleValues = new double[faces.Count];
		Dictionary<int, (double, Point)> valueTupleByInt = new Dictionary<int, (double, Point)>();
		ProgressReporter progressReporter = reporter.WithProgressModifier(0, 0.5f);
		ProgressReporter progressReporter2 = reporter.WithProgressModifier(50, 0.5f);
		for (int i = 0; i < faces.Count; i++)
		{
			int faceIndex = faces[i];
			progressReporter.ReportProgressFraction((float)i / (float)faces.Count);
			if (reporter.CancelRequested)
			{
				return;
			}
			if (FaceDeleted[faceIndex])
			{
				doubleValues[i] = double.MaxValue;
				continue;
			}
			Duple<int> duple = FaceCells[faceIndex];
			if (duple.A < 0 || duple.B < 0)
			{
				doubleValues[i] = double.MaxValue;
				continue;
			}
			if (CellNumSides(duple.A) != 3 || CellNumSides(duple.B) != 3)
			{
				doubleValues[i] = double.MaxValue;
				continue;
			}
			if (!valueTupleByInt.TryGetValue(duple.A, out var mergeInfo))
			{
				mergeInfo = MergeInfo(duple.A);
				valueTupleByInt[duple.A] = mergeInfo;
			}
			if (!valueTupleByInt.TryGetValue(duple.B, out var mergeInfo2))
			{
				mergeInfo2 = MergeInfo(duple.B);
				valueTupleByInt[duple.B] = mergeInfo2;
			}
			double mergeInfoMergeInfo2Term = Math.Sqrt(mergeInfo.Item1 + mergeInfo2.Item1);
			doubleValues[i] = SignedSpacing(faceIndex, mergeInfo.Item2, mergeInfo2.Item2) / mergeInfoMergeInfo2Term;
		}
		int[] facesArray = faces.ToArray();
		Array.Sort(doubleValues, facesArray);
		for (int j = 0; j < facesArray.Length; j++)
		{
			progressReporter2.ReportProgressFraction((float)j / (float)faces.Count);
			if (reporter.CancelRequested)
			{
				break;
			}
			int faceIndex2 = facesArray[j];
			if (doubleValues[j] >= distanceThreshold)
			{
				break;
			}
			Duple<int> duple2 = FaceCells[faceIndex2];
			if (CellNumSides(duple2.A) != 3 || CellNumSides(duple2.B) != 3)
			{
				continue;
			}
			if (checkCenters || convexThreshold >= 0.0)
			{
				int a = FaceNodes[faceIndex2].A;
				int b = FaceNodes[faceIndex2].B;
				HalfEdge he = new HalfEdge(faceIndex2, 0);
				he = HalfEdgeNextByCell(he);
				int index = HalfEdgeNodeForward(he);
				he = new HalfEdge(faceIndex2, 1);
				he = HalfEdgeNextByCell(he);
				int index2 = HalfEdgeNodeForward(he);
				if (convexThreshold == 0.0)
				{
					if (NativePredicates.CounterClockwise(NodePoints[index], NodePoints[a], NodePoints[index2]) <= 0.0 || NativePredicates.CounterClockwise(NodePoints[index2], NodePoints[b], NodePoints[index]) <= 0.0)
					{
						continue;
					}
				}
				else if (convexThreshold > 0.0 && (SinTurnAngle(NodePoints[index], NodePoints[a], NodePoints[index2]) <= convexThreshold || SinTurnAngle(NodePoints[index2], NodePoints[b], NodePoints[index]) <= convexThreshold))
				{
					continue;
				}
				if (checkCenters)
				{
					Polygon polygon = new Polygon(new Point[4]
					{
						NodePoints[a],
						NodePoints[index],
						NodePoints[b],
						NodePoints[index2]
					});
					Point point = valueTupleByInt[duple2.A].Item2;
					if (!polygon.Contains(point))
					{
						continue;
					}
					Point point2 = valueTupleByInt[duple2.B].Item2;
					if (!polygon.Contains(point2))
					{
						continue;
					}
				}
			}
			MergeCells(faceIndex2);
		}
		static double SinTurnAngle(Point p0, Point p1, Point p2)
		{
			Vector vector = p1 - p0;
			Vector vector2 = p2 - p0;
			vector = vector.UnitVector();
			vector2 = vector2.UnitVector();
			return vector.CrossProduct(vector2);
		}
	}

	public int HalfEdgeToCell(HalfEdge he)
	{
		return FaceCells[he.fIdx][he.orient];
	}

	public int HalfEdgeToOppositeCell(HalfEdge he)
	{
		return FaceCells[he.fIdx][1 - he.orient];
	}

	public HalfEdge HalfEdgeNext(HalfEdge he)
	{
		if (FaceCells[he.fIdx][he.orient] >= 0)
		{
			return HalfEdgeNextByCell(he);
		}
		return HalfEdgeNextByNode(he);
	}

	public HalfEdge HalfEdgePrev(HalfEdge he)
	{
		if (FaceCells[he.fIdx][he.orient] >= 0)
		{
			return HalfEdgePrevByCell(he);
		}
		return HalfEdgePrevByNode(he);
	}

	public int HalfEdgeNodeForward(HalfEdge he)
	{
		return FaceNodes[he.fIdx][1 - he.orient];
	}

	public int HalfEdgeNodeBack(HalfEdge he)
	{
		return FaceNodes[he.fIdx][he.orient];
	}

	public virtual HalfEdge HalfEdgeNextByCell(HalfEdge he)
	{
		int index = FaceCells[he.fIdx][he.orient];
		int halfEdgeNodeForward = HalfEdgeNodeForward(he);
		foreach (HalfEdge cellFace in CellFaces[index])
		{
			if (HalfEdgeNodeBack(cellFace) == halfEdgeNodeForward)
			{
				return cellFace;
			}
		}
		throw new DynamicMeshException("Failed to find next halfedge. Corrupt cell face list");
	}

	public HalfEdge HalfEdgePrevByCell(HalfEdge he)
	{
		int index = FaceCells[he.fIdx][he.orient];
		int halfEdgeNodeBack = HalfEdgeNodeBack(he);
		foreach (HalfEdge cellFace in CellFaces[index])
		{
			if (HalfEdgeNodeForward(cellFace) == halfEdgeNodeBack)
			{
				return cellFace;
			}
		}
		throw new Exception("Failed to find previous halfedge. Corrupt cell face list");
	}

	public List<HalfEdge> NodeToHalfEdges(int nIdx)
	{
		IReadOnlyList<int> readOnlyList = NodeToEdges(nIdx);
		List<HalfEdge> halfEdges = new List<HalfEdge>();
		foreach (int readOnlyList2 in readOnlyList)
		{
			if (FaceNodes[readOnlyList2].A == nIdx)
			{
				halfEdges.Add(new HalfEdge(readOnlyList2, 0));
			}
			if (FaceNodes[readOnlyList2].B == nIdx)
			{
				halfEdges.Add(new HalfEdge(readOnlyList2, 1));
			}
		}
		return halfEdges;
	}

	public List<int> NodeToNodes(int nIdx)
	{
		List<int> result = new List<int>();
		NodeToNodes(nIdx, result);
		return result;
	}

	public void NodeToNodes(int nIdx, List<int> result)
	{
		foreach (int nodeToEdge in NodeToEdges(nIdx))
		{
			if (FaceNodes[nodeToEdge].A != nIdx)
			{
				result.Add(FaceNodes[nodeToEdge].A);
			}
			else if (FaceNodes[nodeToEdge].B != nIdx)
			{
				result.Add(FaceNodes[nodeToEdge].B);
			}
		}
	}

	public HalfEdge NodeToBoundaryHalfEdge(int nIdx)
	{
		foreach (HalfEdge nodeToHalfEdge in NodeToHalfEdges(nIdx))
		{
			if (HalfEdgeToCell(nodeToHalfEdge) < 0)
			{
				return nodeToHalfEdge;
			}
		}
		return HalfEdge.NullHalfEdge;
	}

	public List<HalfEdge> NodeToSortedHalfEdges(int nIdx)
	{
		List<HalfEdge> nodeToHalfEdges = NodeToHalfEdges(nIdx);
		nodeToHalfEdges.Sort(Comparison);
		return nodeToHalfEdges;
		int Comparison(HalfEdge a, HalfEdge b)
		{
			double halfEdgeBearing = HalfEdgeBearing(a);
			double halfEdgeBearing2 = HalfEdgeBearing(b);
			if (halfEdgeBearing < halfEdgeBearing2)
			{
				return -1;
			}
			if (halfEdgeBearing > halfEdgeBearing2)
			{
				return 1;
			}
			return 0;
		}
	}

	public List<HalfEdge> NodeToSortedHalfEdges(int nIdx, HalfEdge he0)
	{
		List<HalfEdge> nodeToHalfEdges = NodeToHalfEdges(nIdx);
		double refAngle = HalfEdgeBearing(he0);
		nodeToHalfEdges.Sort(Comparison);
		return nodeToHalfEdges;
		int Comparison(HalfEdge a, HalfEdge b)
		{
			double halfEdgeBearing = HalfEdgeBearing(a);
			double halfEdgeBearing2 = HalfEdgeBearing(b);
			if (halfEdgeBearing < refAngle)
			{
				halfEdgeBearing += Math.PI * 2.0;
			}
			if (halfEdgeBearing2 < refAngle)
			{
				halfEdgeBearing2 += Math.PI * 2.0;
			}
			if (halfEdgeBearing < halfEdgeBearing2)
			{
				return -1;
			}
			if (halfEdgeBearing > halfEdgeBearing2)
			{
				return 1;
			}
			return 0;
		}
	}

	public virtual double HalfEdgeBearing(HalfEdge he)
	{
		return HalfEdgeBearingSimple(he);
	}

	public double HalfEdgeBearingSimple(HalfEdge he)
	{
		Point point = NodePoints[HalfEdgeNodeBack(he)];
		return (NodePoints[HalfEdgeNodeForward(he)] - point).MathBearing;
	}

	public HalfEdge HalfEdgeNextByNode(HalfEdge he)
	{
		return HalfEdgeTravByNode(he, 1);
	}

	public HalfEdge HalfEdgePrevByNode(HalfEdge he)
	{
		return HalfEdgeTravByNode(he, -1);
	}

	public HalfEdge HalfEdgeNextByNodeSimple(HalfEdge he)
	{
		return HalfEdgeTravByNode(he, 1, forceSimple: true);
	}

	public HalfEdge HalfEdgePrevByNodeSimple(HalfEdge he)
	{
		return HalfEdgeTravByNode(he, -1, forceSimple: true);
	}

	public HalfEdge HalfEdgeTravByNode(HalfEdge he, int direc, bool forceSimple = false)
	{
		HalfEdge halfEdge = HalfEdgeFlip(he);
		int nIdx;
		double halfEdgeBearingSimple;
		switch (direc)
		{
		case 1:
			nIdx = HalfEdgeNodeForward(he);
			halfEdgeBearingSimple = (forceSimple ? HalfEdgeBearingSimple(halfEdge) : HalfEdgeBearing(halfEdge));
			break;
		case -1:
			nIdx = HalfEdgeNodeBack(he);
			halfEdgeBearingSimple = (forceSimple ? HalfEdgeBearingSimple(halfEdge) : HalfEdgeBearing(he));
			break;
		default:
			throw new Exception("Bad direction");
		}
		List<HalfEdge> nodeToHalfEdges = NodeToHalfEdges(nIdx);
		if (nodeToHalfEdges.Count == 1)
		{
			return halfEdge;
		}
		double positiveInfinity = double.PositiveInfinity;
		foreach (HalfEdge nodeToHalfEdge in nodeToHalfEdges)
		{
			if (direc == 1)
			{
				if (nodeToHalfEdge.fIdx == he.fIdx && nodeToHalfEdge.orient == 1 - he.orient)
				{
					continue;
				}
			}
			else if (nodeToHalfEdge.fIdx == he.fIdx && nodeToHalfEdge.orient == he.orient)
			{
				continue;
			}
			double halfEdgeBearingSimple2 = (forceSimple ? HalfEdgeBearingSimple(nodeToHalfEdge) : HalfEdgeBearing(nodeToHalfEdge));
			double piSum = (double)direc * (halfEdgeBearingSimple - halfEdgeBearingSimple2);
			if (piSum < 0.0)
			{
				piSum += Math.PI * 2.0;
			}
			if (EnhancedMeshConsistencyChecks && (piSum < 0.0 || piSum > 6.283185307179596))
			{
				throw new StructuredException(Message.GenericError("Half-Edge traversal error, near ({Coordinate}).", FaceMidPoint(nodeToHalfEdge.fIdx)));
			}
			if (piSum < positiveInfinity)
			{
				positiveInfinity = piSum;
				halfEdge = nodeToHalfEdge;
			}
		}
		if (direc == -1)
		{
			halfEdge = HalfEdgeFlip(halfEdge);
		}
		return halfEdge;
	}

	public HalfEdge HalfEdgeFlip(HalfEdge he)
	{
		return new HalfEdge(he.fIdx, (byte)(1 - he.orient));
	}

	public bool SplitCell(int cIdx, int nodeA, int nodeB)
	{
		int newFaceIndex;
		return SplitCell(cIdx, nodeA, nodeB, out newFaceIndex);
	}

	public virtual bool SplitCell(int cIdx, int nodeA, int nodeB, out int newFaceIndex)
	{
		newFaceIndex = -1;
		if (!NodesToHalfEdge(nodeA, nodeB).IsNull())
		{
			return false;
		}
		List<HalfEdge> cellFaces = new List<HalfEdge>();
		List<HalfEdge> cellFacesList = new List<HalfEdge>();
		int i;
		for (i = 0; i < CellFaces[cIdx].Count && HalfEdgeNodeBack(CellFaces[cIdx][i]) != nodeA; i++)
		{
		}
		int j;
		for (j = 0; j < CellFaces[cIdx].Count; j++)
		{
			int index = (i + j) % CellFaces[cIdx].Count;
			HalfEdge halfEdge = CellFaces[cIdx][index];
			cellFacesList.Add(halfEdge);
			if (HalfEdgeNodeForward(halfEdge) == nodeB)
			{
				break;
			}
		}
		for (j++; j < CellFaces[cIdx].Count; j++)
		{
			int index2 = (i + j) % CellFaces[cIdx].Count;
			HalfEdge halfEdge = CellFaces[cIdx][index2];
			cellFaces.Add(halfEdge);
			if (HalfEdgeNodeForward(halfEdge) == nodeA)
			{
				break;
			}
		}
		int newFaceIndex2 = AddFace(nodeA, nodeB);
		int addCell = AddCell();
		cellFaces.Add(new HalfEdge(newFaceIndex2, 0));
		cellFacesList.Add(new HalfEdge(newFaceIndex2, 1));
		CellFaces[cIdx] = cellFaces;
		CellFaces[addCell] = cellFacesList;
		SetFaceCell(newFaceIndex2, 0, cIdx);
		SetFaceCell(newFaceIndex2, 1, addCell);
		foreach (HalfEdge halfEdge2 in cellFacesList)
		{
			SetFaceCell(halfEdge2, addCell, check: false);
		}
		newFaceIndex = newFaceIndex2;
		return true;
	}

	public int MergeCells(int fIdx)
	{
		Duple<int> duple = FaceCells[fIdx];
		int a = duple.A;
		List<HalfEdge> cellFaces = new List<HalfEdge>();
		HalfEdge he = new HalfEdge(fIdx, 0);
		he = HalfEdgeNext(he);
		HalfEdge halfEdge = he;
		do
		{
			cellFaces.Add(he);
			he = HalfEdgeNext(he);
			if (he.fIdx == fIdx)
			{
				he = HalfEdgeFlip(he);
				he = HalfEdgeNext(he);
			}
		}
		while (!(he == halfEdge));
		CellFaces[a] = cellFaces;
		DeleteCell(duple.B);
		foreach (HalfEdge halfEdge2 in cellFaces)
		{
			if (FaceCells[halfEdge2.fIdx][halfEdge2.orient] != a)
			{
				SetFaceCell(halfEdge2.fIdx, halfEdge2.orient, a);
			}
		}
		SetFaceCell(fIdx, 0, -99, check: false);
		DeleteFace(fIdx);
		return a;
	}

	public virtual int SplitFace(int fIdx, int node, out int newFace)
	{
		int nodeB = FaceNodes[fIdx][1];
		int faceCell = FaceCells[fIdx][0];
		int faceCell2 = FaceCells[fIdx][1];
		SetFaceNode(fIdx, 1, node);
		int newFace2 = AddFace(node, nodeB, faceCell, faceCell2);
		if (faceCell >= 0)
		{
			int indexOf = CellFaces[faceCell].IndexOf(new HalfEdge(fIdx, 0));
			CellFaces[faceCell].Insert(indexOf + 1, new HalfEdge(newFace2, 0));
		}
		if (faceCell2 >= 0)
		{
			int index = CellFaces[faceCell2].IndexOf(new HalfEdge(fIdx, 1));
			CellFaces[faceCell2].Insert(index, new HalfEdge(newFace2, 1));
		}
		newFace = newFace2;
		return node;
	}

	public virtual int MergeFaces(int nIdx)
	{
		List<HalfEdge> nodeToHalfEdges = NodeToHalfEdges(nIdx);
		if (nodeToHalfEdges.Count != 2)
		{
			throw new DynamicMeshException($"JoinFaces called with degree {nodeToHalfEdges.Count} node, not degree 2");
		}
		HalfEdgeNodeForward(nodeToHalfEdges[0]);
		int nIdx2 = HalfEdgeNodeForward(nodeToHalfEdges[1]);
		int fIdx = nodeToHalfEdges[1].fIdx;
		for (byte b = 0; b < 2; b++)
		{
			int faceCell = FaceCells[fIdx][b];
			if (faceCell >= 0)
			{
				CellFaces[faceCell].Remove(new HalfEdge(fIdx, b));
				SetFaceCell(fIdx, b, -99);
			}
		}
		DeleteFace(fIdx);
		int fIdx2 = nodeToHalfEdges[0].fIdx;
		SetFaceNode(fIdx2, nodeToHalfEdges[0].orient, nIdx2);
		DeleteNode(nIdx);
		return fIdx2;
	}

	public virtual bool FlipFace(int fIdx)
	{
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
		HalfEdge halfEdge = HalfEdgeNextByCell(he);
		HalfEdge halfEdge2 = HalfEdgeNextByCell(halfEdge);
		HalfEdge halfEdge3 = HalfEdgeNextByCell(HalfEdgeFlip(he));
		HalfEdge halfEdge4 = HalfEdgeNextByCell(halfEdge3);
		int nIdx = HalfEdgeNodeForward(halfEdge);
		int nIdx2 = HalfEdgeNodeForward(halfEdge3);
		SetFaceNode(fIdx, 0, nIdx);
		SetFaceNode(fIdx, 1, nIdx2);
		SetFaceCell(halfEdge2, duple.B, check: false);
		SetFaceCell(halfEdge4, duple.A, check: false);
		CellFaces[duple.A] = new List<HalfEdge>
		{
			halfEdge4,
			halfEdge,
			new HalfEdge(fIdx, 0)
		};
		CellFaces[duple.B] = new List<HalfEdge>
		{
			halfEdge2,
			halfEdge3,
			new HalfEdge(fIdx, 1)
		};
		return true;
	}

	public int NodeDegree(int nIdx)
	{
		return NodeToEdges(nIdx).Count;
	}

	public double FaceLength(int fIdx)
	{
		return NodePoints[FaceNodes[fIdx].A].DistanceTo(NodePoints[FaceNodes[fIdx].B]);
	}

	public double FaceLengthSquared(int fIdx)
	{
		return NodePoints[FaceNodes[fIdx].A].DistanceSquaredTo(NodePoints[FaceNodes[fIdx].B]);
	}

	public Point FaceMidPoint(int fIdx)
	{
		return 0.5 * (NodePoints[FaceNodes[fIdx].A] + NodePoints[FaceNodes[fIdx].B]);
	}

	public int CellNumSides(int cIdx)
	{
		return CellFaces[cIdx].Count;
	}

	public double CellCenterSpacing(int fIdx)
	{
		Point pA = CellCircumcenter(FaceCells[fIdx].A);
		Point pB = CellCircumcenter(FaceCells[fIdx].B);
		return SignedSpacing(fIdx, pA, pB);
	}

	public double SignedSpacing(int fIdx, Point pA, Point pB)
	{
		Vector other = (NodePoints[FaceNodes[fIdx].B] - NodePoints[FaceNodes[fIdx].A]).UnitVector().Perpendicular(clockwise: true);
		return (pB - pA).Dot(other);
	}

	public Point CellCircumcenter(int cIdx, bool allPoints = false)
	{
		return Circumcenter(CellSimplePolygon(cIdx), allPoints);
	}

	public static Point Circumcenter(Polygon poly, bool allPoints = false)
	{
		if (!allPoints || poly.Count == 3)
		{
			return Circle.ComputeCenter(poly.Points[0], poly.Points[1], poly.Points[2]);
		}
		Point point = new Point(0.0, 0.0);
		int ordinal = 0;
		for (int i = 0; i < poly.Count; i++)
		{
			try
			{
				point += Circle.ComputeCenter(poly.Points[i], poly.Points[(i + 1) % poly.Count], poly.Points[(i + 2) % poly.Count]);
			}
			catch (ColinearTriangleException)
			{
				continue;
			}
			ordinal++;
		}
		return 1.0 / (double)ordinal * point;
	}

	public Point CellNudgedCircumcenter(int cIdx, double clearance = 0.0)
	{
		Point cc = CellCircumcenter(cIdx, allPoints: true);
		return NudgeCenter(CellSimplePolygon(cIdx), cc, clearance);
	}

	public static Point NudgeCenter(Polygon pg, Point cc, double clearance = 0.0)
	{
		Point com = pg.ComputeCentroid();
		if (clearance >= 1.0)
		{
			return com;
		}
		if (clearance < 0.0)
		{
			return cc;
		}
		if (clearance > 0.0)
		{
			pg = new Polygon(pg.Points.Select((Point p) => (1.0 - clearance) * p + clearance * com));
		}
		if (pg.Contains(cc))
		{
			return cc;
		}
		Segment segment = new Segment(cc, com);
		foreach (Segment segment2 in pg.Segments)
		{
			if (segment.Intersects(segment2, out var x, out var y))
			{
				return new Point(x, y);
			}
		}
		return com;
	}

	public virtual bool MoveNode(int nIdx, Point p, bool check = true)
	{
		if (check && !CheckValidMove(nIdx, p))
		{
			return false;
		}
		NodePoints[nIdx] = p;
		return true;
	}

	public virtual bool CheckValidMove(int nIdx, Point pnt)
	{
		return true;
	}

	public List<int> ValidNodes(bool returnNullOnFullList = false)
	{
		int ordinal = 0;
		for (int i = 0; i < NodeDeleted.Count; i++)
		{
			if (NodeDeleted[i])
			{
				ordinal++;
			}
		}
		if (ordinal == 0)
		{
			if (returnNullOnFullList)
			{
				return null;
			}
			List<int> intList = new List<int>(NodeCount);
			for (int j = 0; j < NodeCount; j++)
			{
				intList.Add(j);
			}
			return intList;
		}
		List<int> nodeCountAboveOrdinal = new List<int>(NodeCount - ordinal);
		for (int k = 0; k < NodeCount; k++)
		{
			if (!NodeDeleted[k])
			{
				nodeCountAboveOrdinal.Add(k);
			}
		}
		return nodeCountAboveOrdinal;
	}

	public List<int> InternalNodes()
	{
		List<int> intList = new List<int>();
		foreach (int nodeIterator in NodeIterator())
		{
			if (!IsBoundaryNode(nodeIterator))
			{
				intList.Add(nodeIterator);
			}
		}
		return intList;
	}

	public IEnumerable<int> BoundaryNodes()
	{
		HashSet<int> intSet = new HashSet<int>();
		foreach (int faceIterator in FaceIterator())
		{
			if (FaceCells[faceIterator].A < 0 || FaceCells[faceIterator].B < 0)
			{
				intSet.Add(FaceNodes[faceIterator].A);
				intSet.Add(FaceNodes[faceIterator].B);
			}
		}
		return intSet;
	}

	public bool IsBoundaryNode(int nIdx)
	{
		foreach (int nodeToEdge in NodeToEdges(nIdx))
		{
			if (FaceCells[nodeToEdge].A < 0 || FaceCells[nodeToEdge].B < 0)
			{
				return true;
			}
		}
		return false;
	}

	public IEnumerable<int> NodeIterator()
	{
		for (int nIdx = 0; nIdx < NodeDeleted.Count; nIdx++)
		{
			if (!NodeDeleted[nIdx])
			{
				yield return nIdx;
			}
		}
	}

	public IEnumerable<int> FaceIterator()
	{
		for (int fIdx = 0; fIdx < FaceDeleted.Count; fIdx++)
		{
			if (!FaceDeleted[fIdx])
			{
				yield return fIdx;
			}
		}
	}

	public IEnumerable<int> CellIterator()
	{
		for (int cIdx = 0; cIdx < CellDeleted.Count; cIdx++)
		{
			if (!CellDeleted[cIdx])
			{
				yield return cIdx;
			}
		}
	}

	public Extent PointsExtent()
	{
		Extent result = Extent.Union(from nIdx in NodeIterator()
			select NodePoints[nIdx]);
		if (result.IsNull())
		{
			result = new Extent(0.0, 1.0, 0.0, 1.0);
		}
		else if (!result.IsValid())
		{
			double scaledDeltaXY = 0.1 * result.DeltaXY;
			if (scaledDeltaXY == 0.0)
			{
				scaledDeltaXY = 1.0;
			}
			result = result.EnforceMinDimensions(scaledDeltaXY, scaledDeltaXY);
		}
		return result;
	}

	public Extent FaceExtent(int fIdx)
	{
		Duple<int> duple = FaceNodes[fIdx];
		Point point = NodePoints[duple[0]];
		Point point2 = NodePoints[duple[1]];
		return new Extent(Math.Min(point.X, point2.X), Math.Max(point.X, point2.X), Math.Min(point.Y, point2.Y), Math.Max(point.Y, point2.Y));
	}

	public Segment FaceSegment(int fIdx)
	{
		return new Segment(NodePoints[FaceNodes[fIdx].A], NodePoints[FaceNodes[fIdx].B]);
	}

	public Segment FaceSegment(HalfEdge he)
	{
		return new Segment(NodePoints[HalfEdgeNodeBack(he)], NodePoints[HalfEdgeNodeForward(he)]);
	}

	public bool NodeInCellTopology(int nIdx, int cIdx)
	{
		foreach (HalfEdge cellFace in CellFaces[cIdx])
		{
			if (nIdx == HalfEdgeNodeForward(cellFace))
			{
				return true;
			}
		}
		return false;
	}

	public SpatialIndex<int> CellIndex()
	{
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(PointsExtent());
		foreach (int cellIterator in CellIterator())
		{
			spatialIndex.Add(CellSimplePolygon(cellIterator), cellIterator);
		}
		return spatialIndex;
	}

	public SpatialIndex<int> FaceIndex()
	{
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(PointsExtent());
		foreach (int faceIterator in FaceIterator())
		{
			spatialIndex.Add(FaceSegment(faceIterator), faceIterator);
		}
		return spatialIndex;
	}

	public SpatialIndex<int> PointIndex()
	{
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(PointsExtent());
		foreach (int nodeIterator in NodeIterator())
		{
			spatialIndex.Add(NodePoints[nodeIterator], nodeIterator);
		}
		return spatialIndex;
	}

	public List<int> FindNodes(Point point, ref SpatialIndex<int> pointIndex, double tolerance = 0.0)
	{
		if (pointIndex == null)
		{
			pointIndex = PointIndex();
		}
		List<int> nodes = new List<int>();
		foreach (int element in pointIndex.GetElements(point, tolerance))
		{
			if (point.DistanceTo(NodePoints[element]) <= tolerance)
			{
				nodes.Add(element);
			}
		}
		return nodes;
	}

	public int FindNearestNode(Point point, ref SpatialIndex<int> pointIndex, double tolerance = 0.0)
	{
		if (pointIndex == null)
		{
			pointIndex = PointIndex();
		}
		new List<int>();
		List<int> elements = pointIndex.GetElements(point, tolerance);
		double tolerance2 = tolerance;
		int result = -1;
		foreach (int element in elements)
		{
			double distanceTo = point.DistanceTo(NodePoints[element]);
			if (distanceTo <= tolerance2)
			{
				result = element;
				tolerance2 = distanceTo;
			}
		}
		return result;
	}

	public int FindOrAddNode(Point point, ref SpatialIndex<int> pointIndex, double tolerance = 0.0)
	{
		int nearestNode = FindNearestNode(point, ref pointIndex, tolerance);
		if (nearestNode < 0)
		{
			nearestNode = AddNode(point);
		}
		return nearestNode;
	}

	public int FindNearestNodeByContainingCell(Point point, SpatialIndex<int> cellIndex, double tolerance = 1E-06)
	{
		int cIdx = FindCellContaining(point, cellIndex);
		double tolerance2 = tolerance;
		int result = -1;
		foreach (int cellToNode in CellToNodes(cIdx))
		{
			double distanceTo = point.DistanceTo(NodePoints[cellToNode]);
			if (distanceTo <= tolerance2)
			{
				result = cellToNode;
				tolerance2 = distanceTo;
			}
		}
		return result;
	}

	public List<List<int>> ExtractNodeSequences(bool extractPerimeter = false)
	{
		List<List<int>> intListList = new List<List<int>>();
		HashSet<int> intSet = new HashSet<int>();
		if (extractPerimeter)
		{
			int cellCount = CellCount;
			Span<Duple<int>> destination = stackalloc Duple<int>[FaceCount];
			FaceCells.CopyTo(destination);
			AddCellsFromFaces();
			HalfEdge he = HalfEdge.NullHalfEdge;
			foreach (int faceIterator in FaceIterator())
			{
				int a = FaceCells[faceIterator].A;
				int b = FaceCells[faceIterator].B;
				if (a >= 0 && b < 0)
				{
					he = new HalfEdge(faceIterator, 1);
					break;
				}
				if (a < 0 && b >= 0)
				{
					he = new HalfEdge(faceIterator, 0);
					break;
				}
			}
			List<int> intList = new List<int> { HalfEdgeNodeBack(he) };
			do
			{
				intList.Add(HalfEdgeNodeForward(he));
				intSet.Add(he.fIdx);
				if (intList.First() == intList.Last())
				{
					break;
				}
				he = HalfEdgeNext(he);
			}
			while (!intSet.Contains(he.fIdx));
			intListList.Add(intList);
			for (int i = cellCount; i < CellCount; i++)
			{
				DeleteCell(i);
			}
			for (int j = 0; j < FaceCount; j++)
			{
				FaceCells[j] = destination[j];
			}
		}
		foreach (int faceIterator2 in FaceIterator())
		{
			if (intSet.Contains(faceIterator2))
			{
				continue;
			}
			List<int> intList2 = new List<int>();
			for (byte b2 = 0; b2 < 2; b2++)
			{
				HalfEdge he2 = new HalfEdge(faceIterator2, b2);
				do
				{
					intList2.Add(HalfEdgeNodeForward(he2));
					intSet.Add(he2.fIdx);
					if (NodeDegree(HalfEdgeNodeForward(he2)) != 2)
					{
						break;
					}
					he2 = HalfEdgeNext(he2);
				}
				while (!intSet.Contains(he2.fIdx));
				intList2.Reverse();
			}
			intListList.Add(intList2);
		}
		return intListList;
	}

	public OperationResult TryExtractBoundaryComplexPolygon(out ComplexPolygon result)
	{
		List<Polygon> polygons = new List<Polygon>();
		HashSet<int> intSet = new HashSet<int>();
		foreach (int faceIterator in FaceIterator())
		{
			HalfEdge halfEdge = HalfEdge.NullHalfEdge;
			for (byte b = 0; b < 2; b++)
			{
				HalfEdge halfEdge2 = new HalfEdge(faceIterator, b);
				if (HalfEdgeToCell(halfEdge2) < 0)
				{
					halfEdge = halfEdge2;
					break;
				}
			}
			if (halfEdge.IsNull() || intSet.Contains(faceIterator))
			{
				continue;
			}
			Polygon polygon = new Polygon();
			HalfEdge halfEdge3 = halfEdge;
			for (int i = 0; i < FaceCount; i++)
			{
				polygon.Add(NodePoints[HalfEdgeNodeForward(halfEdge)]);
				if (intSet.Contains(halfEdge.fIdx))
				{
					result = null;
					return false;
				}
				intSet.Add(halfEdge.fIdx);
				halfEdge = HalfEdgeNext(halfEdge);
				if (halfEdge == halfEdge3)
				{
					break;
				}
			}
			polygons.Add(polygon);
		}
		OperationResult operationResult = ComplexPolygon.TryCreateComplexPolygon(polygons, out result);
		if (!operationResult)
		{
			for (int j = 0; j < operationResult.Messages.Count; j++)
			{
				if (operationResult.Messages[j].MessageType == ComplexPolygon.MessageCodes.InnerRingNoParent)
				{
					operationResult.Messages[j] = MessageCodes.GetNoncontiguousMeshError();
				}
			}
		}
		return operationResult;
	}

	public void AddCellsFromFaces(int maxSides = -1, int skipFaceCell = 0)
	{
		if (maxSides < 0)
		{
			maxSides = FaceCount;
		}
		foreach (List<HalfEdge> cycle in FindCycles(maxSides))
		{
			if (skipFaceCell != 0)
			{
				int ordinal = 0;
				foreach (HalfEdge cycle2 in cycle)
				{
					if (FaceCells[cycle2.fIdx][cycle2.orient] == skipFaceCell)
					{
						ordinal++;
					}
				}
				if (ordinal == cycle.Count)
				{
					continue;
				}
				if (ordinal > 0)
				{
					throw new Exception("Inconsistent FaceCell values");
				}
			}
			int cIdx = AddCell();
			foreach (HalfEdge cycle3 in cycle)
			{
				CellAddFace(cIdx, cycle3);
			}
		}
	}

	public List<List<HalfEdge>> FindCycles(int maxNodes, bool onlyCCW = true)
	{
		List<List<List<HalfEdge>>> linearFeatures;
		return FindCycles(maxNodes, out linearFeatures, onlyCCW, findLinear: false);
	}

	public List<List<HalfEdge>> FindCycles(int maxNodes, out List<List<List<HalfEdge>>> linearFeatures, bool onlyCCW = true, bool findLinear = true, bool simpleGeometry = false)
	{
		HashSet<HalfEdge> halfEdges = new HashSet<HalfEdge>();
		List<List<HalfEdge>> cycles = new List<List<HalfEdge>>();
		List<List<List<HalfEdge>>> localLinearFeatures = null;
		List<List<HalfEdge>> halfEdgeListList;
		if (findLinear)
		{
			linearFeatures = new List<List<List<HalfEdge>>>();
			halfEdgeListList = new List<List<HalfEdge>>();
			localLinearFeatures = linearFeatures;
		}
		else
		{
			linearFeatures = null;
			halfEdgeListList = null;
		}
		foreach (int faceIterator in FaceIterator())
		{
			byte b = 0;
			while (b < 2)
			{
				HalfEdge halfEdge = new HalfEdge(faceIterator, b);
				List<List<HalfEdge>> linears;
				if (!halfEdges.Contains(halfEdge))
				{
					if (HalfEdgeToCell(halfEdge) >= 0)
					{
						halfEdges.Add(halfEdge);
					}
					else
					{
						HalfEdge halfEdge2 = halfEdge;
						int count = cycles.Count;
						List<HalfEdge> halfEdgesList = new List<HalfEdge>();
						linears = null;
						if (findLinear)
						{
							linears = new List<List<HalfEdge>>();
						}
						while (halfEdgesList.Count < 2 * FaceCount)
						{
							if (halfEdges.Contains(halfEdge))
							{
								halfEdgesList.Clear();
								if (findLinear)
								{
									linears.Clear();
								}
								break;
							}
							halfEdges.Add(halfEdge);
							int halfEdgeNodeForward = HalfEdgeNodeForward(halfEdge);
							halfEdgesList.Add(halfEdge);
							int countOffset = -1;
							countOffset = halfEdgesList.Count - 1;
							while (countOffset >= 0 && HalfEdgeNodeBack(halfEdgesList[countOffset]) != halfEdgeNodeForward)
							{
								countOffset--;
							}
							if (countOffset >= 0)
							{
								List<HalfEdge> range = halfEdgesList.GetRange(countOffset, halfEdgesList.Count - countOffset);
								LogCycle(range);
								halfEdgesList.RemoveRange(countOffset);
							}
							halfEdge = ((!simpleGeometry) ? HalfEdgeNextByNode(halfEdge) : HalfEdgeNextByNodeSimple(halfEdge));
							if (halfEdge2 == halfEdge)
							{
								if (halfEdgesList.Count > 0)
								{
									LogCycle(halfEdgesList);
								}
								break;
							}
						}
						if (findLinear)
						{
							if (cycles.Count == count)
							{
								halfEdgeListList.AddRange(linears);
							}
							else
							{
								linearFeatures[count] = linears;
							}
						}
					}
				}
				b++;
				void LogCycle(List<HalfEdge> finishedCycle)
				{
					if (finishedCycle.Count == 2 && finishedCycle[0].fIdx == finishedCycle[1].fIdx)
					{
						linears?.Add(finishedCycle);
					}
					else if (finishedCycle.Count <= maxNodes && (!onlyCCW || !(CycleArea(finishedCycle, simpleGeometry) >= 0.0)))
					{
						cycles.Add(finishedCycle);
						localLinearFeatures?.Add(new List<List<HalfEdge>>());
					}
				}
			}
		}
		if (findLinear && halfEdgeListList.Any())
		{
			cycles.Add(new List<HalfEdge>());
			linearFeatures.Add(halfEdgeListList);
		}
		return cycles;
	}

	public virtual double CycleArea(List<HalfEdge> cycle, bool simpleGeometry)
	{
		Polygon polygon = new Polygon(cycle.Count);
		foreach (HalfEdge cycle2 in cycle)
		{
			polygon.Add(NodePoints[HalfEdgeNodeForward(cycle2)]);
		}
		return polygon.Area;
	}

	public Polyline FaceSimpleGeometry(int fIdx)
	{
		return new Polyline(new List<Point>
		{
			NodePoints[FaceNodes[fIdx].A],
			NodePoints[FaceNodes[fIdx].B]
		});
	}

	public Polyline FaceSimpleGeometry(HalfEdge he)
	{
		Polyline polyline = new Polyline(2);
		if (he.orient == 0)
		{
			polyline.Add(NodePoints[FaceNodes[he.fIdx].A]);
			polyline.Add(NodePoints[FaceNodes[he.fIdx].B]);
		}
		else
		{
			polyline.Add(NodePoints[FaceNodes[he.fIdx].B]);
			polyline.Add(NodePoints[FaceNodes[he.fIdx].A]);
		}
		return polyline;
	}

	public int FindCellContaining(Point pt, SpatialIndex<int> cellIndex, double tolerance = 0.0001)
	{
		List<int> elements = cellIndex.GetElements(pt, tolerance);
		double maxValue = double.MaxValue;
		int result = -1;
		foreach (int element in elements)
		{
			if (CellSimplePolygon(element).ContainsFuzzy(pt.X, pt.Y, out var distSquared, tolerance))
			{
				if (distSquared == 0.0)
				{
					return element;
				}
				if (distSquared < maxValue)
				{
					result = element;
					maxValue = distSquared;
				}
			}
		}
		return result;
	}

	public List<int> BreadthFirstAdjacentCells(int startIdx, IReadOnlyCollection<int> cellsSubset = null)
	{
		IReadOnlyCollection<int> readOnlyCollection = ((cellsSubset != null) ? cellsSubset : Enumerable.Range(0, CellCount).ToList());
		if (readOnlyCollection == null)
		{
			throw new ArgumentNullException("cellsToCheck");
		}
		if (CellFaces == null || FaceCells == null)
		{
			return new List<int>();
		}
		List<int> intList = new List<int>();
		HashSet<int> allowedSet = new HashSet<int>();
		foreach (int readOnlyCollection2 in readOnlyCollection)
		{
			if (readOnlyCollection2 >= 0 && readOnlyCollection2 < CellCount && allowedSet.Add(readOnlyCollection2))
			{
				intList.Add(readOnlyCollection2);
			}
		}
		if (intList.Count == 0)
		{
			return new List<int>();
		}
		HashSet<int> visited = new HashSet<int>();
		List<int> result = new List<int>(intList.Count);
		DoBfsFrom(startIdx);
		while (result.Count < allowedSet.Count)
		{
			int ordinal = -1;
			foreach (int ordinal2 in intList)
			{
				if (!visited.Contains(ordinal2))
				{
					ordinal = ordinal2;
					break;
				}
			}
			if (ordinal == -1)
			{
				break;
			}
			DoBfsFrom(ordinal);
		}
		return result;
		void DoBfsFrom(int seed)
		{
			if (seed >= 0 && seed < CellCount && visited.Add(seed))
			{
				Queue<int> intQueue = new Queue<int>();
				intQueue.Enqueue(seed);
				while (intQueue.Count > 0)
				{
					int dequeue = intQueue.Dequeue();
					if (allowedSet.Contains(dequeue))
					{
						result.Add(dequeue);
					}
					List<HalfEdge> cellFace = CellFaces[dequeue];
					if (cellFace != null)
					{
						foreach (HalfEdge cellFace2 in cellFace)
						{
							int faceCell = FaceCells[cellFace2.fIdx][1 - cellFace2.orient];
							if (faceCell >= 0 && faceCell < CellCount && visited.Add(faceCell))
							{
								intQueue.Enqueue(faceCell);
							}
						}
					}
				}
			}
		}
	}

	public DynamicTINFactory<TPred> TriangulateMesh<TPred>(Func<int, double> ZFunction = null) where TPred : struct, IPredicates
	{
		DynamicTINFactory<TPred> DTin = new DynamicTINFactory<TPred>();
		DTin.OnEdgeOverConstrained = (ConstrainedEdge a, ConstrainedEdge b) =>
		{
			Point location = DTin.Tin.Points[a.PointA];
			throw new MeshTriangulationError("Mesh could not be triangulated", location);
		};
		List<Point> points = new List<Point>();
		List<double> doubleList = new List<double>();
		foreach (int nodeIterator in NodeIterator())
		{
			if (NodeDegree(nodeIterator) <= 0)
			{
				points.Add(NodePoints[nodeIterator]);
				if (ZFunction != null)
				{
					doubleList.Add(ZFunction(nodeIterator));
				}
			}
		}
		if (points.Count > 0)
		{
			if (ZFunction == null)
			{
				DTin.AddMassPoints(new PointCollection(points));
			}
			else
			{
				DTin.AddMassPoints(new PointCollection(points, doubleList));
			}
		}
		List<List<int>> extractNodeSequences = ExtractNodeSequences(extractPerimeter: true);
		for (int intervalIndex = 0; intervalIndex < extractNodeSequences.Count; intervalIndex++)
		{
			List<int> extractNodeSequence = extractNodeSequences[intervalIndex];
			points.Clear();
			doubleList.Clear();
			bool extractNodeSequenceTerm = extractNodeSequence.First() == extractNodeSequence.Last() && extractNodeSequence.Count() > 3;
			if (extractNodeSequenceTerm)
			{
				extractNodeSequence.RemoveAt(0);
			}
			foreach (int extractNodeSequence2 in extractNodeSequence)
			{
				points.Add(NodePoints[extractNodeSequence2]);
				if (ZFunction != null)
				{
					doubleList.Add(ZFunction(extractNodeSequence2));
				}
			}
			if (extractNodeSequenceTerm)
			{
				Polygon polygon = ((ZFunction != null) ? new Polygon(points, doubleList) : new Polygon(points));
				if (intervalIndex == 0)
				{
					DTin.AddPerimeter(polygon);
				}
				else
				{
					DTin.AddBreakline(polygon);
				}
			}
			else
			{
				Polyline breakline = ((ZFunction != null) ? new Polyline(points, doubleList) : new Polyline(points));
				DTin.AddBreakline(breakline);
			}
		}
		DTin.ComputeTIN();
		return DTin;
	}

	public virtual void DumpToJson(string filename = "C:\\Temp\\cmd.json")
	{
		List<object> localValue = new List<object>
		{
			new List<object> { "SET", "g_raw", this },
			new List<object> { "PYTHON", "\r\ng=json_to_mesh(g_raw)\r\nfig,ax = plt.subplots(num = 2, clear = 1,figsize=(11,9))\r\nax.set_adjustable('datalim')\r\nax.set_position([0,0,1,1])\r\nif 'rawStringXY' in g.edges.dtype.names:\r\n    g.plot_edges(ax = ax, color = 'k', lw = 0.8, alpha = 0.6, subedges='rawStringXY')\r\nelse:\r\n    g.plot_edges(ax = ax, color = 'k', lw = 0.8, alpha = 0.6)\r\nax.axis('equal')\r\n" }
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

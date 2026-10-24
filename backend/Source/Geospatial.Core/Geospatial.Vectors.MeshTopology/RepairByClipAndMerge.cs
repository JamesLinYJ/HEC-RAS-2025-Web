using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geospatial.Vectors.Topology;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class RepairByClipAndMerge : RepairProcess
{
	private class RepairState
	{
		private struct SplitState
		{
			public int fOriginal;

			public Polyline pline;

			public List<(PointOnPolyline, int)> splits;

			public SplitState(int fIdx, SlidingMesh mesh)
			{
				fOriginal = fIdx;
				pline = mesh.FaceSmoothLine[fIdx];
				splits = new List<(PointOnPolyline, int)> { (pline.FirstPointOnPolyline(), fIdx) };
			}
		}

		private Clearing clearing;

		private ISizeFunction sizeFunction;

		private Dictionary<int, SplitState> faceSplits;

		private SlidingMesh mesh => clearing.mesh;

		private ConceptualMesh cmesh => clearing.cmesh;

		public RepairState(Clearing clearing, ISizeFunction sizeFunction)
		{
			this.clearing = clearing;
			faceSplits = new Dictionary<int, SplitState>();
			this.sizeFunction = sizeFunction;
		}

		public void Repair()
		{
			OperationResult res = new OperationResult();
			RepairProcess.SimplifyMultipointConflicts(mesh, cmesh, out var conflictsToRepair, attemptResolve: false, assumeSimpleValid: false, res);
			if (conflictsToRepair.Count == 0)
			{
				return;
			}
			HashSet<int> intSet = new HashSet<int>();
			int nodeCount = mesh.NodeCount;
			int ordinal = 0;
			foreach (var (localValue, localValue2) in conflictsToRepair)
			{
				if (mesh.FaceConstraints[localValue] == ConstraintState.FIXED)
				{
					ordinal++;
					continue;
				}
				intSet.AddRange(mesh.FaceCells[localValue]);
				intSet.AddRange(mesh.FaceCells[localValue2]);
				SplitAtIntersections(localValue, localValue2);
			}
			foreach (int ordinal2 in intSet)
			{
				if (ordinal2 >= 0)
				{
					mesh.DeleteCell(ordinal2);
				}
			}
			mesh.AddCellsFromFaces(-1, -2);
			List<HalfEdge> halfEdges = new List<HalfEdge>();
			foreach (int faceIterator in mesh.FaceIterator())
			{
				if (mesh.FaceConstraints[faceIterator] < (ConstraintState)0)
				{
					continue;
				}
				int arcIdx = mesh.constraints[(int)mesh.FaceConstraints[faceIterator]].ArcIdx;
				if (arcIdx < 0)
				{
					throw new Exception("Clearing mesh missing arc index");
				}
				Duple<int> duple = cmesh.FaceCells[arcIdx];
				if (!cmesh.IsEmpty(duple.A) && !cmesh.IsEmpty(duple.B))
				{
					continue;
				}
				int faceConstraintOrientation = mesh.FaceConstraintOrientation(faceIterator);
				for (int i = 0; i < 2; i++)
				{
					if (mesh.FaceCells[faceIterator][i] < 0)
					{
						continue;
					}
					int idx = i ^ faceConstraintOrientation;
					if (duple[idx] >= 0 && cmesh.CellRegionMetadata[duple[idx]].PatchType != PatchType.Empty)
					{
						continue;
					}
					mesh.DeleteCell(mesh.FaceCells[faceIterator][i], halfEdges);
					int faceCount = mesh.FaceCount;
					while (faceCount >= 0 && halfEdges.Count != 0)
					{
						HalfEdge halfEdge = halfEdges.Last();
						halfEdges.RemoveAt(halfEdges.Count - 1);
						if (mesh.FaceConstraints[halfEdge.fIdx] == ConstraintState.FREE && !mesh.FaceDeleted[halfEdge.fIdx])
						{
							mesh.DeleteFaceAndNakedNodesCascade(halfEdge.fIdx, halfEdges);
						}
						faceCount--;
					}
					if (faceCount < 0)
					{
						throw new Exception("Infinite breaker tripped during mesh repair");
					}
				}
			}
			MergeCellsAndFaces(nodeCount);
		}

		private void MergeCellsAndFaces(int nodeCountOriginal)
		{
			HashSet<int> cellIterator = new HashSet<int>(mesh.CellIterator());
			HashSet<int> intSet = new HashSet<int>(20);
			for (int ordinal = 10; ordinal > 0; ordinal--)
			{
				foreach (int cellIterator2 in cellIterator)
				{
					if (mesh.CellDeleted[cellIterator2] || mesh.CellFaces[cellIterator2].Count != 2)
					{
						continue;
					}
					int fIdxCellFaceTerm = -1;
					foreach (HalfEdge cellFace in mesh.CellFaces[cellIterator2])
					{
						if (mesh.FaceConstraints[cellFace.fIdx] == ConstraintState.FREE)
						{
							fIdxCellFaceTerm = cellFace.fIdx;
							break;
						}
					}
					if (fIdxCellFaceTerm < 0)
					{
						throw new Exception("Transient cell with two faces does not have a free face");
					}
					intSet.Add(mesh.FaceNodes[fIdxCellFaceTerm].A);
					intSet.Add(mesh.FaceNodes[fIdxCellFaceTerm].B);
					mesh.MergeCells(fIdxCellFaceTerm);
				}
				cellIterator.Clear();
				foreach (int ordinal2 in intSet)
				{
					if (ordinal2 < nodeCountOriginal || mesh.NodeDegree(ordinal2) != 2)
					{
						continue;
					}
					foreach (int nodeToCellIndex in mesh.NodeToCells(ordinal2))
					{
						cellIterator.Add(nodeToCellIndex);
					}
					mesh.MergeFaces(ordinal2);
				}
				if (cellIterator.Count == 0)
				{
					break;
				}
				intSet.Clear();
			}
			PostMergeFaces postMergeFaces = new PostMergeFaces(mesh, sizeFunction);
			postMergeFaces.localIntersectionFactor = 100.0;
			postMergeFaces.maxSidesAllowed = 6;
			postMergeFaces.enableCollapse = true;
			postMergeFaces.cellScaleFactor = 1.0;
			postMergeFaces.faceScaleFactor = 1.0;
			postMergeFaces.OptimizeAdaptive(mesh.NodeCount);
			if (sizeFunction != null)
			{
				foreach (int faceIterator in mesh.FaceIterator())
				{
					if (mesh.FaceTargetLength[faceIterator] < 0f)
					{
						mesh.FaceTargetLength[faceIterator] = (float)sizeFunction.Query(mesh.FaceMidPoint(faceIterator));
					}
				}
			}
			new OptimizeNodeSkew(mesh).Optimize(10);
		}

		private void SplitAtIntersections(int fIdxConstrained, int fIdxClip)
		{
			double tolerance = 1E-06;
			if (!faceSplits.TryGetValue(fIdxConstrained, out var faceSplits2))
			{
				faceSplits2 = new SplitState(fIdxConstrained, mesh);
				faceSplits[fIdxConstrained] = faceSplits2;
			}
			if (!faceSplits.TryGetValue(fIdxClip, out var faceSplits3))
			{
				faceSplits3 = new SplitState(fIdxClip, mesh);
				faceSplits[fIdxClip] = faceSplits3;
			}
			Polyline pline = faceSplits2.pline;
			Polyline pline2 = faceSplits3.pline;
			double length = pline.Length;
			double length2 = pline2.Length;
			List<LineIntersection> allIntersections = pline.GetAllIntersections(pline2);
			for (int i = 0; i < allIntersections.Count; i++)
			{
				LineIntersection lineIntersection = allIntersections[i];
				if (i == 0 || i == allIntersections.Count - 1)
				{
					double distanceAlongOverLength = pline.DistanceAlong(lineIntersection.AIntersection) / length;
					double distanceAlongOverLength2 = pline2.DistanceAlong(lineIntersection.BIntersection) / length2;
					bool condition = distanceAlongOverLength <= tolerance || distanceAlongOverLength >= 1.0 - tolerance;
					bool condition2 = distanceAlongOverLength2 <= tolerance || distanceAlongOverLength2 >= 1.0 - tolerance;
					if (condition & condition2)
					{
						continue;
					}
				}
				Point point = lineIntersection.Point;
				int addNode = mesh.AddNode(point);
				mesh.NodeConstraints[addNode] = mesh.FaceConstraints[fIdxConstrained];
				mesh.UpdatePointOnPolyline(addNode);
				int j;
				for (j = 0; j < faceSplits2.splits.Count - 1 && !(faceSplits2.splits[j + 1].Item1 > lineIntersection.AIntersection); j++)
				{
				}
				mesh.SplitFace(faceSplits2.splits[j].Item2, addNode, out var newFace);
				faceSplits2.splits.Insert(j + 1, (lineIntersection.AIntersection, newFace));
				for (j = 0; j < faceSplits3.splits.Count - 1 && !(faceSplits3.splits[j + 1].Item1 > lineIntersection.BIntersection); j++)
				{
				}
				mesh.SplitFace(faceSplits3.splits[j].Item2, addNode, out var newFace2);
				faceSplits3.splits.Insert(j + 1, (lineIntersection.BIntersection, newFace2));
			}
		}
	}

	public static bool Process(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts = false)
	{
		return new RepairByClipAndMerge(mesh, sizeFunction, cmesh, logConflicts).Process(logConflicts);
	}

	private RepairByClipAndMerge(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts)
		: base(mesh, sizeFunction, cmesh)
	{
	}

	public void DumpClearings(List<Clearing> clearings)
	{
		List<List<object>> objectListList = new List<List<object>>
		{
			new List<object> { "PYTHON", "clearings=[]" },
			new List<object> { "SET", "g_raw", mesh },
			new List<object> { "SET", "cg_raw", cmesh }
		};
		for (int i = 0; i < clearings.Count; i++)
		{
			objectListList.AddRange(new _003C_003Ez__ReadOnlyArray<List<object>>(new List<object>[2]
			{
				new List<object>
				{
					"SET",
					"tmp_raw",
					clearings[i].mesh
				},
				new List<object> { "PYTHON", "clearings.append(json_to_mesh(tmp_raw))" }
			}));
		}
		objectListList.Add(new List<object> { "PYTHON", "\r\ng=json_to_mesh(g_raw)\r\nfig,ax=plt.subplots(num=1,clear=1)\r\nax.set_adjustable('datalim')\r\ng.plot_edges(lw=0.5,subedges='smoothStringXY',color='k')\r\ng.plot_cells(lw=0,subedges='smoothStringXY',color='0.8',zorder=-1)\r\nfor i,clear_g in enumerate(clearings):\r\n    pnt=clear_g.cells_centroid().mean(axis=0)\r\n    ax.text(pnt[0],pnt[1],str(i))" });
		using FileStream utf8Json = File.Create("C:\\Temp\\cmd.json");
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			Converters = { (JsonConverter)new TinJsonConverter() }
		};
		JsonSerializer.Serialize(utf8Json, objectListList, options);
	}

	private bool Process(bool logConflicts)
	{
		OperationResult res = new OperationResult();
		List<Clearing> clearAndRefineConflictsKeepMesh = ClearAndRefineConflictsKeepMesh(moveConstrainedToClearing: true, 1, res);
		if (clearAndRefineConflictsKeepMesh.Count == 0)
		{
			return true;
		}
		if (logConflicts)
		{
			LogConflicts(clearAndRefineConflictsKeepMesh, res);
		}
		List<int> intList = new List<int>();
		HashSet<int> hs = new HashSet<int>();
		Dictionary<int, int> arcToConstraint = null;
		for (int i = 0; i < clearAndRefineConflictsKeepMesh.Count; i++)
		{
			Clearing clearing = clearAndRefineConflictsKeepMesh[i];
			Simplify(clearing);
			mesh.MergeMesh(clearing.mesh, out var nodeMap, out var faceMap, out var _, ref arcToConstraint);
			hs.AddRange(nodeMap);
			HalfEdge[] faceMapArray = faceMap;
			for (int j = 0; j < faceMapArray.Length; j++)
			{
				HalfEdge halfEdge = faceMapArray[j];
				if (halfEdge.fIdx >= 0 && mesh.FaceConstraints[halfEdge.fIdx] < (ConstraintState)0)
				{
					intList.Add(halfEdge.fIdx);
				}
			}
		}
		return false;
	}

	public void Simplify(Clearing clearing)
	{
		new RepairState(clearing, sizeFunction).Repair();
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class RepairProcess
{
	public struct ArcFace(int fIdx, int clearingIdx)
	{
		public int fIdx = fIdx;

		public int clearingIdx = clearingIdx;

		public Polyline polyline = null;
	}

	protected SlidingMesh mesh;

	protected ConceptualMesh cmesh;

	protected ISizeFunction sizeFunction;

	public RepairProcess(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh)
	{
		this.mesh = mesh;
		this.sizeFunction = sizeFunction;
		this.cmesh = cmesh;
	}

	public static bool EndPointIntersection(PointOnPolyline intersection, int segCount)
	{
		double tolerance = 1E-06;
		if ((intersection.SegmentIndex != 0 || !(intersection.Fraction <= tolerance)) && (intersection.SegmentIndex + 1 != segCount || !(intersection.Fraction >= 1.0 - tolerance)))
		{
			return intersection.SegmentIndex == segCount;
		}
		return true;
	}

	public static bool PolylinesConflict(Polyline plineA, Polyline plineB)
	{
		if (plineA.PointCount == 2 && plineB.PointCount == 2)
		{
			return false;
		}
		for (int i = 0; i < plineA.SegmentCount; i++)
		{
			Segment segment = plineA.Segments[i];
			for (int j = 0; j < plineB.SegmentCount; j++)
			{
				Segment seg = plineB.Segments[j];
				if (segment.IntersectPointCheckNodes(seg, out var _, out var _, out var t, out var t2))
				{
					bool segmentCountPlineATerm = (i == 0 && t == 0.0) || (i == plineA.SegmentCount - 1 && t == 1.0);
					bool t2IsZero = (j == 0 && t2 == 0.0) || (j == plineB.SegmentCount - 1 && t2 == 1.0);
					if (!(segmentCountPlineATerm & t2IsZero))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	public static void SimplifyMultipointConflicts(SlidingMesh mesh, ConceptualMesh cmesh, out List<(int, int)> conflictsToRepair, bool attemptResolve, bool assumeSimpleValid, OperationResult res)
	{
		double polylineConflictEpsilon = 1E-10;
		HashSet<int> intSet = new HashSet<int>();
		HashSet<(int, int)> conflictSet = new HashSet<(int, int)>();
		List<int> intList = new List<int>();
		IEnumerable<int> enumerable = mesh.CellIterator();
		List<int> intListList = new List<int>();
		while (enumerable.Any())
		{
			HashSet<int> enumerableSet = new HashSet<int>();
			foreach (int enumerable2 in enumerable)
			{
				bool condition = false;
				for (int i = 0; i < mesh.CellFaces[enumerable2].Count - 1; i++)
				{
					HalfEdge he = mesh.CellFaces[enumerable2][i];
					Polyline faceSmoothGeometry = mesh.GetFaceSmoothGeometry(he.fIdx);
					for (int j = i + 1; j < mesh.CellFaces[enumerable2].Count; j++)
					{
						HalfEdge he2 = mesh.CellFaces[enumerable2][j];
						Polyline faceSmoothGeometry2 = mesh.GetFaceSmoothGeometry(he2.fIdx);
						if (!Polyline.Conflict(faceSmoothGeometry, faceSmoothGeometry2, polylineConflictEpsilon))
						{
							continue;
						}
						intListList.Clear();
						if (attemptResolve && MaybeResolveBySimplifying(mesh, cmesh, he.fIdx, faceSmoothGeometry, he2.fIdx, faceSmoothGeometry2, intListList))
						{
							foreach (int ordinal in intListList)
							{
								foreach (int faceCell in mesh.FaceCells[ordinal])
								{
									if (faceCell >= 0)
									{
										enumerableSet.Add(faceCell);
									}
								}
							}
							continue;
						}
						condition = true;
						if (FaceIsExact(mesh, cmesh, he.fIdx) || !assumeSimpleValid)
						{
							intList.Clear();
							intList.Add(he2.fIdx);
							ChaseForeignConflicts(he.fIdx, faceSmoothGeometry, mesh.HalfEdgeToOppositeCell(he2), he2.fIdx, intList);
							intSet.Add(he.fIdx);
							foreach (int ordinal2 in intList)
							{
								LogConflict(he.fIdx, ordinal2);
							}
						}
						if (!FaceIsExact(mesh, cmesh, he2.fIdx) && assumeSimpleValid)
						{
							continue;
						}
						intList.Clear();
						intList.Add(he.fIdx);
						ChaseForeignConflicts(he2.fIdx, faceSmoothGeometry2, mesh.HalfEdgeToOppositeCell(he), he.fIdx, intList);
						intSet.Add(he2.fIdx);
						foreach (int ordinal3 in intList)
						{
							LogConflict(he2.fIdx, ordinal3);
						}
					}
				}
				if (condition)
				{
					continue;
				}
				bool condition2 = false;
				double cellSimpleArea = 0.0;
				for (int k = 0; k < mesh.CellFaces[enumerable2].Count; k++)
				{
					HalfEdge halfEdge = mesh.CellFaces[enumerable2][k];
					Polyline pline = mesh.FaceSmoothLine[halfEdge.fIdx];
					if (pline == null || pline.Count < 3)
					{
						continue;
					}
					if (!condition2)
					{
						condition2 = true;
						cellSimpleArea = mesh.CellSimpleArea(enumerable2);
						if (cellSimpleArea <= 0.0)
						{
							Point pnt = mesh.NodePoints[mesh.CellToNodes(enumerable2)[0]];
							res.AddMessage(MeshGenerationMessageCodes.GetErrorGeneratedTopology(pnt));
							break;
						}
						if (mesh.CellSmoothArea(enumerable2) > 0.0)
						{
							break;
						}
					}
					double area = Polygon.ComputeArea(pline.PointCount, (int index) => pline[index]);
					if (halfEdge.orient == 1)
					{
						area *= -1.0;
					}
					if (!(area > cellSimpleArea))
					{
						continue;
					}
					condition = true;
					if (!FaceIsExact(mesh, cmesh, halfEdge.fIdx))
					{
						mesh.FaceSmoothLine[halfEdge.fIdx] = mesh.FaceSimpleGeometry(halfEdge.fIdx);
						foreach (int faceCell2 in mesh.FaceCells[halfEdge.fIdx])
						{
							if (faceCell2 >= 0)
							{
								enumerableSet.Add(faceCell2);
							}
						}
						break;
					}
					intList.Clear();
					int count = mesh.CellFaces[enumerable2].Count;
					intList.Add(mesh.CellFaces[enumerable2][(k + count - 1) % count].fIdx);
					intList.Add(mesh.CellFaces[enumerable2][(k + 1) % count].fIdx);
					for (int intervalIndex = 0; intervalIndex < 2; intervalIndex++)
					{
						int nIdx = mesh.FaceNodes[halfEdge.fIdx][intervalIndex];
						foreach (int nodeToCellIndex in mesh.NodeToCells(nIdx))
						{
							if (nodeToCellIndex != enumerable2)
							{
								ChaseForeignConflicts(halfEdge.fIdx, pline, nodeToCellIndex, -1, intList);
							}
						}
					}
					foreach (int ordinal4 in intList)
					{
						LogConflict(halfEdge.fIdx, ordinal4);
					}
					break;
				}
			}
			enumerable = enumerableSet;
		}
		conflictsToRepair = conflictSet.ToList();
		void ChaseForeignConflicts(int fIdx, Polyline polyline, int cIdx, int fOmitIdx, List<int> fIdxConflicts)
		{
			if (cIdx >= 0)
			{
				for (int l = 0; l < mesh.CellFaces[cIdx].Count; l++)
				{
					HalfEdge he3 = mesh.CellFaces[cIdx][l];
					if (fOmitIdx != he3.fIdx && !fIdxConflicts.Contains(he3.fIdx) && he3.fIdx != fIdx)
					{
						Polyline faceSmoothGeometry3 = mesh.GetFaceSmoothGeometry(he3.fIdx);
						if (Polyline.Conflict(polyline, faceSmoothGeometry3, polylineConflictEpsilon))
						{
							fIdxConflicts.Add(he3.fIdx);
							ChaseForeignConflicts(fIdx, polyline, mesh.HalfEdgeToOppositeCell(he3), he3.fIdx, fIdxConflicts);
						}
					}
				}
			}
		}
		void LogConflict(int fIdxA, int fIdxB)
		{
			if (FaceIsExact(mesh, cmesh, fIdxB) || (fIdxB < fIdxA && !FaceIsExact(mesh, cmesh, fIdxA)))
			{
				int fIdxB2 = fIdxB;
				fIdxB = fIdxA;
				fIdxA = fIdxB2;
			}
			(int, int) fIdxAFIdxBTerm = (fIdxA, fIdxB);
			conflictSet.Add(fIdxAFIdxBTerm);
		}
	}

	protected void LogConflicts(List<Clearing> clearings, OperationResult res)
	{
		for (int i = 0; i < clearings.Count; i++)
		{
			if (i >= 5)
			{
				int clearingsTerm = clearings.Count - i;
				res.AddMessage(new Message($"{clearingsTerm} additional conflict locations not reported"));
				break;
			}
			res.AddMessage(RepairMultipointConflicts.MessageCodes.GetRepairWarning(clearings[i].mesh.NodePoints[0]));
		}
	}

	public static bool FaceIsExact(SlidingMesh mesh, ConceptualMesh cmesh, int fIdx)
	{
		int faceConstraint = (int)mesh.FaceConstraints[fIdx];
		if (faceConstraint >= 0)
		{
			return cmesh.FaceDiscretization[mesh.constraints[faceConstraint].ArcIdx].followExactly;
		}
		return false;
	}

	private static bool MaybeResolveBySimplifying(SlidingMesh mesh, ConceptualMesh cmesh, int fIdxA, Polyline plineA, int fIdxB, Polyline plineB, List<int> modifiedFaces)
	{
		bool faceIsExact = FaceIsExact(mesh, cmesh, fIdxA);
		bool faceIsExact2 = FaceIsExact(mesh, cmesh, fIdxB);
		if (!faceIsExact && !faceIsExact2)
		{
			ResolveBySimplifying(mesh, fIdxA, plineA, fIdxB, plineB, modifiedFaces);
			return true;
		}
		if (faceIsExact && !faceIsExact2)
		{
			return false;
		}
		if (faceIsExact2 && !faceIsExact)
		{
			return false;
		}
		throw new Exception("Conflict between two constrained faces shouldn't be possible");
	}

	private static void ResolveBySimplifying(SlidingMesh mesh, int fIdxA, Polyline plineA, int fIdxB, Polyline plineB, List<int> modifiedFaces)
	{
		if (plineA.PointCount == 2)
		{
			mesh.FaceSmoothLine[fIdxB] = mesh.FaceSimpleGeometry(fIdxB);
			modifiedFaces.Add(fIdxB);
		}
		else
		{
			mesh.FaceSmoothLine[fIdxA] = mesh.FaceSimpleGeometry(fIdxA);
			modifiedFaces.Add(fIdxA);
		}
	}

	protected List<Clearing> ClearAndRefineConflictsKeepMesh(bool moveConstrainedToClearing, int buffer, OperationResult res)
	{
		SimplifyMultipointConflicts(mesh, cmesh, out var conflictsToRepair, attemptResolve: true, assumeSimpleValid: false, res);
		SlidingMesh allClearings = new SlidingMesh();
		allClearings.constraints = mesh.constraints;
		HashSet<HalfEdge> allExposed = new HashSet<HalfEdge>();
		Dictionary<int, int> nodeMap = new Dictionary<int, int>();
		Dictionary<int, int> faceMap = new Dictionary<int, int>();
		foreach (var conflictsToRepair2 in conflictsToRepair)
		{
			if (moveConstrainedToClearing)
			{
				MoveFaceToClearings(conflictsToRepair2.Item1);
			}
			MoveFaceToClearings(conflictsToRepair2.Item2);
		}
		for (int i = 0; i < buffer; i++)
		{
			foreach (HalfEdge allExposed2 in allExposed.ToList())
			{
				if (mesh.FaceDeleted[allExposed2.fIdx])
				{
					allExposed.Remove(allExposed2);
				}
				else if (mesh.FaceCells[allExposed2.fIdx].A < 0 && mesh.FaceCells[allExposed2.fIdx].B < 0)
				{
					if (!faceMap.ContainsKey(allExposed2.fIdx))
					{
						throw new Exception("Bad bookkeeping in repair");
					}
					mesh.DeleteFaceAndNakedNodes(allExposed2.fIdx);
				}
				else if (mesh.FaceConstraints[allExposed2.fIdx] != ConstraintState.FIXED)
				{
					MoveFaceToClearings(allExposed2.fIdx);
				}
			}
		}
		foreach (HalfEdge allExposed3 in allExposed)
		{
			if (mesh.FaceDeleted[allExposed3.fIdx])
			{
				continue;
			}
			if (mesh.FaceCells[allExposed3.fIdx].A < 0 && mesh.FaceCells[allExposed3.fIdx].B < 0)
			{
				if (!faceMap.ContainsKey(allExposed3.fIdx))
				{
					throw new Exception("Bad bookkeeping in repair");
				}
				mesh.DeleteFaceAndNakedNodes(allExposed3.fIdx);
				continue;
			}
			allClearings.FaceConstraints[faceMap[allExposed3.fIdx]] = ConstraintState.FIXED;
			allClearings.SetFaceCell(faceMap[allExposed3.fIdx], (byte)(1 - allExposed3.orient), -2);
			foreach (int faceNode in mesh.FaceNodes[allExposed3.fIdx])
			{
				allClearings.NodeConstraints[nodeMap[faceNode]] = ConstraintState.FIXED;
			}
		}
		List<Clearing> clearings = new List<Clearing>();
		if (allClearings.CellCount != 0)
		{
			foreach (SlidingMesh part in allClearings.Split(splitTangents: true))
			{
				Clearing clearing = new Clearing
				{
					cmesh = cmesh,
					mesh = part
				};
				clearings.Add(clearing);
			}
		}
		return clearings;
		int MoveFaceToClearings(int fIdx, bool delete = true)
		{
			if (mesh.FaceDeleted[fIdx])
			{
				return -1;
			}
			Duple<int> duple = mesh.FaceNodes[fIdx];
			if (!faceMap.TryGetValue(fIdx, out var faceMap2))
			{
				Span<int> intSpan = stackalloc int[2];
				for (int j = 0; j < 2; j++)
				{
					int duple2 = duple[j];
					int nodeMap2 = -1;
					if (!nodeMap.TryGetValue(duple2, out nodeMap2))
					{
						nodeMap2 = allClearings.AddNode(mesh.NodePoints[duple2]);
						allClearings.NodeConstraints[nodeMap2] = mesh.NodeConstraints[duple2];
						allClearings.NodePointOnConstraint[nodeMap2] = mesh.NodePointOnConstraint[duple2];
						nodeMap[duple2] = nodeMap2;
					}
					intSpan[j] = nodeMap2;
				}
				HalfEdge halfEdge = allClearings.NodesToHalfEdge(intSpan[0], intSpan[1]);
				faceMap2 = ((!halfEdge.IsNull()) ? halfEdge.fIdx : allClearings.AddFace(intSpan[0], intSpan[1]));
				faceMap[fIdx] = faceMap2;
				allClearings.FaceSmoothLine[faceMap2] = mesh.FaceSmoothLine[fIdx]?.Clone();
			}
			if (delete)
			{
				for (int k = 0; k < 2; k++)
				{
					int faceCell = mesh.FaceCells[fIdx][k];
					if (faceCell >= 0)
					{
						int cIdx = allClearings.AddCell();
						foreach (HalfEdge cellFace in mesh.CellFaces[faceCell])
						{
							if (!faceMap.TryGetValue(cellFace.fIdx, out var moveFaceToClearings))
							{
								moveFaceToClearings = MoveFaceToClearings(cellFace.fIdx, delete: false);
							}
							allClearings.CellAddFace(cIdx, new HalfEdge(moveFaceToClearings, cellFace.orient));
						}
					}
				}
				List<HalfEdge> halfEdges = new List<HalfEdge>();
				mesh.DeleteFaceCascade(fIdx, halfEdges);
				foreach (int duple3 in duple)
				{
					if (mesh.NodeDegree(duple3) == 0)
					{
						mesh.DeleteNode(duple3);
					}
				}
				allClearings.FaceConstraints[faceMap2] = mesh.FaceConstraints[fIdx];
				allExposed.AddRange(halfEdges);
			}
			else
			{
				allClearings.FaceConstraints[faceMap2] = mesh.FaceConstraints[fIdx];
			}
			return faceMap2;
		}
	}

	public void RepairGlobalBySliding(List<(int, int)> conflictsToRepair)
	{
		_ = conflictsToRepair.Count;
	}
}

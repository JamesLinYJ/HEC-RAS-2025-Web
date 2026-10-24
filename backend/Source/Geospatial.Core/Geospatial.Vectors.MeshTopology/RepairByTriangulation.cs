using System;
using System.Collections.Generic;
using System.Linq;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

internal class RepairByTriangulation : RepairProcess
{
	public class RepairClearingState
	{
		private Clearing clearing;

		private ISizeFunction sizeFunction;

		public double minNormalizedPerimeterRatio = 0.05;

		public DynamicTINFactory<NativePredicates> DTin;

		public List<int> meshNodeToTIN;

		private Dictionary<(int, int, int), bool> conflictCache;

		private SlidingMesh mesh => clearing.mesh;

		public RepairClearingState(Clearing clearing, ISizeFunction sizeFunction)
		{
			this.clearing = clearing;
			this.sizeFunction = sizeFunction;
		}

		public void InitializeTin()
		{
			try
			{
				DTin = mesh.TriangulateMesh<NativePredicates>((int nIdx) => nIdx);
			}
			catch (MeshTriangulationError meshTriangulationError)
			{
				throw new StructuredException(RepairMultipointConflicts.MessageCodes.GetClearingTinError(meshTriangulationError.Location));
			}
			meshNodeToTIN = new List<int>(mesh.NodeCount);
			for (int ordinal = 0; ordinal < mesh.NodeCount; ordinal++)
			{
				meshNodeToTIN.Add(-1);
			}
			Tin tin = DTin.Tin;
			for (int meshNodeToTIN2 = 8; meshNodeToTIN2 < tin.Count; meshNodeToTIN2++)
			{
				int index = (int)tin.Z[meshNodeToTIN2];
				meshNodeToTIN[index] = meshNodeToTIN2;
			}
			int validNodeCountMeshTerm = 8 + mesh.ValidNodeCount();
			if (tin.Count == validNodeCountMeshTerm)
			{
				return;
			}
			Point pt = Point.NullPoint;
			if (tin.Count < validNodeCountMeshTerm)
			{
				HashSet<int> intSet = new HashSet<int>();
				for (int ordinal2 = 8; ordinal2 < tin.Count; ordinal2++)
				{
					intSet.Add((int)tin.Z[ordinal2]);
				}
				foreach (int nodeIterator in mesh.NodeIterator())
				{
					if (!intSet.Contains(nodeIterator))
					{
						pt = mesh.NodePoints[nodeIterator];
						break;
					}
				}
			}
			throw new StructuredException(RepairMultipointConflicts.MessageCodes.GetClearingTinError(pt));
		}

		public void Simplify()
		{
			SlidingMesh slidingMesh = clearing.mesh;
			conflictCache = new Dictionary<(int, int, int), bool>();
			InitializeTin();
			TinEditLog tinEditLog = new TinEditLog();
			PriorityDictionary<int, double> priorityDictionary = new PriorityDictionary<int, double>(-1, double.MaxValue);
			foreach (int nodeIterator in slidingMesh.NodeIterator())
			{
				if (slidingMesh.NodeConstraints[nodeIterator] >= (ConstraintState)0 && slidingMesh.NodeDegree(nodeIterator) == 2)
				{
					priorityDictionary.Add(nodeIterator, ArcNodeScore(nodeIterator, slidingMesh, sizeFunction));
				}
			}
			bool condition = true;
			List<(int, double)> valueTuples = new List<(int, double)>();
			List<int> intList = new List<int>(2);
			while (condition)
			{
				valueTuples.Clear();
				condition = false;
				int elt;
				double priority;
				while (priorityDictionary.TryDequeue(out elt, out priority))
				{
					intList.Clear();
					slidingMesh.NodeToNodes(elt, intList);
					if (MaybeRemoveArcNode(elt, tinEditLog))
					{
						condition = true;
						break;
					}
					valueTuples.Add((elt, priority));
				}
				if (!condition)
				{
					continue;
				}
				foreach (var localValue in valueTuples)
				{
					priorityDictionary.Add(localValue.Item1, localValue.Item2);
				}
				foreach (int ordinal in intList)
				{
					if (priorityDictionary.Contains(ordinal))
					{
						priorityDictionary.Add(ordinal, ArcNodeScore(ordinal, slidingMesh, sizeFunction));
					}
				}
			}
			CopyTinFacesToMesh();
			UpdateMultipoint();
		}

		public void CopyTinFacesToMesh()
		{
			Tin tin = DTin.Tin;
			List<TriangleDefinition> triangleDefinition = tin.TriangleDefinition;
			for (int i = 0; i < tin.TriangleCount; i++)
			{
				if (!triangleDefinition[i].IsDeleted)
				{
					TriangleDefinition triangleDefinition2 = triangleDefinition[i];
					int tinP0Term = (int)tin.Z[triangleDefinition2.p0];
					int tinP1Term = (int)tin.Z[triangleDefinition2.p1];
					int tinP2Term = (int)tin.Z[triangleDefinition2.p2];
					HalfEdge he = mesh.NodesToHalfEdge(tinP0Term, tinP1Term);
					if (he.IsNull() || mesh.HalfEdgeToCell(he) != -1)
					{
						mesh.AddCellFromNodes(new List<int> { tinP0Term, tinP1Term, tinP2Term }, ElementMode.CREATE_OR_EXISTING);
					}
				}
			}
		}

		public void UpdateMultipoint()
		{
			foreach (int faceIterator in mesh.FaceIterator())
			{
				if (mesh.FaceSmoothLine[faceIterator] == null)
				{
					mesh.FaceSmoothLine[faceIterator] = mesh.ComputeSubFaceGeometry(faceIterator, constrained: true, interior: false);
				}
			}
		}

		public bool MaybeRemoveArcNode(int arcNode, TinEditLog tinEditLog)
		{
			List<TriangleDefinition> triDefs = DTin.Tin.TriangleDefinition;
			int triStart = 0;
			DTin.Tin.FindTriangleContainingPoint(mesh.NodePoints[arcNode], ref triStart);
			List<HalfEdge> nodeToHalfEdges = mesh.NodeToHalfEdges(arcNode);
			int nA = mesh.HalfEdgeNodeForward(nodeToHalfEdges[0]);
			int nIdx = arcNode;
			int nB = mesh.HalfEdgeNodeForward(nodeToHalfEdges[1]);
			int pA = meshNodeToTIN[nA];
			int pB = meshNodeToTIN[nB];
			if (DTin.SegmentViolatesConstraints(pA, pB, ref triStart, meshNodeToTIN[nIdx]))
			{
				return false;
			}
			HalfEdge halfEdge = nodeToHalfEdges[0];
			ConstraintState faceConstraint = mesh.FaceConstraints[halfEdge.fIdx];
			EdgeDefinition origEdge = DTin.Tin.FindEdge(meshNodeToTIN[nIdx], meshNodeToTIN[nA], ref triStart);
			if (origEdge.IsNull())
			{
				throw new StructuredException(Message.GenericError("Error evaluating Arc/Node connectivity at or near {Coordinate}", mesh.NodePoints[arcNode]));
			}
			bool isDeleted = triDefs[origEdge.ATriangleIndex].IsDeleted;
			bool isDeleted2 = triDefs[origEdge.BTriangleIndex].IsDeleted;
			tinEditLog.Clear();
			DTin.UnconstrainEdge(meshNodeToTIN[nIdx], meshNodeToTIN[nA], ref triStart, tinEditLog);
			DTin.UnconstrainEdge(meshNodeToTIN[nIdx], meshNodeToTIN[nB], ref triStart, tinEditLog);
			DTin.DynDeletePoint(meshNodeToTIN[nIdx], ref triStart, tinEditLog, clearData: false);
			int origTinPointIdx = meshNodeToTIN[nIdx];
			meshNodeToTIN[nIdx] = -1;
			DTin.ConstrainEdge(meshNodeToTIN[nA], meshNodeToTIN[nB], isPerimeter: false, -1, ref triStart, tinEditLog);
			DTin.RepairIsDeleted(tinEditLog, ref triStart);
			DTin.RepairIsDeleted(origEdge, isDeleted, isDeleted2, meshNodeToTIN[nA], meshNodeToTIN[nB], ref triStart);
			if (minNormalizedPerimeterRatio > 0.0)
			{
				double computed = 0.219346;
				double maxValue = double.MaxValue;
				foreach (Tuple<int, int, int> createdTriangle in tinEditLog.CreatedTriangles)
				{
					double perimeterRatio = new Triangle(DTin.Tin.Points[createdTriangle.Item1], DTin.Tin.Points[createdTriangle.Item2], DTin.Tin.Points[createdTriangle.Item3]).PerimeterRatio;
					maxValue = Math.Min(maxValue, perimeterRatio);
				}
				if (maxValue < computed * minNormalizedPerimeterRatio)
				{
					bool condition = true;
					foreach (Tuple<int, int, int> key2 in tinEditLog.DeletedTriangles.Keys)
					{
						if (new Triangle(DTin.Tin.Points[key2.Item1], DTin.Tin.Points[key2.Item2], DTin.Tin.Points[key2.Item3]).PerimeterRatio <= maxValue)
						{
							condition = false;
							break;
						}
					}
					if (condition)
					{
						RevertTin();
						return false;
					}
				}
			}
			mesh.DeleteNodeCascade(nIdx, out var deletedFaces);
			int fIdx = mesh.AddFace(nA, nB);
			mesh.FaceConstraints[fIdx] = faceConstraint;
			foreach (Tuple<int, int, int> createdTriangle2 in tinEditLog.CreatedTriangles)
			{
				int[] intValues = new int[3] { createdTriangle2.Item1, createdTriangle2.Item2, createdTriangle2.Item3 };
				(int, int, int) key;
				if (intValues[0] < intValues[1] && intValues[0] < intValues[2])
				{
					key = (intValues[0], intValues[1], intValues[2]);
				}
				else
				{
					key = ((intValues[1] >= intValues[0] || intValues[1] >= intValues[2]) ? (intValues[2], intValues[0], intValues[1]) : (intValues[1], intValues[2], intValues[0]));
				}
				if (conflictCache.TryGetValue(key, out var localValue))
				{
					if (localValue)
					{
						RevertMesh();
						RevertTin();
						return false;
					}
					continue;
				}
				for (int i = 0; i < 3; i++)
				{
					int ordinal = intValues[i];
					int ordinal2 = intValues[(i + 1) % 3];
					if (ordinal < 8 || ordinal2 < 8)
					{
						continue;
					}
					double tinDTinTerm = DTin.Tin.Z[ordinal];
					double tinDTinTerm2 = DTin.Tin.Z[ordinal2];
					int ordinal3 = (int)tinDTinTerm;
					int ordinal4 = (int)tinDTinTerm2;
					if (mesh.NodeConstraints[ordinal3] != ConstraintState.FREE && mesh.NodeConstraints[ordinal4] != ConstraintState.FREE)
					{
						int fIdx2 = mesh.NodesToHalfEdge(ordinal3, ordinal4).fIdx;
						if (fIdx2 >= 0 && mesh.FaceConstraints[fIdx2] >= (ConstraintState)0 && TinArcConflicts(fIdx2, ref triStart))
						{
							conflictCache[key] = true;
							RevertMesh();
							RevertTin();
							return false;
						}
					}
				}
				conflictCache[key] = false;
			}
			DTin.Tin.Points[origTinPointIdx] = new Point(double.NaN, double.NaN);
			return true;
			void RevertMesh()
			{
				mesh.DeleteFace(fIdx);
				mesh.UndeleteNode(nIdx);
				foreach (int deletedFaceIndex in deletedFaces)
				{
					mesh.UndeleteFace(deletedFaceIndex);
				}
			}
			void RevertTin()
			{
				EdgeDefinition origEdge2 = DTin.Tin.FindEdge(meshNodeToTIN[nA], meshNodeToTIN[nB], ref triStart);
				bool isDeleted3 = triDefs[origEdge2.ATriangleIndex].IsDeleted;
				bool isDeleted4 = triDefs[origEdge2.BTriangleIndex].IsDeleted;
				DTin.UnconstrainEdge(meshNodeToTIN[nA], meshNodeToTIN[nB], ref triStart);
				DTin.DynInsertPoint(mesh.NodePoints[nIdx], ref triStart, translatePoint: true, out var p);
				DTin.Tin.Z[p] = nIdx;
				DTin.Tin.Points[origTinPointIdx] = new Point(double.NaN, double.NaN);
				meshNodeToTIN[nIdx] = p;
				DTin.ConstrainEdge(meshNodeToTIN[nIdx], meshNodeToTIN[nA], isPerimeter: false, -1, ref triStart);
				DTin.ConstrainEdge(meshNodeToTIN[nIdx], meshNodeToTIN[nB], isPerimeter: false, -1, ref triStart);
				DTin.RepairIsDeleted(origEdge2, isDeleted3, isDeleted4, meshNodeToTIN[nIdx], meshNodeToTIN[nA], ref triStart);
				DTin.RepairIsDeleted(origEdge2, isDeleted3, isDeleted4, meshNodeToTIN[nIdx], meshNodeToTIN[nB], ref triStart);
			}
		}

		private bool TinArcConflicts(int arcFIdx, ref int triStart)
		{
			int meshNodeToTIN2 = meshNodeToTIN[mesh.FaceNodes[arcFIdx].A];
			int meshNodeToTIN3 = meshNodeToTIN[mesh.FaceNodes[arcFIdx].B];
			Polyline polyline = mesh.ComputeSubFaceGeometry(arcFIdx);
			if (polyline.Count == 2)
			{
				return DTin.Tin.FindEdge(meshNodeToTIN2, meshNodeToTIN3, ref triStart).IsNull();
			}
			return TinEdgeConflicts(meshNodeToTIN2, meshNodeToTIN3, polyline, ref triStart);
		}

		public bool TinEdgeConflicts(int pFrom, int pTo, Polyline multipoint, ref int tri)
		{
			Tin tin = DTin.Tin;
			EdgeDefinition edgeDefinition = tin.FindEdge(pFrom, pTo, ref tri);
			if (edgeDefinition.IsNull())
			{
				return true;
			}
			List<TriangleDefinition> triangleDefinition = tin.TriangleDefinition;
			TriangleDefinition triangleDefinition2 = triangleDefinition[edgeDefinition.ATriangleIndex];
			TriangleDefinition triangleDefinition3 = triangleDefinition[edgeDefinition.BTriangleIndex];
			return !TrianglePairCovers(new Point[4]
			{
				tin.Points[triangleDefinition3.EdgePointB((edgeDefinition.BEdge012 + 1) % 3)],
				tin.Points[triangleDefinition3.EdgePointA((edgeDefinition.BEdge012 + 1) % 3)],
				tin.Points[triangleDefinition2.EdgePointB((edgeDefinition.AEdge012 + 1) % 3)],
				tin.Points[triangleDefinition2.EdgePointA((edgeDefinition.AEdge012 + 1) % 3)]
			}, multipoint);
		}

		public static bool TrianglePairCovers(Point[] points, Polyline pline, double tolerance = 1E-05)
		{
			Triangle[] triangles = new Triangle[2]
			{
				new Triangle(points[1], points[3], points[2]),
				new Triangle(points[3], points[1], points[0])
			};
			Segment seg = new Segment(points[1], points[3]);
			int ordinal = -1;
			int unity = -1;
			BarycentricWeights barycentricWeights = default;
			for (int i = 0; i < pline.Count; i++)
			{
				Point pt = pline[i];
				if (triangles[0].Contains(pt, out var weights))
				{
					unity = 0;
				}
				else
				{
					if (!triangles[1].Contains(pt, out weights))
					{
						return false;
					}
					unity = 1;
				}
				if (i > 0 && ordinal != unity && !new Segment(pline[i - 1], pline[i]).Intersects(seg) && barycentricWeights.Weight2 > tolerance && weights.Weight2 > tolerance)
				{
					return false;
				}
				ordinal = unity;
				barycentricWeights = weights;
			}
			return true;
		}
	}

	public static bool Process(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts = false)
	{
		return new RepairByTriangulation(mesh, sizeFunction, cmesh, logConflicts).Process(logConflicts);
	}

	private RepairByTriangulation(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts)
		: base(mesh, sizeFunction, cmesh)
	{
		base.sizeFunction = sizeFunction;
	}

	private bool Process(bool logConflicts)
	{
		OperationResult res = new OperationResult();
		List<Clearing> clearAndRefineConflicts = ClearAndRefineConflicts(res);
		if (clearAndRefineConflicts.Count == 0)
		{
			return true;
		}
		if (logConflicts)
		{
			LogConflicts(clearAndRefineConflicts, res);
		}
		List<int> intList = new List<int>();
		HashSet<int> intSet = new HashSet<int>();
		Dictionary<int, int> arcToConstraint = null;
		for (int i = 0; i < clearAndRefineConflicts.Count; i++)
		{
			Clearing clearing = clearAndRefineConflicts[i];
			new RepairClearingState(clearing, sizeFunction).Simplify();
			mesh.MergeMesh(clearing.mesh, out var nodeMap, out var faceMap, out var _, ref arcToConstraint);
			intSet.AddRange(nodeMap);
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
		PostMergeFaces postMergeFaces = new PostMergeFaces(mesh, sizeFunction);
		postMergeFaces.nodes = intSet.ToList();
		postMergeFaces.enableMerge = true;
		postMergeFaces.enableCollapse = true;
		postMergeFaces.faceScaleFactor = 0.0;
		postMergeFaces.OptimizeAdaptive(2 * intSet.Count);
		return false;
	}

	private List<Clearing> ClearAndRefineConflicts(OperationResult res)
	{
		List<Clearing> clearings = new List<Clearing>();
		int CLEARING_BASE = -100;
		List<int> intList = new List<int>();
		Dictionary<int, ArcFace> deletedArcFaces = new Dictionary<int, ArcFace>();
		RepairProcess.SimplifyMultipointConflicts(mesh, cmesh, out var conflictsToRepair, attemptResolve: true, assumeSimpleValid: true, res);
		foreach (var conflictsToRepair2 in conflictsToRepair)
		{
			intList.Add(conflictsToRepair2.Item1);
			intList.Add(conflictsToRepair2.Item2);
		}
		HashSet<HalfEdge> allExposed = new HashSet<HalfEdge>();
		foreach (int ordinal in intList)
		{
			if (!mesh.FaceDeleted[ordinal])
			{
				DeleteAndTrack(ordinal);
			}
		}
		allExposed.RemoveWhere((HalfEdge he) =>
		{
			if (mesh.FaceDeleted[he.fIdx])
			{
				return true;
			}
			if (mesh.FaceCells[he.fIdx].A <= CLEARING_BASE && mesh.FaceCells[he.fIdx].B <= CLEARING_BASE)
			{
				DeleteAndTrack(he.fIdx);
				return true;
			}
			return false;
		});
		for (int ordinal2 = 0; ordinal2 < 10; ordinal2++)
		{
			List<HalfEdge> halfEdges = allExposed.Where((HalfEdge he) =>
			{
				Polyline polyline = null;
				if (mesh.FaceConstraints[he.fIdx] < (ConstraintState)0)
				{
					polyline = mesh.FaceSmoothLine[he.fIdx];
					if (polyline != null)
					{
						_ = polyline.PointCount;
						_ = 2;
					}
					return false;
				}
				polyline = mesh.ComputeSubFaceGeometry(he.fIdx);
				if (polyline.PointCount == 2)
				{
					return false;
				}
				int halfEdgeToOppositeCell = mesh.HalfEdgeToOppositeCell(he);
				if (halfEdgeToOppositeCell <= CLEARING_BASE)
				{
					return true;
				}
				if (halfEdgeToOppositeCell < 0)
				{
					return true;
				}
				if (Polygon.ComputeArea(polyline.PointCount, (int i) => polyline[i]) * (double)(1 - 2 * he.orient) > 0.0)
				{
					return true;
				}
				foreach (LineIntersection allIntersection in polyline.GetAllIntersections(mesh.FaceSegment(he.fIdx)))
				{
					if (allIntersection.BIntersection.Fraction > 0.0 && allIntersection.BIntersection.Fraction < 1.0)
					{
						return true;
					}
				}
				return false;
			}).ToList();
			if (halfEdges.Count == 0)
			{
				break;
			}
			foreach (HalfEdge halfEdge3 in halfEdges)
			{
				DeleteAndTrack(halfEdge3.fIdx);
			}
			allExposed.RemoveWhere((HalfEdge he) =>
			{
				if (mesh.FaceDeleted[he.fIdx])
				{
					return true;
				}
				if (mesh.FaceCells[he.fIdx].A <= CLEARING_BASE && mesh.FaceCells[he.fIdx].B <= CLEARING_BASE)
				{
					DeleteAndTrack(he.fIdx);
					return true;
				}
				return false;
			});
		}
		List<List<HalfEdge>> halfEdgeListList = new List<List<HalfEdge>>();
		while (allExposed.Count > 0)
		{
			HalfEdge halfEdge = allExposed.First();
			List<HalfEdge> halfEdgesList = new List<HalfEdge>();
			allExposed.Remove(halfEdge);
			halfEdgesList.Add(halfEdge);
			HalfEdge halfEdge2 = halfEdge;
			for (int faceCount = mesh.FaceCount; faceCount >= 0; faceCount--)
			{
				halfEdge2 = mesh.HalfEdgeNextByNode(halfEdge2);
				if (halfEdge2 == halfEdgesList[0])
				{
					break;
				}
				bool allExposedHalfEdge2Term = allExposed.Remove(halfEdge2);
				if (DynamicMesh.EnhancedMeshConsistencyChecks && !allExposedHalfEdge2Term)
				{
					throw new Exception("Cycle detection error for F[" + halfEdge2.fIdx + "], near " + mesh.FaceMidPoint(halfEdge2.fIdx).ToParentheticalString());
				}
				halfEdgesList.Add(halfEdge2);
			}
			halfEdgeListList.Add(halfEdgesList);
		}
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		for (int intervalIndex = 0; intervalIndex < halfEdgeListList.Count; intervalIndex++)
		{
			List<HalfEdge> halfEdges2 = halfEdgeListList[intervalIndex];
			int cellToClearing = CellToClearing(mesh.HalfEdgeToCell(halfEdges2[0]));
			for (int unity = 1; unity < halfEdges2.Count; unity++)
			{
				MergeClearings(cellToClearing, CellToClearing(mesh.HalfEdgeToCell(halfEdges2[unity])));
			}
			cellToClearing = Canonical(cellToClearing);
			Clearing clearing = new Clearing();
			clearing.cmesh = cmesh;
			clearing.mesh = new SlidingMesh();
			clearing.mesh.constraints = mesh.constraints;
			foreach (HalfEdge halfEdge4 in halfEdges2)
			{
				if (!deletedArcFaces.ContainsKey(halfEdge4.fIdx))
				{
					AddPolyline(clearing, mesh.FaceSimpleGeometry(halfEdge4.fIdx), ConstraintState.FIXED, (halfEdge4.orient == 0) ? (-99) : (-1), (halfEdge4.orient == 0) ? (-1) : (-99));
				}
			}
			clearings.Add(clearing);
			intByInt[cellToClearing] = intervalIndex;
		}
		foreach (KeyValuePair<int, ArcFace> deletedArcFace in deletedArcFaces)
		{
			int key = deletedArcFace.Key;
			ArcFace arcFace = deletedArcFace.Value;
			if (!mesh.FaceDeleted[key])
			{
				Duple<int> duple = mesh.FaceNodes[key];
				mesh.DeleteFace(key);
				mesh.DeleteNakedNodes(duple);
			}
			if (mesh.FaceConstraints[key] >= (ConstraintState)0)
			{
				int key2 = Canonical(key);
				Clearing clearing2 = clearings[intByInt[key2]];
				AddPolyline(clearing2, arcFace.polyline, mesh.FaceConstraints[key]);
			}
		}
		return clearings;
		int Canonical(int clearIdx)
		{
			for (int faceCount2 = mesh.FaceCount; faceCount2 > 0; faceCount2--)
			{
				int clearingIdx = deletedArcFaces[clearIdx].clearingIdx;
				if (clearingIdx == clearIdx)
				{
					return clearIdx;
				}
				clearIdx = clearingIdx;
			}
			throw new DynamicMeshException("Clearing index resolution failed");
		}
		int CellToClearing(int cIdx)
		{
			return CLEARING_BASE - cIdx;
		}
		int ClearingToCell(int fIdx)
		{
			return CLEARING_BASE - fIdx;
		}
		void DeleteAndTrack(int fIdx)
		{
			int fIdx2 = fIdx;
			if (mesh.FaceCells[fIdx].A <= CLEARING_BASE)
			{
				fIdx2 = CellToClearing(mesh.FaceCells[fIdx].A);
				if (mesh.FaceCells[fIdx].B <= CLEARING_BASE)
				{
					MergeClearings(fIdx2, CellToClearing(mesh.FaceCells[fIdx].B));
				}
			}
			else if (mesh.FaceCells[fIdx].B <= CLEARING_BASE)
			{
				fIdx2 = CellToClearing(mesh.FaceCells[fIdx].B);
			}
			ArcFace deletedArcFaces2 = new ArcFace(fIdx, fIdx2);
			if (mesh.FaceConstraints[fIdx] >= (ConstraintState)0)
			{
				deletedArcFaces2.polyline = mesh.ComputeSubFaceGeometry(fIdx);
			}
			deletedArcFaces[fIdx] = deletedArcFaces2;
			List<int> readOnlyListList = null;
			List<HalfEdge> halfEdges3 = new List<HalfEdge>();
			if (IsBoundary(mesh.FaceCells[fIdx].A))
			{
				if (mesh.FaceCells[fIdx].B >= 0)
				{
					mesh.DeleteCell(mesh.FaceCells[fIdx].B, halfEdges3);
				}
			}
			else if (IsBoundary(mesh.FaceCells[fIdx].B))
			{
				if (mesh.FaceCells[fIdx].A >= 0)
				{
					mesh.DeleteCell(mesh.FaceCells[fIdx].A, halfEdges3);
				}
			}
			else
			{
				ConstraintState constraintState = mesh.FaceConstraints[fIdx];
				Duple<int> duple2 = mesh.FaceNodes[fIdx];
				mesh.DeleteFaceCascade(fIdx, halfEdges3);
				foreach (int duple22 in duple2)
				{
					IReadOnlyList<int> readOnlyList = mesh.NodeToEdges(duple22);
					if (readOnlyList.Count == 0)
					{
						mesh.DeleteNode(duple22);
					}
					else if (constraintState >= (ConstraintState)0 && mesh.NodeConstraints[duple22] == constraintState)
					{
						bool condition = true;
						foreach (int readOnlyList2 in readOnlyList)
						{
							if (readOnlyList2 != fIdx && mesh.FaceConstraints[readOnlyList2] == constraintState)
							{
								condition = false;
								break;
							}
						}
						if (condition)
						{
							if (readOnlyListList == null)
							{
								readOnlyListList = new List<int>(readOnlyList);
							}
							else
							{
								readOnlyListList.AddRange(readOnlyList);
							}
						}
					}
				}
			}
			allExposed.AddRange(halfEdges3);
			foreach (HalfEdge halfEdge5 in halfEdges3)
			{
				mesh.SetFaceCell(halfEdge5, ClearingToCell(fIdx2));
			}
			if (readOnlyListList != null)
			{
				foreach (int readOnlyList3 in readOnlyListList)
				{
					DeleteAndTrack(readOnlyList3);
				}
			}
		}
		bool IsBoundary(int cIdx)
		{
			if (cIdx < 0)
			{
				return cIdx > CLEARING_BASE;
			}
			return false;
		}
		void MergeClearings(int clearA, int clearB)
		{
			clearA = Canonical(clearA);
			clearB = Canonical(clearB);
			if (clearA != clearB)
			{
				ArcFace deletedArcFace2 = deletedArcFaces[clearB];
				deletedArcFace2.clearingIdx = clearA;
				deletedArcFaces[clearB] = deletedArcFace2;
			}
		}
	}

	private static double ArcNodeScore(int arcNode, SlidingMesh mesh, ISizeFunction sizeFunction)
	{
		List<HalfEdge> nodeToHalfEdges = mesh.NodeToHalfEdges(arcNode);
		int index = mesh.HalfEdgeNodeForward(nodeToHalfEdges[0]);
		int index2 = mesh.HalfEdgeNodeForward(nodeToHalfEdges[1]);
		Point point = mesh.NodePoints[arcNode];
		double result = Math.Abs(Triangle.ComputeArea(mesh.NodePoints[index], mesh.NodePoints[arcNode], mesh.NodePoints[index2]));
		if (sizeFunction != null)
		{
			double query = sizeFunction.Query(point);
			if (mesh.NodePoints[index].DistanceTo(mesh.NodePoints[index2]) > 1.5 * query)
			{
				result = double.MaxValue;
			}
		}
		return result;
	}

	public void AddPolyline(Clearing clearing, Polyline polyline, ConstraintState constraint, int cellA = -99, int cellB = -99)
	{
		SlidingMesh slidingMesh = clearing.mesh;
		int orAddNode = clearing.FindOrAddNode(polyline[0], constraint, checkFixed: true);
		Polyline polyline2 = null;
		if (constraint >= (ConstraintState)0)
		{
			int arcIdx = slidingMesh.constraints[(int)constraint].ArcIdx;
			polyline2 = cmesh.FaceSmoothLine[arcIdx];
			if (slidingMesh.NodePoints[orAddNode] == polyline2.First() || slidingMesh.NodePoints[orAddNode] == polyline2.Last())
			{
				slidingMesh.NodeConstraints[orAddNode] = ConstraintState.FIXED;
			}
		}
		int orAddNode2 = orAddNode;
		for (int i = 1; i < polyline.Count; i++)
		{
			int orAddNode3 = clearing.FindOrAddNode(polyline[i], constraint, i + 1 == polyline.Count);
			int index = slidingMesh.AddFace(orAddNode2, orAddNode3, cellA, cellB);
			slidingMesh.FaceConstraints[index] = constraint;
			orAddNode2 = orAddNode3;
		}
		if (polyline2 != null && (slidingMesh.NodePoints[orAddNode2] == polyline2.First() || slidingMesh.NodePoints[orAddNode2] == polyline2.Last()))
		{
			slidingMesh.NodeConstraints[orAddNode2] = ConstraintState.FIXED;
		}
	}
}

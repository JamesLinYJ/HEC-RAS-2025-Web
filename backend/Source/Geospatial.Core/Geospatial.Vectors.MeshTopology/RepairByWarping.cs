using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Extensions;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

internal class RepairByWarping : RepairProcess
{
	public static bool Process(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts = false)
	{
		return new RepairByWarping(mesh, sizeFunction, cmesh, logConflicts).Process(logConflicts);
	}

	private RepairByWarping(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts)
		: base(mesh, sizeFunction, cmesh)
	{
	}

	private bool Process(bool logConflicts)
	{
		OperationResult res = new OperationResult();
		List<Clearing> clearAndRefineConflictsKeepMesh = ClearAndRefineConflictsKeepMesh(moveConstrainedToClearing: false, 0, res);
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

	public static bool Simplify(Clearing clearing)
	{
		SlidingMesh slidingMesh = clearing.mesh;
		SlidingMesh warpee = new SlidingMesh();
		Dictionary<int, int> intByInt = new Dictionary<int, int>();
		Dictionary<int, (int, int)> valueTupleByInt = new Dictionary<int, (int, int)>();
		foreach (int nodeIterator in slidingMesh.NodeIterator())
		{
			int nodeIteratorAddNodeTerm = (intByInt[nodeIterator] = warpee.AddNode(slidingMesh.NodePoints[nodeIterator]));
			valueTupleByInt[nodeIteratorAddNodeTerm] = (nodeIterator, -1);
			warpee.NodeConstraints[nodeIteratorAddNodeTerm] = slidingMesh.NodeConstraints[nodeIterator];
		}
		foreach (int faceIterator in slidingMesh.FaceIterator())
		{
			int addNodeTerm = intByInt[slidingMesh.FaceNodes[faceIterator].A];
			if (slidingMesh.FaceConstraints[faceIterator] == ConstraintState.FIXED)
			{
				warpee.NodeConstraints[addNodeTerm] = ConstraintState.FIXED;
			}
			Point point = slidingMesh.NodePoints[slidingMesh.FaceNodes[faceIterator].A];
			Point point2 = slidingMesh.NodePoints[slidingMesh.FaceNodes[faceIterator].B];
			Polyline polyline = slidingMesh.GetFaceSmoothGeometry(faceIterator);
			int count = 60;
			if (polyline.Count == 2 && slidingMesh.FaceConstraints[faceIterator] != ConstraintState.FIXED)
			{
				Polyline dst = null;
				DiscretizeLine.DiscretizeByCount(polyline, ref dst, count, 1.0);
				slidingMesh.FaceSmoothLine[faceIterator] = dst;
				polyline = dst;
			}
			double[] cumulativeSegmentLengths = polyline.CumulativeSegmentLengths();
			for (int i = 1; i < polyline.Count - 1; i++)
			{
				double cumulativeSegmentLengthsSegmentCountTerm = cumulativeSegmentLengths[i - 1] / cumulativeSegmentLengths[polyline.SegmentCount - 1];
				Point point3 = (1.0 - cumulativeSegmentLengthsSegmentCountTerm) * point + cumulativeSegmentLengthsSegmentCountTerm * point2;
				int addNode = warpee.AddNode(point3);
				if (slidingMesh.FaceConstraints[faceIterator] == ConstraintState.FIXED)
				{
					warpee.NodeConstraints[addNode] = ConstraintState.FIXED;
				}
				valueTupleByInt[addNode] = (faceIterator, i);
				warpee.AddFace(addNodeTerm, addNode);
				addNodeTerm = addNode;
			}
			warpee.AddFace(addNodeTerm, intByInt[slidingMesh.FaceNodes[faceIterator].B]);
		}
		Point[] nodePoints = warpee.NodePoints.ToArray();
		CycleTree cycleTree = CycleTree.CreateCycleTree(warpee);
		foreach (KeyValuePair<int, (int, int)> localValue in valueTupleByInt)
		{
			int key = localValue.Key;
			int ordinal = localValue.Value.Item1;
			int ordinal2 = localValue.Value.Item2;
			if (ordinal2 >= 0)
			{
				warpee.NodePoints[key] = slidingMesh.FaceSmoothLine[ordinal][ordinal2];
			}
		}
		List<int> nodes = cycleTree.children[0].nodes;
		double scaledLengthOverCount = 0.1 * cycleTree.children[0].poly.Length / (double)nodes.Count;
		ConceptualMesh warpedCMesh = new ConceptualMesh();
		List<int> intList = nodes.Select((int index) => warpedCMesh.AddNode(warpee.NodePoints[index])).ToList();
		intList.Reverse();
		warpedCMesh.AddCellFromNodes(intList, ElementMode.CREATE);
		ConstantSizeFunction constantSizeFunction = new ConstantSizeFunction(scaledLengthOverCount);
		foreach (int faceIterator2 in warpedCMesh.FaceIterator())
		{
			Polyline polyline2 = warpedCMesh.FaceSimpleGeometry(faceIterator2);
			DiscretizeLine discretizeLine = DiscretizeLine.BySize(scaledLengthOverCount, 1.0, polyline2.Length);
			Polyline dst2 = null;
			discretizeLine.Apply(polyline2, ref dst2);
			int node = nodes[warpedCMesh.FaceNodes[faceIterator2].A];
			int node2 = nodes[warpedCMesh.FaceNodes[faceIterator2].B];
			Point point4 = nodePoints[node];
			Point point5 = nodePoints[node2];
			double[] doubleValues = new double[dst2.Count];
			double[] doubleValuesArray = new double[dst2.Count];
			double[] cumulativePointStations = dst2.CumulativePointStations();
			for (int intervalIndex = 0; intervalIndex < dst2.Count; intervalIndex++)
			{
				if (intervalIndex == 0)
				{
					doubleValues[intervalIndex] = point4.X;
					doubleValuesArray[intervalIndex] = point4.Y;
					continue;
				}
				if (intervalIndex == dst2.Count - 1)
				{
					doubleValues[intervalIndex] = point5.X;
					doubleValuesArray[intervalIndex] = point5.Y;
					continue;
				}
				double cumulativePointStationsTerm = cumulativePointStations[intervalIndex] / cumulativePointStations.Last();
				Point point6 = point4 + cumulativePointStationsTerm * (point5 - point4);
				doubleValues[intervalIndex] = point6.X;
				doubleValuesArray[intervalIndex] = point6.Y;
			}
			dst2.TrackZ(doubleValues);
			dst2.TrackM(doubleValuesArray);
			warpedCMesh.FaceNodeString[faceIterator2] = dst2;
		}
		TrianglePaver trianglePaver = new TrianglePaver(warpedCMesh, 0);
		trianglePaver.sizeFunction = constantSizeFunction;
		trianglePaver.expandMBR = 5.0;
		trianglePaver.Generate();
		Tin tin = trianglePaver.DTin.Tin;
		TinLaplacian tinLaplacian = new TinLaplacian(tin);
		double[] doubleValues2 = new double[tin.Count];
		double[] doubleValues3 = new double[tin.Count];
		for (int intervalIndex2 = 0; intervalIndex2 < tin.Count; intervalIndex2++)
		{
			doubleValues2[intervalIndex2] = (doubleValues3[intervalIndex2] = double.NaN);
		}
		int t = 0;
		foreach (int faceIterator3 in warpedCMesh.FaceIterator())
		{
			Polyline polyline3 = warpedCMesh.FaceNodeString[faceIterator3];
			for (int intervalIndex3 = 0; intervalIndex3 < polyline3.Count; intervalIndex3++)
			{
				Point point7 = polyline3[intervalIndex3];
				tin.FindTriangleAndNearestPointIndex(point7, ref t, out var p);
				doubleValues2[p] = polyline3.Z[intervalIndex3];
				doubleValues3[p] = polyline3.M[intervalIndex3];
			}
		}
		tinLaplacian.SolveLaplacian(doubleValues2);
		tinLaplacian.SolveLaplacian(doubleValues3);
		tin.TrackZ(doubleValues2.AsSpan());
		tin.TrackM(doubleValues3.AsSpan());
		QuadTransformLaplacian quadTransformLaplacian = new QuadTransformLaplacian(warpedCMesh, 0, tin);
		quadTransformLaplacian.ClipInputs = false;
		foreach (int nodeIterator2 in warpee.NodeIterator())
		{
			if (warpee.NodeConstraints[nodeIterator2] == ConstraintState.FREE)
			{
				Point[] points = new Point[1];
				Point[] pointsArray = new Point[1];
				points[0] = nodePoints[nodeIterator2];
				quadTransformLaplacian.Transform(points, pointsArray);
				warpee.NodePoints[nodeIterator2] = pointsArray[0];
			}
		}
		double maxValue = double.MaxValue;
		foreach (int cellIterator in warpee.CellIterator())
		{
			maxValue = Math.Min(maxValue, warpee.CellSimpleArea(cellIterator));
		}
		if (maxValue > 0.0)
		{
			foreach (KeyValuePair<int, (int, int)> localValue2 in valueTupleByInt)
			{
				int key2 = localValue2.Key;
				int ordinal3 = localValue2.Value.Item1;
				int ordinal4 = localValue2.Value.Item2;
				if (ordinal4 >= 0)
				{
					slidingMesh.FaceSmoothLine[ordinal3][ordinal4] = warpee.NodePoints[key2];
				}
			}
		}
		return true;
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
		int orAddNode2 = clearing.FindOrAddNode(polyline[polyline.Count - 1], constraint, checkFixed: true);
		int index = slidingMesh.AddFace(orAddNode, orAddNode2, cellA, cellB);
		if (polyline2 != null && (slidingMesh.NodePoints[orAddNode2] == polyline2.First() || slidingMesh.NodePoints[orAddNode2] == polyline2.Last()))
		{
			slidingMesh.NodeConstraints[orAddNode2] = ConstraintState.FIXED;
		}
		slidingMesh.FaceSmoothLine[index] = polyline.Clone();
	}
}

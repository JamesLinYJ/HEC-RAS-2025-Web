using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class PostRepave
{
	public static void DoRepave(SlidingMesh mesh, ConceptualMesh cmesh, int regionIdx, double stretchRate, double allowance = 0.4)
	{
		double[] doubleValues = new double[mesh.NodeCount];
		double[] doubleValuesArray = new double[mesh.NodeCount];
		double[] doubleValues2 = new double[mesh.NodeCount];
		double[] doubleValues3 = new double[mesh.NodeCount];
		InitializePointScale(mesh, doubleValues, doubleValuesArray, doubleValues2, doubleValues3, allowance);
		FillByTelescoping(mesh, 0.0 - stretchRate, doubleValues, doubleValues2);
		FillByTelescoping(mesh, stretchRate, doubleValuesArray, doubleValues3);
		ResolveConflicts(mesh, doubleValues, doubleValuesArray, doubleValues2, doubleValues3);
		HashSet<HalfEdge> origPerimeter = mesh.ExternalHalfEdges();
		ClearCellsBySize(mesh, doubleValues, doubleValuesArray, stretchRate);
		mesh.DeleteNakedFaces(deleteConstrained: false);
		mesh.DeleteNakedNodes();
		mesh.FillGaps(null, null, origPerimeter);
	}

	private static void InitializePointScale(SlidingMesh mesh, double[] minSize, double[] maxSize, double[] minSizeDistance, double[] maxSizeDistance, double allowance)
	{
		Dictionary<int, Tuple<int, double>> valueTupleByInt = new Dictionary<int, Tuple<int, double>>();
		foreach (int faceIterator in mesh.FaceIterator())
		{
			ConstraintState constraintState = mesh.FaceConstraints[faceIterator];
			if (constraintState != ConstraintState.FIXED && (constraintState < (ConstraintState)0 || mesh.constraints[(int)constraintState].mode != VirtualConstraints.ConstraintMode.FIXED))
			{
				continue;
			}
			double faceLength = mesh.FaceLength(faceIterator);
			foreach (int faceNode in mesh.FaceNodes[faceIterator])
			{
				if (!valueTupleByInt.ContainsKey(faceNode))
				{
					valueTupleByInt[faceNode] = new Tuple<int, double>(0, 0.0);
				}
				Tuple<int, double> tuple = valueTupleByInt[faceNode];
				valueTupleByInt[faceNode] = new Tuple<int, double>(tuple.Item1 + 1, tuple.Item2 + faceLength);
			}
		}
		foreach (int nodeIterator in mesh.NodeIterator())
		{
			minSize[nodeIterator] = double.NaN;
			maxSize[nodeIterator] = double.NaN;
			minSizeDistance[nodeIterator] = double.MaxValue;
			maxSizeDistance[nodeIterator] = double.MaxValue;
		}
		foreach (int key in valueTupleByInt.Keys)
		{
			Tuple<int, double> tuple2 = valueTupleByInt[key];
			double tuple2Term = tuple2.Item2 / (double)tuple2.Item1;
			minSize[key] = tuple2Term * (1.0 - allowance);
			maxSize[key] = tuple2Term * (1.0 + allowance);
			minSizeDistance[key] = 0.0;
			maxSizeDistance[key] = 0.0;
		}
	}

	private static void FillByTelescoping(SlidingMesh mesh, double stretchRate, double[] newZ, double[] pathDistance)
	{
		int sign = double.Sign(stretchRate);
		HashSet<int> visited = new HashSet<int>();
		PriorityQueue<int, double> priorityQueue = new PriorityQueue<int, double>();
		foreach (int nodeIterator in mesh.NodeIterator())
		{
			if (GetNodeState(nodeIterator) == TinSizeFunction.NodeState.FIXED)
			{
				visited.Add(nodeIterator);
				Visit(nodeIterator);
			}
			else if (double.IsFinite(newZ[nodeIterator]))
			{
				priorityQueue.Enqueue(nodeIterator, (double)sign * newZ[nodeIterator]);
			}
		}
		int element;
		double priority;
		while (priorityQueue.TryDequeue(out element, out priority))
		{
			if (!(priority > (double)sign * newZ[element]))
			{
				visited.Add(element);
				Visit(element);
			}
		}
		double newZ2 = ((sign > 0) ? 100000000.0 : 0.0);
		foreach (int nodeIterator2 in mesh.NodeIterator())
		{
			if (double.IsNaN(newZ[nodeIterator2]))
			{
				newZ[nodeIterator2] = newZ2;
			}
		}
		TinSizeFunction.NodeState GetNodeState(int nIdx)
		{
			if (!double.IsFinite(newZ[nIdx]) || pathDistance[nIdx] != 0.0)
			{
				return TinSizeFunction.NodeState.FREE;
			}
			return TinSizeFunction.NodeState.FIXED;
		}
		void Visit(int nIdx)
		{
			_ = mesh.NodePoints[nIdx];
			foreach (HalfEdge nodeToHalfEdge in mesh.NodeToHalfEdges(nIdx))
			{
				int halfEdgeNodeForward = mesh.HalfEdgeNodeForward(nodeToHalfEdge);
				if (!visited.Contains(halfEdgeNodeForward) && GetNodeState(halfEdgeNodeForward) != TinSizeFunction.NodeState.FIXED)
				{
					double faceLength = mesh.FaceLength(nodeToHalfEdge.fIdx);
					double newZ3 = newZ[nIdx] + stretchRate * faceLength;
					if (double.IsNaN(newZ[halfEdgeNodeForward]) || (double)sign * newZ3 < (double)sign * newZ[halfEdgeNodeForward])
					{
						newZ[halfEdgeNodeForward] = newZ3;
						pathDistance[halfEdgeNodeForward] = pathDistance[nIdx] + faceLength;
						priorityQueue.Enqueue(halfEdgeNodeForward, (double)sign * newZ[halfEdgeNodeForward]);
					}
				}
			}
		}
	}

	private static void ResolveConflicts(SlidingMesh mesh, double[] minSize, double[] maxSize, double[] minSizeDistance, double[] maxSizeDistance)
	{
		foreach (int nodeIterator in mesh.NodeIterator())
		{
			if (maxSize[nodeIterator] < minSize[nodeIterator])
			{
				double minSizeDistanceNodeIteratorTerm = minSizeDistance[nodeIterator] + maxSizeDistance[nodeIterator];
				double minSizeDistanceNodeIteratorTerm2 = ((minSizeDistanceNodeIteratorTerm == 0.0) ? 0.5 : (minSizeDistance[nodeIterator] / minSizeDistanceNodeIteratorTerm));
				minSize[nodeIterator] = minSizeDistanceNodeIteratorTerm2 * maxSize[nodeIterator] + (1.0 - minSizeDistanceNodeIteratorTerm2) * minSize[nodeIterator];
				maxSize[nodeIterator] = minSize[nodeIterator];
			}
		}
	}

	private static void ClearCellsBySize(SlidingMesh mesh, double[] minSize, double[] maxSize, double stretchRate)
	{
		foreach (int faceIterator in mesh.FaceIterator())
		{
			double faceLength = mesh.FaceLength(faceIterator);
			double stretchRateMaxSizeTerm = (1.0 + stretchRate) * Math.Min(maxSize[mesh.FaceNodes[faceIterator].A], maxSize[mesh.FaceNodes[faceIterator].B]);
			double stretchRateMinSizeTerm = 1.0 / (1.0 + stretchRate) * Math.Max(minSize[mesh.FaceNodes[faceIterator].A], minSize[mesh.FaceNodes[faceIterator].B]);
			if (mesh.FaceConstraints[faceIterator] < (ConstraintState)0 && (!(faceLength > stretchRateMinSizeTerm) || !(faceLength < stretchRateMaxSizeTerm)))
			{
				mesh.DeleteFaceCascade(faceIterator);
			}
		}
	}
}

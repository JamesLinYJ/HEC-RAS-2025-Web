using System;
using System.Collections.Generic;
using System.Linq;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class RepairBySliding : RepairProcess
{
	public class MinimizeLocalNodeConflicts : OptimizeNodeMetric
	{
		public MinimizeLocalNodeConflicts(SlidingMesh mesh, List<int> nodes)
			: base(mesh, nodes)
		{
		}

		public override double NodeLoss(int nIdx, Point pnt)
		{
			ConstraintState constraintState = mesh.NodeConstraints[nIdx];
			Polyline polyline = mesh.constraints[(int)constraintState].Polyline;
			List<HalfEdge> nodeToHalfEdges = mesh.NodeToHalfEdges(nIdx);
			double computed = 0.0;
			foreach (HalfEdge nodeToHalfEdge in nodeToHalfEdges)
			{
				if (mesh.FaceConstraints[nodeToHalfEdge.fIdx] == constraintState)
				{
					continue;
				}
				Segment seg = new Segment(pnt, mesh.NodePoints[mesh.HalfEdgeNodeForward(nodeToHalfEdge)]);
				IEnumerable<LineIntersection> allIntersections = polyline.GetAllIntersections(seg);
				double fractionBIntersectionTerm = 0.0;
				foreach (LineIntersection allIntersection in allIntersections)
				{
					fractionBIntersectionTerm = Math.Max(fractionBIntersectionTerm, allIntersection.BIntersection.Fraction);
				}
				computed += fractionBIntersectionTerm;
			}
			return computed;
		}
	}

	private RepairBySliding(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh)
		: base(mesh, sizeFunction, cmesh)
	{
	}

	public static bool Process(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, bool logConflicts = false)
	{
		OperationResult res = new OperationResult();
		RepairProcess.SimplifyMultipointConflicts(mesh, cmesh, out var conflictsToRepair, attemptResolve: true, assumeSimpleValid: false, res);
		return DoRepairs(mesh, sizeFunction, cmesh, conflictsToRepair);
	}

	private static bool DoRepairs(SlidingMesh mesh, ISizeFunction sizeFunction, ConceptualMesh cmesh, List<(int, int)> conflictsToRepair)
	{
		new OperationResult();
		HashSet<int> intSet = new HashSet<int>();
		foreach (var conflictsToRepair2 in conflictsToRepair)
		{
			int conflictsToRepair2Term = conflictsToRepair2.Item1;
			int conflictsToRepair2Term2 = conflictsToRepair2.Item2;
			int[] intValues = new int[2] { conflictsToRepair2Term, conflictsToRepair2Term2 };
			foreach (int index in intValues)
			{
				foreach (int faceNode in mesh.FaceNodes[index])
				{
					if (!mesh.NodeIsFixed(faceNode) && mesh.NodeConstraints[faceNode] >= (ConstraintState)0)
					{
						intSet.Add(faceNode);
					}
				}
			}
		}
		if (intSet.Count == 0)
		{
			return false;
		}
		new MinimizeLocalNodeConflicts(mesh, intSet.ToList()).OptimizeOnce();
		return false;
	}
}

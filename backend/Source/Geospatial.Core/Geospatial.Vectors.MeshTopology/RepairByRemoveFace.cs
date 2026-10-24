using System.Collections.Generic;
using System.Linq;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class RepairByRemoveFace : RepairProcess
{
	private RepairByRemoveFace(SlidingMesh mesh, ConceptualMesh cmesh, ISizeFunction sizeFunction)
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
		PostMergeFaces postMergeFaces = new PostMergeFaces(mesh, sizeFunction)
		{
			localIntersectionFactor = 5.0,
			maxSidesAllowed = 6,
			deltaCostThreshold = 4.0
		};
		HashSet<int> scaledCount = new HashSet<int>(4 * conflictsToRepair.Count);
		foreach (var (index, index2) in conflictsToRepair)
		{
			foreach (int faceNode in mesh.FaceNodes[index])
			{
				if (mesh.NodeConstraints[faceNode] != ConstraintState.FIXED)
				{
					scaledCount.Add(faceNode);
				}
			}
			foreach (int faceNode2 in mesh.FaceNodes[index2])
			{
				if (mesh.NodeConstraints[faceNode2] != ConstraintState.FIXED)
				{
					scaledCount.Add(faceNode2);
				}
			}
		}
		postMergeFaces.nodes = scaledCount.ToList();
		postMergeFaces.OptimizeAdaptive(conflictsToRepair.Count);
		return false;
	}
}

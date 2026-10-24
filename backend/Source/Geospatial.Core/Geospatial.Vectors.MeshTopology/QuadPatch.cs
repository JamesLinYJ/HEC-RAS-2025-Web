using System;
using System.Collections.Generic;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class QuadPatch : PatchCommon, IRegionGenerator
{
	public QuadPatch(ConceptualMesh mesh, int cIdx)
		: base(mesh, cIdx)
	{
	}

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		result = new SlidingMesh();
		SetSides();
		transform = CreateTransform();
		result.AddCartesian(fSouthNodes.Count, fWestNodes.Count, new Point(0.0, 0.0), new Point(1.0, 1.0));
		List<Point> nodePoints = new List<Point>(result.NodePoints);
		result.Transform(transform);
		SetTargetLengths(result, nodePoints, transform, Sides);
		SetConstraints(result, Sides, nodePoints);
		return result;
	}
}

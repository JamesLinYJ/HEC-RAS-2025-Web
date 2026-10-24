using System;
using System.Collections.Generic;

namespace Geospatial.Vectors.MeshTopology;

public class OptimizeNodeSkew : OptimizeNodeMetric
{
	protected int nIdxCached = -1;

	public OptimizeNodeSkew(SlidingMesh mesh, IList<int> nodes = null)
		: base(mesh, nodes)
	{
		snapTolerance = 0.01;
	}

	public override double NodeLoss(int nIdx, Point p)
	{
		double computed = 0.0;
		Point nodePoint = mesh.NodePoints[nIdx];
		mesh.NodePoints[nIdx] = p;
		foreach (int nodeToCellIndex in mesh.NodeToCells(nIdx))
		{
			computed += Math.Pow(MeshMetrics.CellSkewness(mesh, nodeToCellIndex), 3.0);
		}
		float nodeFaceLengthError = MeshMetrics.NodeFaceLengthError(mesh, nIdx);
		computed += 0.1 * (double)nodeFaceLengthError;
		mesh.NodePoints[nIdx] = nodePoint;
		return computed;
	}
}

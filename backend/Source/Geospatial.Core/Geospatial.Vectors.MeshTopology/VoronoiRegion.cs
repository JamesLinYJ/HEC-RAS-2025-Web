using System;
using System.Collections.Generic;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public class VoronoiRegion : IRegionGenerator
{
	public SlidingMesh result;

	public ConceptualMesh cmesh;

	public int regionIdx;

	public List<int> cmeshCellChildren;

	public SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null)
	{
		return null;
	}

	public ISizeFunction GetRegionSizeFunction(bool secondary)
	{
		return null;
	}

	public virtual PointCollection CellCenters()
	{
		return null;
	}
}

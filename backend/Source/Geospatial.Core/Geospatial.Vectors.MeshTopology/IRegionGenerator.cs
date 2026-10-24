using System;
using Utility.Logging;
using Utility.Progress;

namespace Geospatial.Vectors.MeshTopology;

public interface IRegionGenerator
{
	ISizeFunction GetRegionSizeFunction(bool secondary)
	{
		return null;
	}

	SlidingMesh Generate(Action<SlidingMesh> Progress = null, ProgressReporter reporter = null, OperationResult res = null);
}

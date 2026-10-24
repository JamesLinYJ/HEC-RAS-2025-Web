using System;
using Utility.Progress;

namespace Ras.Layers;

public class ComputePackage
{
	public Geometry Geometry { get; set; }

	public BoundaryCondition BoundaryConditions { get; set; }

	public Terrain Terrain { get; set; }

	public string OutputFilename { get; set; }

	public bool IsValid(Plan p, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		throw new NotImplementedException();
	}
}

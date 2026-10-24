using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;

namespace Ras.Synthetics;

public class NormalBCPlanarParams : NormalDepthParams
{
	public override BoundaryConditionLine GetDownstreamBC(BoundaryCondition bc, Polyline pl)
	{
		return BoundaryConditionLine.NormalDepth(bc, pl, Slope);
	}
}

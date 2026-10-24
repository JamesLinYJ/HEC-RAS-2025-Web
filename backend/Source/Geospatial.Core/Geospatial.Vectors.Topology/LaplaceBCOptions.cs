using Utility.Attributes;

namespace Geospatial.Vectors.Topology;

public enum LaplaceBCOptions
{
	Default,
	[EnumDisplayName("Perpendicular to Arc")]
	ZeroFlux,
	[EnumDisplayName("Parallel to Sides")]
	Dirichlet
}

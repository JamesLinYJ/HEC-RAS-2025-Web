using Geospatial.Vectors;

namespace Ras.Output;

public static class OutputVariables
{
	public static readonly OutputVariable Legacy_WaterSurface = new OutputVariable("Water Surface", d1: true, sa: true, d2: true, MeshElementType.Cell);

	public static readonly OutputVariable Depth = new OutputVariable("Cell Depth", d1: true, sa: true, d2: true, MeshElementType.Cell);

	public static readonly OutputVariable Velocity = new OutputVariable("Face Velocity", d1: true, sa: true, d2: true, MeshElementType.Face)
	{
		CrossSectionSpecificName = "Velocity Total"
	};

	public static readonly OutputVariable Flow = new OutputVariable("Face Flow", d1: true, sa: false, d2: false, MeshElementType.Face);
}

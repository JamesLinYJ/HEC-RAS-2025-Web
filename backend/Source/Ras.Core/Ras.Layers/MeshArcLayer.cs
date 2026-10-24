using System.Diagnostics;

namespace Ras.Layers;

[DebuggerDisplay("{Count} Arcs")]
public class MeshArcLayer : GeometryLayerBase<MeshArc>
{
	public const string HDF5GroupName = "/Geometry/Mesh Arcs";

	protected override string HDF5Group => "/Geometry/Mesh Arcs";

	public MeshArcLayer(Geometry geom)
		: base(geom)
	{
	}
}

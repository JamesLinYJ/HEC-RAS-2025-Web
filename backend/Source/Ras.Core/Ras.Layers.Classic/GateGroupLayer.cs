using System.Diagnostics;
using Ras.Hydraulics.Classic;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Gate Groups")]
public class GateGroupLayer : ClassicGeometryLayerBase<GateGroup>
{
	public static readonly string HDF5GroupName = "/Geometry/Structures/Gate Groups";

	protected override string HDF5Group => HDF5GroupName;

	public GateGroupLayer(ClassicGeometry geom)
		: base(geom)
	{
	}
}

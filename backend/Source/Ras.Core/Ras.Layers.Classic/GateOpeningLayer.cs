using System.Diagnostics;
using Ras.Hydraulics.Classic;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Gate Openings")]
public class GateOpeningLayer : ClassicGeometryLayerBase<GateOpening>
{
	public static readonly string HDF5GroupName = GateGroupLayer.HDF5GroupName + "/Openings";

	protected override string HDF5Group => HDF5GroupName;

	public GateOpeningLayer(ClassicGeometry geom)
		: base(geom)
	{
	}
}

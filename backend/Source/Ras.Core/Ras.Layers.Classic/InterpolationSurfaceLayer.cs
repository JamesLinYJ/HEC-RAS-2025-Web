using System.Diagnostics;
using Ras.Hydraulics;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Interpolation Surfaces")]
public class InterpolationSurfaceLayer : ClassicGeometryLayerBase<InterpolationSurface>
{
	public static readonly string HDF5GroupName = "/Geometry/Cross Section Interpolation Surfaces";

	protected override string HDF5Group => HDF5GroupName;

	internal InterpolationSurfaceLayer(ClassicGeometry geom)
		: base(geom)
	{
	}
}

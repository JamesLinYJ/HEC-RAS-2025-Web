using System.Diagnostics;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} River Edgelines")]
public class RiverEdgelineLayer : ClassicGeometryLayerBase<Edgeline>
{
	public const string HDF5GroupName = "/Geometry/River Edge Lines";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => "/Geometry/River Edge Lines";

	internal RiverEdgelineLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<Edgeline>(this, (Edgeline riv) => riv.Polyline, (Polyline pl) => new Edgeline
			{
				Polyline = pl
			});
		}
		return _plWrapper;
	}
}

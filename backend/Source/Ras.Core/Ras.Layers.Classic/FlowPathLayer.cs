using System.Diagnostics;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Flow-Path Lines")]
public class FlowPathLayer : ClassicGeometryLayerBase<FlowPathLine>
{
	public const string HDF5GroupName = "/Geometry/Flow Path Lines";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => "/Geometry/Flow Path Lines";

	public FlowPathLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<FlowPathLine>(this, (FlowPathLine riv) => riv.Polyline, (Polyline pl) => new FlowPathLine
			{
				Polyline = pl
			});
		}
		return _plWrapper;
	}
}

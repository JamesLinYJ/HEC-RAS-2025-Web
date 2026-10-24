using System.Diagnostics;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Breaklines")]
public class BreaklineLayer : ClassicGeometryLayerBase<Breakline>
{
	public const string HDF5GroupName = "/Geometry/2D Flow Area Break Lines";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => "/Geometry/2D Flow Area Break Lines";

	internal BreaklineLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<Breakline>(this, (Breakline riv) => riv.Polyline, (Polyline pl) => new Breakline
			{
				Polyline = pl
			});
		}
		return _plWrapper;
	}
}

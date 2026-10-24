using System.Diagnostics;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Layers.Classic;

[DebuggerDisplay("{Count} Banklines")]
public class BanklineLayer : ClassicGeometryLayerBase<Bankline>
{
	public const string HDF5GroupName = "/Geometry/River Bank Lines";

	private IPolylineCollection _plWrapper;

	protected override string HDF5Group => "/Geometry/River Bank Lines";

	internal BanklineLayer(ClassicGeometry geom)
		: base(geom)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineFtLayerWrapped<Bankline>(this, (Bankline riv) => riv.Polyline, (Polyline pl) => new Bankline
			{
				Polyline = pl
			});
		}
		return _plWrapper;
	}
}

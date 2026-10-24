using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class PolylineFeatureCollection : FeatureCollection<Polyline>
{
	private IPolylineCollection _plWrapper;

	public PolylineFeatureCollection(PolylineCollection pc, Table table)
		: base((IVectorCollection<Polyline>)pc, table)
	{
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineCollection(Features);
		}
		return _plWrapper;
	}
}

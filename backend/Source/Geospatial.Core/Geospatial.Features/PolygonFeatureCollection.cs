using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class PolygonFeatureCollection : FeatureCollection<Polygon>
{
	private IPolygonCollection _plWrapper;

	public PolygonFeatureCollection(PolygonCollection pc, Table table)
		: base((IVectorCollection<Polygon>)pc, table)
	{
	}

	public PolygonFeatureCollection()
		: base((IVectorCollection<Polygon>)new PolygonCollection(), new Table())
	{
	}

	public IPolygonCollection AsPolygonCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolygonCollection(Features);
		}
		return _plWrapper;
	}
}

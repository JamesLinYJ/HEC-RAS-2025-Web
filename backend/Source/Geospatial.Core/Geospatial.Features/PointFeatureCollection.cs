using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class PointFeatureCollection : FeatureCollection<Point>
{
	private PointCollection _plWrapper;

	public PointFeatureCollection(PointCollection pc, Table table)
		: base((IVectorCollection<Point>)pc, table)
	{
	}

	public PointCollection AsPointCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PointCollection(Features);
		}
		return _plWrapper;
	}
}

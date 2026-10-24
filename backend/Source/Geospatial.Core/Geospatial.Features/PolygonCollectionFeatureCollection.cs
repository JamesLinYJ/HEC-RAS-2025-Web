using System.Linq;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class PolygonCollectionFeatureCollection : FeatureCollection<PolygonCollection>
{
	private IPolygonCollection _plWrapper;

	public new PolygonCollectionCollection Features { get; init; }

	public PolygonCollectionFeatureCollection(PolygonCollectionCollection mpc, Table table)
		: base((IVectorCollection<PolygonCollection>)mpc, table)
	{
		Features = mpc;
	}

	public IPolygonCollection ToExplodedPolygonCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolygonCollection(Features.SelectMany((PolygonCollection f) => f));
		}
		return _plWrapper;
	}
}

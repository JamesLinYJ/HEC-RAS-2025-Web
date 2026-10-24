using System.Linq;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class PolylineCollectionFeatureCollection : FeatureCollection<PolylineCollection>
{
	private IPolylineCollection _plWrapper;

	public new PolylineCollectionCollection Features { get; init; }

	public PolylineCollectionFeatureCollection(PolylineCollectionCollection mpc, Table table)
		: base((IVectorCollection<PolylineCollection>)mpc, table)
	{
		Features = mpc;
	}

	public IPolylineCollection ToExplodedPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new PolylineCollection(Features.SelectMany((PolylineCollection f) => f));
		}
		return _plWrapper;
	}
}

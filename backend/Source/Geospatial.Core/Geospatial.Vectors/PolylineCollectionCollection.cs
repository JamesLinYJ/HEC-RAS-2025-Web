using System.Collections.Generic;

namespace Geospatial.Vectors;

public class PolylineCollectionCollection : VectorCollectionBase<PolylineCollection>
{
	public PolylineCollectionCollection()
	{
	}

	public PolylineCollectionCollection(PolylineCollection poly)
		: base(poly)
	{
	}

	public PolylineCollectionCollection(IEnumerable<PolylineCollection> plines)
		: base(plines)
	{
	}
}

using System.Collections.Generic;

namespace Geospatial.Vectors;

public class PointCollectionCollection : VectorCollectionBase<PointCollection>
{
	public PointCollectionCollection()
	{
	}

	public PointCollectionCollection(PointCollection pc)
		: base(pc)
	{
	}

	public PointCollectionCollection(IEnumerable<PointCollection> pcs)
		: base(pcs)
	{
	}
}

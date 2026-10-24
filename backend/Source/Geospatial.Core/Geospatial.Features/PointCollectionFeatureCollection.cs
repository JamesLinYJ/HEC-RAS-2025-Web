using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class PointCollectionFeatureCollection : FeatureCollection<PointCollection>
{
	public PointCollectionFeatureCollection(PointCollectionCollection mpc, Table table)
		: base((IVectorCollection<PointCollection>)mpc, table)
	{
	}
}

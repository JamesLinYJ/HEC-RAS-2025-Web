using System.Collections.Generic;
using System.Linq;
using Utility;

namespace Geospatial.Vectors;

public class PolygonCollectionCollection : VectorCollectionBase<PolygonCollection>
{
	private readonly Versioned<double> _areaCache = new Versioned<double>();

	public double TotalArea => _areaCache.Get(VersionXY, ComputeArea);

	public PolygonCollectionCollection()
	{
	}

	public PolygonCollectionCollection(PolygonCollection poly)
		: base(poly)
	{
	}

	public PolygonCollectionCollection(IEnumerable<PolygonCollection> polygons)
		: base(polygons)
	{
	}

	private double ComputeArea()
	{
		return _elements.Sum((PolygonCollection pg) => pg.TotalArea);
	}
}

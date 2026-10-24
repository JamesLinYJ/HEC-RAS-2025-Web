using System.Collections.Generic;
using System.Linq;
using Utility;

namespace Geospatial.Vectors;

public class ComplexPolygonCollectionCollection : VectorCollectionBase<ComplexPolygonCollection>
{
	private readonly Versioned<double> _areaCache = new Versioned<double>();

	public double TotalArea => _areaCache.Get(VersionXY, ComputeArea);

	public ComplexPolygonCollectionCollection()
	{
	}

	public ComplexPolygonCollectionCollection(ComplexPolygonCollection poly)
		: base(poly)
	{
	}

	public ComplexPolygonCollectionCollection(IEnumerable<ComplexPolygonCollection> polygons)
		: base(polygons)
	{
	}

	private double ComputeArea()
	{
		return _elements.Sum((ComplexPolygonCollection cpc) => cpc.ComputeArea());
	}
}

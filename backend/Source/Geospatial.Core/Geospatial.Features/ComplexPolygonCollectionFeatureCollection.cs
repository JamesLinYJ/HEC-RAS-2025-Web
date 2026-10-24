using System.Linq;
using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class ComplexPolygonCollectionFeatureCollection : FeatureCollection<ComplexPolygonCollection>
{
	private ComplexPolygonCollection _cpWrapper;

	public new ComplexPolygonCollectionCollection Features { get; init; }

	public ComplexPolygonCollectionFeatureCollection(ComplexPolygonCollectionCollection features, Table table)
		: base((IVectorCollection<ComplexPolygonCollection>)features, table)
	{
		Features = features;
	}

	public ComplexPolygonCollection ToExplodedComplexPolygonCollection()
	{
		if (_cpWrapper == null)
		{
			_cpWrapper = new ComplexPolygonCollection(Features.SelectMany((ComplexPolygonCollection f) => f));
		}
		return _cpWrapper;
	}
}

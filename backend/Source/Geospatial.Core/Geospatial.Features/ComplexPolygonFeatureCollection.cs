using Geospatial.Vectors;
using Utility.Memory;

namespace Geospatial.Features;

public class ComplexPolygonFeatureCollection : FeatureCollection<ComplexPolygon>
{
	public ComplexPolygonFeatureCollection(ComplexPolygonCollection pc, Table table)
		: base((IVectorCollection<ComplexPolygon>)pc, table)
	{
	}
}

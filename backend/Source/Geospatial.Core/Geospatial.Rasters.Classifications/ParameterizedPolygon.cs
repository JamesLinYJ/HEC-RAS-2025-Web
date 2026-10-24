using Geospatial.Vectors;

namespace Geospatial.Rasters.Classifications;

public class ParameterizedPolygon
{
	public Polygon Polygon { get; set; }

	public ParameterSet ParameterSet { get; set; }

	public ParameterizedPolygon(Polygon pgon, ParameterSet pset)
	{
		Polygon = pgon;
		ParameterSet = pset;
	}
}

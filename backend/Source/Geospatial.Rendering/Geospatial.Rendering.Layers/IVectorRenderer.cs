using System.Collections.Generic;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Layers;

public interface IVectorRenderer
{
	IPolygonProvider PolygonProvider { get; set; }

	ISegmentProvider LineProvider { get; set; }

	void Paint(IList<Point> pts, VectorSymbology symb, bool paintShadow = false);

	void Paint(Point pt, VectorSymbology symb, bool paintShadow = false);

	void Paint(Segment seg, VectorSymbology symb, bool paintShadow = false);

	void Paint(Polyline pl, VectorSymbology symb, bool paintShadow = false);
}

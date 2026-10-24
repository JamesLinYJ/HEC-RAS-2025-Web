using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public interface IPolylineRenderer
{
	bool Render<T, K>(PlotRequest request, K model, Func<int, K, Polyline> getPolyline, IList<FeatureSegmentSubset> candidates, VectorSymbology symbology) where T : Polyline;

	bool Render(PlotRequest request, IReadOnlyList<Segment> segments, VectorSymbology symbology);
}

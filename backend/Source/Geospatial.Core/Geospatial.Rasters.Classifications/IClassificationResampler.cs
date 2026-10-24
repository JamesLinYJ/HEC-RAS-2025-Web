using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters.Classifications;

public interface IClassificationResampler : IResample<int>, ISurface<int>
{
	IClassification Classifier { get; }

	List<Dictionary<int, double>> SampleClassifications(IList<Polygon> polygons);
}

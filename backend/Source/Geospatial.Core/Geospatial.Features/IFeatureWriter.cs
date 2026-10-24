using System.Collections.Generic;

namespace Geospatial.Features;

public interface IFeatureWriter : IFeatureReader
{
	void WriteMetadata(IFeatureLayer ftLayer);

	void WriteFeatures<T>(IList<T> features) where T : IFeatureRow;
}

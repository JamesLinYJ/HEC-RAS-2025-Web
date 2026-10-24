namespace Geospatial.Features;

public interface IFeatureReader
{
	void ReadMetadata(IFeatureLayer ftLayer);

	void ReadFeatures(IFeatureLayer ftLayer);
}

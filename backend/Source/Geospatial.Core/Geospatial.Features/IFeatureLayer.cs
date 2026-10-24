namespace Geospatial.Features;

public interface IFeatureLayer
{
	void Save(IFeatureWriter hw);

	bool NeedsSave();

	void EvictMemory();
}

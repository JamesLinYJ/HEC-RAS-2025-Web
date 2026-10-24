using H5Assist;

namespace Ras.Layers;

public interface IFeatureLayer
{
	bool NeedsSave();

	void Save(H5Writer hw, bool forceSaveIfFeaturesUnloaded = true);

	void LoadMetadata(H5Reader hr);

	int PeekCount(H5Reader hr);

	void EvictMemory();

	void RemoveAt(int index);
}

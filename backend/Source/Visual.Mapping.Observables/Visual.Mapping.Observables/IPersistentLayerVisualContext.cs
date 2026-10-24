using Utility.Hashing;

namespace Visual.Mapping.Observables;

public interface IPersistentLayerVisualContext : IDataHash
{
	bool IsDataLoaded { get; }

	bool NeedsSave();

	void Save();
}

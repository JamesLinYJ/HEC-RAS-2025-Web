using Utility.DataStructures;

namespace Visual.Mapping.Observables;

public interface ITaggedTopologyFeature
{
	IReadOnlyAdvancedCollection<ObservableTag> Tags { get; }

	void AddTag(ObservableTag tag);

	void RemoveTag(ObservableTag tag);
}

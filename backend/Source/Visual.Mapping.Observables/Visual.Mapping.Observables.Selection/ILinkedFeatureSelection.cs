using Visual.Observables.Linking;

namespace Visual.Mapping.Observables.Selection;

public interface ILinkedFeatureSelection : IFeatureSelection
{
	Linkage Linkage { get; }
}
public interface ILinkedFeatureSelection<T> : ILinkedFeatureSelection, IFeatureSelection, IFeatureSelection<T> where T : class
{
	void IFeatureSelection<T>.SetLastSelectedFeature(T feature)
	{
		LastSelectedFeature = feature;
		Linkage.CurrentContext = feature;
	}
}

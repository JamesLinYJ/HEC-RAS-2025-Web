namespace Ras.Observables;

public interface IObservableFeatureLayer
{
	void AlertFeatureAtDeleted(int index);

	void DeleteFeatureAt(int featureIdx);
}

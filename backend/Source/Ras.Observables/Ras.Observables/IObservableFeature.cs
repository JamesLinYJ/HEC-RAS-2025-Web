using Geospatial;

namespace Ras.Observables;

public interface IObservableFeature<T> where T : IFeatureRow, new()
{
	T BaseFeature { get; }
}

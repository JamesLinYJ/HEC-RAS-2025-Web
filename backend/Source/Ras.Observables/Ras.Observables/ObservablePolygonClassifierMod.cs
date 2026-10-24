using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Observables;

public class ObservablePolygonClassifierMod : ObservableObject, IObservableFeature<PolygonClassifierMod>
{
	public delegate void ClassificationChangedEvent(ObservablePolygonClassifierMod sender, string newVal);

	private PolygonClassifierMod _mod;

	public PolygonClassifierMod BaseFeature => _mod;

	public string Classification
	{
		get
		{
			return _mod.Classification;
		}
		set
		{
			if (SetProperty(_mod.Classification, value, _mod, (PolygonClassifierMod model, string newVal) =>
			{
				model.Classification = newVal;
			}, "Classification"))
			{
				ClassificationChanged?.Invoke(this, value);
			}
		}
	}

	public Polygon Polygon => _mod.Polygon;

	public event ClassificationChangedEvent ClassificationChanged;

	public ObservablePolygonClassifierMod(PolygonClassifierMod mod)
	{
		_mod = mod;
	}
}

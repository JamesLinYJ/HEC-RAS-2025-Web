using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Observables.Layers;

public class ObservablePolygonRasterMod : ObservableObject, IObservableRasterMod, IObservableFeature<PolygonRasterMod>
{
	private PolygonRasterMod _mod;

	public PolygonRasterMod Mod => _mod;

	public PolygonRasterMod BaseFeature => _mod;

	public RasterModType ModificationType
	{
		get
		{
			return _mod.ModificationType;
		}
		set
		{
			SetProperty(_mod.ModificationType, value, _mod, (PolygonRasterMod model, RasterModType newVal) =>
			{
				model.ModificationType = newVal;
			}, "ModificationType");
		}
	}

	public float Value
	{
		get
		{
			return _mod.Value;
		}
		set
		{
			SetProperty(_mod.Value, value, _mod, (PolygonRasterMod model, float newVal) =>
			{
				model.Value = newVal;
			}, "Value");
		}
	}

	public Polygon Polygon
	{
		get
		{
			return _mod.Polygon;
		}
		set
		{
			SetProperty(_mod.Polygon, value, _mod, (PolygonRasterMod model, Polygon newVal) =>
			{
				model.Polygon = newVal;
			}, "Polygon");
		}
	}

	public ObservablePolygonRasterMod(PolygonRasterMod mod)
	{
		_mod = mod;
	}
}

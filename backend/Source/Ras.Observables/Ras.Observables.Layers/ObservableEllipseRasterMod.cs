using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Rasters;
using Geospatial.Vectors;

namespace Ras.Observables.Layers;

public class ObservableEllipseRasterMod : ObservableObject, IObservableRasterMod, IObservableFeature<EllipseRasterMod>
{
	private EllipseRasterMod _mod;

	public EllipseRasterMod Mod => _mod;

	public EllipseRasterMod BaseFeature => _mod;

	public RasterModType ModificationType
	{
		get
		{
			return _mod.ModificationType;
		}
		set
		{
			SetProperty(_mod.ModificationType, value, _mod, (EllipseRasterMod model, RasterModType newVal) =>
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
			SetProperty(_mod.Value, value, _mod, (EllipseRasterMod model, float newVal) =>
			{
				model.Value = newVal;
			}, "Value");
		}
	}

	public Ellipse Ellipse
	{
		get
		{
			return _mod.Ellipse;
		}
		set
		{
			SetProperty(_mod.Ellipse, value, _mod, (EllipseRasterMod model, Ellipse newVal) =>
			{
				model.Ellipse = newVal;
			}, "Ellipse");
		}
	}

	public ObservableEllipseRasterMod(EllipseRasterMod mod)
	{
		_mod = mod;
	}
}

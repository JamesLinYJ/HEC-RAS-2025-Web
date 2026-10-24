using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableInterpolationSurface : ObservableObject, IObservableFeature<InterpolationSurface>
{
	private InterpolationSurface _surface;

	public InterpolationSurface BaseFeature => _surface;

	public Tin Tin
	{
		get
		{
			return _surface.Tin;
		}
		set
		{
			SetProperty(_surface.Tin, value, _surface, (InterpolationSurface r, Tin v) =>
			{
				r.Tin = v;
			}, "Tin");
		}
	}

	public ObservableInterpolationSurface(InterpolationSurface surface)
	{
		_surface = surface;
	}
}

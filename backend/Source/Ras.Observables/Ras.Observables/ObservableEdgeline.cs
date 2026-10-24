using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableEdgeline : ObservableObject, IObservableFeature<Edgeline>
{
	private Edgeline _edgeline;

	public Edgeline BaseFeature => _edgeline;

	public Polyline Polyline
	{
		get
		{
			return _edgeline.Polyline;
		}
		set
		{
			SetProperty(_edgeline.Polyline, value, _edgeline, (Edgeline u, Polyline n) =>
			{
				u.Polyline = n;
			}, "Polyline");
		}
	}

	public ObservableEdgeline(Edgeline edgeline)
	{
		_edgeline = edgeline;
	}
}

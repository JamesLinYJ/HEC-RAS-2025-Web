using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableBreakline : ObservableObject, IObservableFeature<Breakline>
{
	private Breakline _breakline;

	public Breakline BaseFeature => _breakline;

	public Polyline Polyline
	{
		get
		{
			return _breakline.Polyline;
		}
		set
		{
			SetProperty(_breakline.Polyline, value, _breakline, (Breakline r, Polyline v) =>
			{
				r.Polyline = v;
			}, "Polyline");
		}
	}

	public ObservableBreakline(Breakline breakline)
	{
		_breakline = breakline;
	}
}

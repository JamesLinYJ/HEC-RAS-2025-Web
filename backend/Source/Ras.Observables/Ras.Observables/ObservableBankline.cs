using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableBankline : ObservableObject, IObservableFeature<Bankline>
{
	private Bankline _bankline;

	public Bankline BaseFeature => _bankline;

	public Polyline Polyline
	{
		get
		{
			return _bankline.Polyline;
		}
		set
		{
			SetProperty(_bankline.Polyline, value, _bankline, (Bankline r, Polyline v) =>
			{
				r.Polyline = v;
			}, "Polyline");
		}
	}

	public ObservableBankline(Bankline bankline)
	{
		_bankline = bankline;
	}
}

using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableRiver : ObservableObject, IObservableFeature<River>
{
	private readonly River _river;

	public River BaseFeature => _river;

	public Polyline Polyline
	{
		get
		{
			return _river.Polyline;
		}
		set
		{
			SetProperty(_river.Polyline, value, _river, (River r, Polyline v) =>
			{
				r.Polyline = v;
			}, "Polyline");
		}
	}

	public string RiverName
	{
		get
		{
			return _river.RiverName;
		}
		set
		{
			SetProperty(_river.RiverName, value, _river, (River r, string v) =>
			{
				r.RiverName = v;
			}, "RiverName");
		}
	}

	public string ReachName
	{
		get
		{
			return _river.ReachName;
		}
		set
		{
			SetProperty(_river.ReachName, value, _river, (River r, string v) =>
			{
				r.ReachName = v;
			}, "ReachName");
		}
	}

	public ObservableRiver(River riv)
	{
		_river = riv;
	}
}

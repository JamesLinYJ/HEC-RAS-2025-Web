using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class ObservableFlowPathLine : ObservableObject, IObservableFeature<FlowPathLine>
{
	private FlowPathLine _flowPathLine;

	public FlowPathLine BaseFeature => _flowPathLine;

	public Polyline Polyline
	{
		get
		{
			return _flowPathLine.Polyline;
		}
		set
		{
			SetProperty(_flowPathLine.Polyline, value, _flowPathLine, (FlowPathLine r, Polyline v) =>
			{
				r.Polyline = v;
			}, "Polyline");
		}
	}

	public ObservableFlowPathLine(FlowPathLine flowPathLine)
	{
		_flowPathLine = flowPathLine;
	}
}

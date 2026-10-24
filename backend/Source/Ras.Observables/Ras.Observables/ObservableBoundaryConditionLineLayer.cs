using System;
using Geospatial.Vectors;
using Ras.Layers.BoundaryConditions;
using Utility;

namespace Ras.Observables;

public class ObservableBoundaryConditionLineLayer : ObservableFeatureLayer<BoundaryConditionLineLayer, BoundaryConditionLine, ObservableBoundaryConditionLine>
{
	private ObservableBoundaryCondition _bc;

	private IPolylineCollection _plWrapper;

	public ObservableBoundaryCondition BoundaryCondition
	{
		get
		{
			return _bc;
		}
		private set
		{
			SetProperty(ref _bc, value, "BoundaryCondition");
		}
	}

	public ObservableBoundaryConditionLineLayer(ObservableBoundaryCondition bc)
		: base(bc.BoundaryCondition.BoundaryConditionLines, (Func<BoundaryConditionLine, ObservableBoundaryConditionLine>)((BoundaryConditionLine bcLine) => new ObservableBoundaryConditionLine(bc, bcLine)))
	{
		BoundaryCondition = bc;
	}

	public IPolylineCollection AsPolylineCollection()
	{
		if (_plWrapper == null)
		{
			_plWrapper = new ObservablePolylineFtLayerWrapped<BoundaryConditionLineLayer, BoundaryConditionLine, ObservableBoundaryConditionLine>(this, (BoundaryConditionLine riv) => riv.Polyline, (Polyline pl) => new BoundaryConditionLine
			{
				Polyline = pl,
				Name = GetUniqueBCLineName(),
				BoundaryCondition = BoundaryCondition.BoundaryCondition
			});
		}
		return _plWrapper;
	}

	private string GetUniqueBCLineName()
	{
		return StringHelpers.NextUniqueName(BoundaryCondition.BCLineLayer.Features, (ObservableBoundaryConditionLine line) => line.Name, "Boundary Condition Line");
	}
}

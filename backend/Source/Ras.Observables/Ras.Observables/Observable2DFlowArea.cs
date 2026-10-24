using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Ras.Hydraulics;

namespace Ras.Observables;

public class Observable2DFlowArea : ObservableObject, IObservableFeature<FlowArea>
{
	private readonly FlowArea _fa;

	public FlowArea FlowArea => _fa;

	public FlowArea BaseFeature => _fa;

	public string Name
	{
		get
		{
			return _fa.Name;
		}
		set
		{
			SetProperty(_fa.Name, value, _fa, (FlowArea fa, string v) =>
			{
				fa.Name = v;
			}, "Name");
		}
	}

	public Mesh Mesh => FlowArea.Mesh;

	public PointCollection CellCenters
	{
		get
		{
			return FlowArea.CellCenters;
		}
		set
		{
			SetProperty(FlowArea.CellCenters, value, FlowArea, (FlowArea u, PointCollection n) =>
			{
				u.CellCenters = n;
			}, "CellCenters");
		}
	}

	public Polygon Perimeter
	{
		get
		{
			return FlowArea.Perimeter;
		}
		set
		{
			SetProperty(FlowArea.Perimeter, value, FlowArea, (FlowArea u, Polygon n) =>
			{
				u.Perimeter = n;
			}, "Perimeter");
		}
	}

	public double TargetedCellSpacing
	{
		get
		{
			return FlowArea.TargetedCellSpacing;
		}
		set
		{
			SetProperty(FlowArea.TargetedCellSpacing, value, FlowArea, (FlowArea u, double n) =>
			{
				u.TargetedCellSpacing = n;
			}, "TargetedCellSpacing");
		}
	}

	public int CellCount => _fa.CellCount;

	public Observable2DFlowArea(FlowArea fa)
	{
		_fa = fa;
	}

	public override string ToString()
	{
		return Name;
	}
}

using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers.BoundaryConditions;
using Utility.Validation;

namespace Ras.Observables;

public class ObservableFlowHydroParams : ObservableValidator
{
	private readonly FlowHydroParams _flowParams;

	public FlowHydroParams FlowHydroParams => _flowParams;

	[NotNegative]
	public float QMin
	{
		get
		{
			return _flowParams.QMin;
		}
		set
		{
			SetProperty(_flowParams.QMin, value, _flowParams, (FlowHydroParams model, float localValue) =>
			{
				model.QMin = localValue;
			}, "QMin");
		}
	}

	[GreaterThanZero]
	public float QMult
	{
		get
		{
			return _flowParams.QMult;
		}
		set
		{
			SetProperty(_flowParams.QMult, value, _flowParams, (FlowHydroParams r, float v) =>
			{
				r.QMult = v;
			}, "QMult");
		}
	}

	public float QSlope
	{
		get
		{
			return _flowParams.QSlope;
		}
		set
		{
			SetProperty(_flowParams.QSlope, value, _flowParams, (FlowHydroParams r, float v) =>
			{
				r.QSlope = v;
			}, "QSlope");
		}
	}

	public ObservableFlowHydroParams(FlowHydroParams flowParams)
	{
		_flowParams = flowParams;
	}
}

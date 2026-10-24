using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Hydraulics.HydraulicDesign;

namespace Ras.Observables;

public class ObservableTractiveForceMethod : ObservableValidator
{
	private TractiveForceMethod _tractiveForce;

	private ObservableTractiveForceMethodPart _leftBank;

	private ObservableTractiveForceMethodPart _rightBank;

	private ObservableTractiveForceMethodPart _bed;

	public TractiveForceMethod Model => _tractiveForce;

	public ObservableTractiveForceMethodPart LeftBank => _leftBank;

	public ObservableTractiveForceMethodPart RightBank => _rightBank;

	public ObservableTractiveForceMethodPart Bed => _bed;

	public double Depth
	{
		get
		{
			return _tractiveForce.Depth;
		}
		set
		{
			SetProperty(_tractiveForce.Depth, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.Depth = n;
			}, "Depth");
		}
	}

	public double Discharge
	{
		get
		{
			return _tractiveForce.Discharge;
		}
		set
		{
			SetProperty(_tractiveForce.Discharge, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.Discharge = n;
			}, "Discharge");
		}
	}

	public double Slope
	{
		get
		{
			return _tractiveForce.Slope;
		}
		set
		{
			SetProperty(_tractiveForce.Slope, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.Slope = n;
			}, "Slope");
		}
	}

	public double LeftBankSideSlope
	{
		get
		{
			return _tractiveForce.LeftBankSideSlope;
		}
		set
		{
			SetProperty(_tractiveForce.LeftBankSideSlope, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.LeftBankSideSlope = n;
			}, "LeftBankSideSlope");
		}
	}

	public double RightBankSideSlope
	{
		get
		{
			return _tractiveForce.RightBankSideSlope;
		}
		set
		{
			SetProperty(_tractiveForce.RightBankSideSlope, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.RightBankSideSlope = n;
			}, "RightBankSideSlope");
		}
	}

	public double Temperature
	{
		get
		{
			return _tractiveForce.Temperature;
		}
		set
		{
			SetProperty(_tractiveForce.Temperature, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.Temperature = n;
			}, "Temperature");
		}
	}

	public double Width
	{
		get
		{
			return _tractiveForce.Width;
		}
		set
		{
			SetProperty(_tractiveForce.Width, value, _tractiveForce, (TractiveForceMethod u, double n) =>
			{
				u.Width = n;
			}, "Width");
		}
	}

	public TractiveForceSolutionMethod SolutionMethod
	{
		get
		{
			return _tractiveForce.SolutionMethod;
		}
		set
		{
			SetProperty(_tractiveForce.SolutionMethod, value, _tractiveForce, (TractiveForceMethod u, TractiveForceSolutionMethod n) =>
			{
				u.SolutionMethod = n;
			}, "SolutionMethod");
		}
	}

	public ObservableTractiveForceMethod(TractiveForceMethod tractiveForce)
	{
		_tractiveForce = tractiveForce;
		_leftBank = new ObservableTractiveForceMethodPart(tractiveForce.LeftBank);
		_rightBank = new ObservableTractiveForceMethodPart(tractiveForce.RightBank);
		_bed = new ObservableTractiveForceMethodPart(tractiveForce.Bed);
	}
}

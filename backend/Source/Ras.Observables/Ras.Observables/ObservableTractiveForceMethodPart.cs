using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Hydraulics.HydraulicDesign;
using Ras.Layers;
using Utility.Validation;

namespace Ras.Observables;

public class ObservableTractiveForceMethodPart : ObservableValidator
{
	private TractiveForceMethodPart _tractiveForcePart;

	public TractiveForceMethodPart TractiveForcePart => _tractiveForcePart;

	public double D
	{
		get
		{
			return _tractiveForcePart.D;
		}
		set
		{
			SetProperty(_tractiveForcePart.D, value, _tractiveForcePart, (TractiveForceMethodPart u, double n) =>
			{
				u.D = n;
			}, "D");
		}
	}

	[Double(true)]
	[RecommendedBounds(30.0, 330.0, null, false)]
	public double AngleOfRepose
	{
		get
		{
			return _tractiveForcePart.AngleOfRepose;
		}
		set
		{
			SetProperty(_tractiveForcePart.AngleOfRepose, value, _tractiveForcePart, (TractiveForceMethodPart u, double n) =>
			{
				u.AngleOfRepose = n;
			}, "AngleOfRepose");
		}
	}

	public double Roughness
	{
		get
		{
			return _tractiveForcePart.Roughness;
		}
		set
		{
			SetProperty(_tractiveForcePart.Roughness, value, _tractiveForcePart, (TractiveForceMethodPart u, double n) =>
			{
				u.Roughness = n;
			}, "Roughness");
		}
	}

	public double SpecificGravity
	{
		get
		{
			return _tractiveForcePart.SpecificGravity;
		}
		set
		{
			SetProperty(_tractiveForcePart.SpecificGravity, value, _tractiveForcePart, (TractiveForceMethodPart u, double n) =>
			{
				u.SpecificGravity = n;
			}, "SpecificGravity");
		}
	}

	public double ShieldsParameter
	{
		get
		{
			return _tractiveForcePart.ShieldsParameter;
		}
		set
		{
			SetProperty(_tractiveForcePart.ShieldsParameter, value, _tractiveForcePart, (TractiveForceMethodPart u, double n) =>
			{
				u.ShieldsParameter = n;
			}, "ShieldsParameter");
		}
	}

	public BankEquation Equation
	{
		get
		{
			return _tractiveForcePart.Equation;
		}
		set
		{
			SetProperty(_tractiveForcePart.Equation, value, _tractiveForcePart, (TractiveForceMethodPart u, BankEquation n) =>
			{
				u.Equation = n;
			}, "Equation");
		}
	}

	public ObservableTractiveForceMethodPart(TractiveForceMethodPart tractiveForcePart)
	{
		_tractiveForcePart = tractiveForcePart;
	}
}

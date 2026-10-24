using System;
using Utility.Units;

namespace Ras.Engine.Structures.Culverts;

public class CulvertSolverControl
{
	public double EnergyGradeTolerance { get; set; } = 1E-06;

	public int MaxIterations { get; set; } = 100;

	public double ZeroFlowDepthTolerance { get; set; } = 0.001;

	public double ZeroFlowEgDiffTolerance { get; set; } = 0.0001;

	public double OutletControlDepthTolerance { get; set; } = 0.01;

	public int NumProfileStations { get; set; } = 20;

	public UnitSystem UnitSys { get; }

	public double MannCoef { get; set; }

	public double Grav { get; set; }

	public bool IsAllowSiphon { get; set; }

	public bool UseUSVelInEntLoss { get; set; } = true;

	public CulvertSolverControl(UnitSystem unitSys)
	{
		UnitSys = unitSys;
		switch (unitSys)
		{
		case UnitSystem.USCustomary:
			MannCoef = 1.48592;
			Grav = 32.174;
			break;
		case UnitSystem.SI:
			MannCoef = 1.0;
			Grav = 9.80665;
			break;
		default:
			throw new NotSupportedException("Unsupported unit system " + unitSys);
		}
	}
}

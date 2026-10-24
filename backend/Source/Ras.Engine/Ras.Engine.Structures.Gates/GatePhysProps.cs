using Utility.Units;

namespace Ras.Engine.Structures.Gates;

public class GatePhysProps
{
	public GateType Shape { get; set; }

	public GateWeirType WeirShape { get; set; }

	public double Rise { get; set; }

	public double Span { get; set; }

	public double Invert { get; set; }

	public double TrunnionHeight { get; set; }

	public double CoefGate { get; set; }

	public double CoefOrifice { get; set; }

	public double CoefWeir { get; set; }

	public double ExpHead { get; set; } = 0.5;

	public double ExpOpen { get; set; } = 1.0;

	public double ExpTrunnion { get; set; }

	public double DesignHead { get; set; }

	public bool IsDesignHead { get; set; }

	public bool IsHeadRefCenter { get; set; }

	public double Tolerance { get; set; } = 0.01;

	public double Grav { get; set; }

	public double SmallPositiveNumber { get; set; } = 1E-07;

	public void InitializeDefault()
	{
		TrunnionHeight = 1.0;
		ExpTrunnion = 0.0;
		CoefGate = 0.5;
		CoefOrifice = 0.8;
		CoefWeir = 3.0;
		Shape = GateType.Sluice;
		WeirShape = GateWeirType.BroadCrested;
		Invert = 26.0;
		Rise = 10.0;
		Span = 10.0;
	}

	public void SetUnitSys(UnitSystem unitSys)
	{
		if (unitSys == UnitSystem.USCustomary)
		{
			Grav = 32.174;
		}
		else
		{
			Grav = 9.80665;
		}
	}
}

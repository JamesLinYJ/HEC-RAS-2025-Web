using Utility.Units;

namespace Geospatial.Hydraulics;

public sealed class DCMControl
{
	public UnitSystem Units { get; }

	public float Slope { get; init; } = 0.001f;

	public float Gravity { get; init; }

	public float WseIncrement { get; init; } = 0.5f;

	public int MaximumWseCount { get; init; } = 50;

	public float ConveyanceRelativeTolerance { get; init; } = 0.0002f;

	public float VegetationStemDensity { get; init; }

	public float VegetationDragCoefficient { get; init; }

	public float VegetationStemDiameter { get; init; }

	public bool ComputeAlpha { get; init; } = true;

	public bool ComputeBeta { get; init; } = true;

	public bool SaveVelocityDistribution { get; init; }

	internal float ManningUnitFactor => HydraulicCurveDefaults.GetManningFactor(Units);

	public DCMControl(UnitSystem units)
	{
		Units = units;
		Gravity = HydraulicCurveDefaults.GetGravity(units);
	}
}

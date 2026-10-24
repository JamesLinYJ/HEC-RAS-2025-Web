using Utility.Units;

namespace Geospatial.Hydraulics;

public sealed class LDMControl
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

	public float VelocityTolerance { get; init; } = 0.0001f;

	public float PicardRelaxationFactor { get; init; } = 0.5f;

	public float NewtonRelaxationFactor { get; init; } = 1f;

	public int MaximumIterations { get; init; } = 50;

	public float NewtonJacobianFactor { get; init; } = 1f;

	public bool ComputeDispersion { get; init; } = true;

	public float MixingCoefficient { get; init; } = 0.1f;

	public float LongitudinalMixingCoefficient { get; init; } = float.NaN;

	public float LateralMixingCoefficient { get; init; } = float.NaN;

	internal float EffectiveLongitudinalMixingCoefficient
	{
		get
		{
			if (!float.IsNaN(LongitudinalMixingCoefficient))
			{
				return LongitudinalMixingCoefficient;
			}
			return MixingCoefficient;
		}
	}

	internal float EffectiveLateralMixingCoefficient
	{
		get
		{
			if (!float.IsNaN(LateralMixingCoefficient))
			{
				return LateralMixingCoefficient;
			}
			return MixingCoefficient;
		}
	}

	internal float ManningUnitFactor => HydraulicCurveDefaults.GetManningFactor(Units);

	public LDMControl(UnitSystem units)
	{
		Units = units;
		Gravity = HydraulicCurveDefaults.GetGravity(units);
	}
}

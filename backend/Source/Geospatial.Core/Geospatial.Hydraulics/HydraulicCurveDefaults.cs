using System;
using Utility.Units;

namespace Geospatial.Hydraulics;

public static class HydraulicCurveDefaults
{
	public const float GravitySi = 9.80665f;

	public const float GravityUsCustomary = 32.174f;

	public const float ManningFactorSi = 1f;

	public const float ManningFactorUsCustomary = 1.486f;

	public const float Slope = 0.001f;

	public const float WseIncrement = 0.5f;

	public const int MaximumWseCount = 50;

	public const float ConveyanceRelativeTolerance = 0.0002f;

	public const float VegetationStemDensity = 0f;

	public const float VegetationDragCoefficient = 0f;

	public const float VegetationStemDiameter = 0f;

	public const bool ComputeAlpha = true;

	public const bool ComputeBeta = true;

	public const bool SaveVelocityDistribution = false;

	public static float GetGravity(UnitSystem units)
	{
		return units switch
		{
			UnitSystem.SI => 9.80665f, 
			UnitSystem.USCustomary => 32.174f, 
			_ => throw new ArgumentOutOfRangeException("units", "A hydraulic unit system is required."), 
		};
	}

	public static float GetManningFactor(UnitSystem units)
	{
		return units switch
		{
			UnitSystem.SI => 1f, 
			UnitSystem.USCustomary => 1.486f, 
			_ => throw new ArgumentOutOfRangeException("units", "A hydraulic unit system is required."), 
		};
	}
}

using System;

namespace Ras.Engine;

public struct SecondaryFlowControl
{
	public bool Enabled = false;

	public SecondaryFlowModel Model = SecondaryFlowModel.Equilibrium;

	public double IntensityFactor = 1.0;

	public double AdaptationLengthFactor = 1.0;

	public double DiffusionFactor = 1.0;

	public double DispersionFactor = 1.0;

	public int OuterIterations = 1;

	public const double Kappa = 0.41;

	public SecondaryFlowControl()
	{
	}

	public static double CalculateRoughnessParameter(double manningsN, double hydraulicRadius, double gravity)
	{
		if (!double.IsFinite(manningsN) || manningsN < 0.0)
		{
			return double.NaN;
		}
		if (!double.IsFinite(hydraulicRadius) || hydraulicRadius <= 0.0)
		{
			return double.NaN;
		}
		if (!double.IsFinite(gravity) || gravity <= 0.0)
		{
			return double.NaN;
		}
		if (manningsN == 0.0)
		{
			return double.PositiveInfinity;
		}
		return 0.41 * Math.Pow(hydraulicRadius, 1.0 / 6.0) / (manningsN * Math.Sqrt(gravity));
	}

	public static double SurfaceVelocityCoefficient(double roughnessParameter)
	{
		return 17.846519928613922 * (0.5 - 1.0 / roughnessParameter);
	}

	public double AdaptationLength(double depth, double roughnessParameter)
	{
		return AdaptationLengthFactor * (roughnessParameter - 1.0) * depth / 0.33619999999999994;
	}
}

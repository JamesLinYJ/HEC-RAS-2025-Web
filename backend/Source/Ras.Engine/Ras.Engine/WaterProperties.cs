using System;

namespace Ras.Engine;

public static class WaterProperties
{
	private const float D0 = 999.8426f;

	private const float D1 = 0.06793952f;

	private const float D2 = -0.00909529f;

	private const float D3 = 0.0001001685f;

	private const float D4 = -1.120083E-06f;

	private const float D5 = 6.536336E-09f;

	private const float A0 = 0.824493f;

	private const float A1 = -0.0040899f;

	private const float A2 = 7.6438E-05f;

	private const float A3 = -8.2467E-07f;

	private const float A4 = 5.3875E-09f;

	private const float B0 = -0.00572466f;

	private const float B1 = 0.00010227f;

	private const float B2 = -1.6546E-06f;

	private const float Cc = 0.00048314f;

	private const float Mw0 = 4.2844E-05f;

	private const float Mw1 = 0.157f;

	private const float Mw2 = 64.993f;

	private const float Mw3 = 91.296f;

	private const float MA0 = 1.541f;

	private const float MA1 = 0.01998f;

	private const float MA2 = -9.52E-05f;

	private const float MB0 = 7.974f;

	private const float MB1 = -0.07561f;

	private const float MB2 = 0.0004724f;

	private const float PsuToKgPerKg = 0.001f;

	public static float Density(float tempC, float salinityPSU)
	{
		float freshwaterDensity = 999.8426f + tempC * (0.06793952f + tempC * (-0.00909529f + tempC * (0.0001001685f + tempC * (-1.120083E-06f + tempC * 6.536336E-09f))));
		float salinityLinearCoefficient = 0.824493f + tempC * (-0.0040899f + tempC * (7.6438E-05f + tempC * (-8.2467E-07f + tempC * 5.3875E-09f)));
		float salinitySqrtCoefficient = -0.00572466f + tempC * (0.00010227f + tempC * -1.6546E-06f);
		return freshwaterDensity + (salinityLinearCoefficient + salinitySqrtCoefficient * MathF.Sqrt(salinityPSU) + 0.00048314f * salinityPSU) * salinityPSU;
	}

	public static float DynamicViscosity(float tempC, float salinityPSU)
	{
		float tempCOffset = tempC + 64.993f;
		float pureWaterViscosity = 4.2844E-05f + 1f / (0.157f * tempCOffset * tempCOffset - 91.296f);
		float scaledSalinityPSU = salinityPSU * 0.001f;
		float viscosityLinearCoefficient = 1.541f + tempC * (0.01998f + tempC * -9.52E-05f);
		float viscosityQuadraticCoefficient = 7.974f + tempC * (-0.07561f + tempC * 0.0004724f);
		return pureWaterViscosity * (1f + scaledSalinityPSU * (viscosityLinearCoefficient + viscosityQuadraticCoefficient * scaledSalinityPSU));
	}

	public static float KinematicViscosity(float tempC, float salinityPSU)
	{
		return DynamicViscosity(tempC, salinityPSU) / Density(tempC, salinityPSU);
	}
}

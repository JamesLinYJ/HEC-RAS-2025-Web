using System;

namespace Ras.Engine.Kinetics;

public static class SedimentHeatExchange
{
	private const float CelsiusToKelvin = 273.15f;

	public static float ComputeHeatFlux(float waterTemperatureCelsius, float sedimentTemperatureCelsius, float sedimentDensityKilogramsPerCubicMeter, float sedimentSpecificHeatJoulesPerKilogramCelsius, float sedimentThermalDiffusivitySquareMetersPerSecond, float activeLayerThicknessMeters)
	{
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		ValidateTemperatureCelsius(sedimentTemperatureCelsius, "sedimentTemperatureCelsius");
		ValidatePositive(sedimentDensityKilogramsPerCubicMeter, "sedimentDensityKilogramsPerCubicMeter");
		ValidatePositive(sedimentSpecificHeatJoulesPerKilogramCelsius, "sedimentSpecificHeatJoulesPerKilogramCelsius");
		ValidateNonnegative(sedimentThermalDiffusivitySquareMetersPerSecond, "sedimentThermalDiffusivitySquareMetersPerSecond");
		ValidatePositive(activeLayerThicknessMeters, "activeLayerThicknessMeters");
		return ComputeHeatFluxCore(sedimentTemperatureCelsius - waterTemperatureCelsius, sedimentDensityKilogramsPerCubicMeter, sedimentSpecificHeatJoulesPerKilogramCelsius, sedimentThermalDiffusivitySquareMetersPerSecond, activeLayerThicknessMeters);
	}

	public static float ComputeHeatFluxUS(float waterTemperatureFahrenheit, float sedimentTemperatureFahrenheit, float sedimentDensityPoundsPerCubicFoot, float sedimentSpecificHeatBtuPerPoundFahrenheit, float sedimentThermalDiffusivitySquareFeetPerSecond, float activeLayerThicknessFeet)
	{
		float temperatureToCelsius = WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit);
		float temperatureToCelsius2 = WaterTemperatureUnitConversions.TemperatureToCelsius(sedimentTemperatureFahrenheit);
		ValidateTemperatureCelsius(temperatureToCelsius, "waterTemperatureFahrenheit");
		ValidateTemperatureCelsius(temperatureToCelsius2, "sedimentTemperatureFahrenheit");
		ValidatePositive(sedimentDensityPoundsPerCubicFoot, "sedimentDensityPoundsPerCubicFoot");
		ValidatePositive(sedimentSpecificHeatBtuPerPoundFahrenheit, "sedimentSpecificHeatBtuPerPoundFahrenheit");
		ValidateNonnegative(sedimentThermalDiffusivitySquareFeetPerSecond, "sedimentThermalDiffusivitySquareFeetPerSecond");
		ValidatePositive(activeLayerThicknessFeet, "activeLayerThicknessFeet");
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeHeatFluxCore((sedimentTemperatureFahrenheit - waterTemperatureFahrenheit) / 1.8f, sedimentDensityPoundsPerCubicFoot * 16.018463f, sedimentSpecificHeatBtuPerPoundFahrenheit * 4186.801f, sedimentThermalDiffusivitySquareFeetPerSecond * 0.09290304f, activeLayerThicknessFeet * 0.3048f));
	}

	private static float ComputeHeatFluxCore(float temperatureDifferenceCelsius, float sedimentDensity, float sedimentSpecificHeat, float sedimentThermalDiffusivity, float activeLayerThickness)
	{
		return sedimentDensity * sedimentSpecificHeat * sedimentThermalDiffusivity / (0.5f * activeLayerThickness) * temperatureDifferenceCelsius;
	}

	private static void ValidateTemperatureCelsius(float computed, string parameterName)
	{
		ValidateFinite(computed, parameterName);
		if (computed <= -273.15f)
		{
			throw new ArgumentOutOfRangeException(parameterName, computed, "Temperature must be above absolute zero.");
		}
	}

	private static void ValidateNonnegative(float computed, string parameterName)
	{
		ValidateFinite(computed, parameterName);
		if (computed < 0f)
		{
			throw new ArgumentOutOfRangeException(parameterName, computed, "Value must be nonnegative.");
		}
	}

	private static void ValidatePositive(float computed, string parameterName)
	{
		ValidateFinite(computed, parameterName);
		if (computed <= 0f)
		{
			throw new ArgumentOutOfRangeException(parameterName, computed, "Value must be positive.");
		}
	}

	private static void ValidateFinite(float computed, string parameterName)
	{
		if (!float.IsFinite(computed))
		{
			throw new ArgumentOutOfRangeException(parameterName, computed, "Value must be finite.");
		}
	}
}

using System;

namespace Ras.Engine.Kinetics;

internal static class WaterTemperatureUnitConversions
{
	internal const float FeetToMeters = 0.3048f;

	internal const float SquareFeetToSquareMeters = 0.09290304f;

	internal const float CubicFeetToCubicMeters = 0.028316846f;

	internal const float InchesMercuryToHectopascals = 33.863888f;

	internal const float PoundsPerCubicFootToKilogramsPerCubicMeter = 16.018463f;

	internal const float BtuPerPoundFahrenheitToJoulesPerKilogramCelsius = 4186.801f;

	internal const float BtuPerSquareFootSecondToWattsPerSquareMeter = 11356.527f;

	internal const float BtuPerSecondToWatts = 1055.0559f;

	internal static float TemperatureToCelsius(float temperatureFahrenheit)
	{
		return (temperatureFahrenheit - 32f) / 1.8f;
	}

	internal static float HeatFluxToSI(float heatFluxBtuPerSquareFootSecond)
	{
		return heatFluxBtuPerSquareFootSecond * 11356.527f;
	}

	internal static float HeatFluxToUS(float heatFluxWattsPerSquareMeter)
	{
		return heatFluxWattsPerSquareMeter / 11356.527f;
	}

	internal static float HeatRateToSI(float heatRateBtuPerSecond)
	{
		return heatRateBtuPerSecond * 1055.0559f;
	}

	internal static float HeatRateToUS(float heatRateWatts)
	{
		return heatRateWatts / 1055.0559f;
	}

	internal static float WindCoefficientBToSI(float windCoefficientB, float windExponent)
	{
		return windCoefficientB * MathF.Pow(0.3048f, 1f - windExponent);
	}

	internal static MeteorologicalConditionsSI ToSI(MeteorologicalConditionsUS meteorologicalConditionsUS)
	{
		return new MeteorologicalConditionsSI
		{
			AirTemperature = TemperatureToCelsius(meteorologicalConditionsUS.AirTemperature),
			RelativeHumidity = meteorologicalConditionsUS.RelativeHumidity,
			AtmosphericPressure = meteorologicalConditionsUS.AtmosphericPressure * 33.863888f,
			WindSpeed = meteorologicalConditionsUS.WindSpeed * 0.3048f,
			WindMeasurementHeight = meteorologicalConditionsUS.WindMeasurementHeight * 0.3048f,
			CloudCover = meteorologicalConditionsUS.CloudCover,
			IncidentShortwave = HeatFluxToSI(meteorologicalConditionsUS.IncidentShortwave)
		};
	}

	internal static WaterColumnStateSI ToSI(WaterColumnStateUS waterColumnStateUS)
	{
		return new WaterColumnStateSI
		{
			WaterTemperature = TemperatureToCelsius(waterColumnStateUS.WaterTemperature),
			WaterDepth = waterColumnStateUS.WaterDepth * 0.3048f,
			SurfaceArea = waterColumnStateUS.SurfaceArea * 0.09290304f,
			SedimentArea = waterColumnStateUS.SedimentArea * 0.09290304f,
			Volume = waterColumnStateUS.Volume * 0.028316846f,
			WaterDensity = waterColumnStateUS.WaterDensity * 16.018463f,
			WaterSpecificHeat = waterColumnStateUS.WaterSpecificHeat * 4186.801f
		};
	}

	internal static SedimentThermalPropertiesSI ToSI(SedimentThermalPropertiesUS sedimentThermalPropertiesUS)
	{
		return new SedimentThermalPropertiesSI
		{
			SedimentTemperature = TemperatureToCelsius(sedimentThermalPropertiesUS.SedimentTemperature),
			ActiveLayerThickness = sedimentThermalPropertiesUS.ActiveLayerThickness * 0.3048f,
			Density = sedimentThermalPropertiesUS.Density * 16.018463f,
			SpecificHeat = sedimentThermalPropertiesUS.SpecificHeat * 4186.801f,
			ThermalDiffusivity = sedimentThermalPropertiesUS.ThermalDiffusivity * 0.09290304f
		};
	}

	internal static ShortwaveRadiationParametersSI ToSI(ShortwaveRadiationParametersUS shortwaveRadiationParametersUS)
	{
		return new ShortwaveRadiationParametersSI
		{
			SurfaceAbsorptionFraction = shortwaveRadiationParametersUS.SurfaceAbsorptionFraction,
			LightExtinction = shortwaveRadiationParametersUS.LightExtinction / 0.3048f
		};
	}

	internal static WaterSurfaceExchangeParametersSI ToSI(WaterSurfaceExchangeParametersUS waterSurfaceExchangeParametersUS)
	{
		return new WaterSurfaceExchangeParametersSI
		{
			WindCoefficientA = waterSurfaceExchangeParametersUS.WindCoefficientA * 0.3048f,
			WindCoefficientB = WindCoefficientBToSI(waterSurfaceExchangeParametersUS.WindCoefficientB, waterSurfaceExchangeParametersUS.WindExponent),
			WindExponent = waterSurfaceExchangeParametersUS.WindExponent,
			WindRoughnessHeight = waterSurfaceExchangeParametersUS.WindRoughnessHeight * 0.3048f,
			AirSpecificHeat = waterSurfaceExchangeParametersUS.AirSpecificHeat * 4186.801f,
			SensibleHeatDiffusivityRatio = waterSurfaceExchangeParametersUS.SensibleHeatDiffusivityRatio,
			SurfaceExchangeMode = waterSurfaceExchangeParametersUS.SurfaceExchangeMode
		};
	}

	internal static WaterHeatFluxComponentsSI ToSI(WaterHeatFluxComponentsUS waterHeatFluxComponentsUS)
	{
		return new WaterHeatFluxComponentsSI
		{
			SolarRadiation = HeatFluxToSI(waterHeatFluxComponentsUS.SolarRadiation),
			AtmosphericLongwave = HeatFluxToSI(waterHeatFluxComponentsUS.AtmosphericLongwave),
			BackRadiation = HeatFluxToSI(waterHeatFluxComponentsUS.BackRadiation),
			SensibleAirWater = HeatFluxToSI(waterHeatFluxComponentsUS.SensibleAirWater),
			LatentEvaporation = HeatFluxToSI(waterHeatFluxComponentsUS.LatentEvaporation),
			Sediment = HeatFluxToSI(waterHeatFluxComponentsUS.Sediment)
		};
	}

	internal static WaterHeatFluxComponentsUS ToUS(WaterHeatFluxComponentsSI waterHeatFluxComponentsSI)
	{
		return new WaterHeatFluxComponentsUS
		{
			SolarRadiation = HeatFluxToUS(waterHeatFluxComponentsSI.SolarRadiation),
			AtmosphericLongwave = HeatFluxToUS(waterHeatFluxComponentsSI.AtmosphericLongwave),
			BackRadiation = HeatFluxToUS(waterHeatFluxComponentsSI.BackRadiation),
			SensibleAirWater = HeatFluxToUS(waterHeatFluxComponentsSI.SensibleAirWater),
			LatentEvaporation = HeatFluxToUS(waterHeatFluxComponentsSI.LatentEvaporation),
			Sediment = HeatFluxToUS(waterHeatFluxComponentsSI.Sediment)
		};
	}
}

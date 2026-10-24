using System;

namespace Ras.Engine.Kinetics;

public static class WaterHeatBudget
{
	public static float ComputeSolarRadiationHeatFlux(float incidentShortwaveWattsPerSquareMeter, float surfaceAbsorptionFraction, float lightExtinctionPerMeter, float waterDepthMeters)
	{
		ValidateNonnegative(incidentShortwaveWattsPerSquareMeter, "incidentShortwaveWattsPerSquareMeter");
		ValidateFraction(surfaceAbsorptionFraction, "surfaceAbsorptionFraction");
		ValidateNonnegative(lightExtinctionPerMeter, "lightExtinctionPerMeter");
		ValidateNonnegative(waterDepthMeters, "waterDepthMeters");
		float y = ComputeSubsurfaceAbsorptionFraction(lightExtinctionPerMeter * waterDepthMeters);
		float surfaceAbsorptionFractionTerm = MathF.FusedMultiplyAdd(1f - surfaceAbsorptionFraction, y, surfaceAbsorptionFraction);
		return incidentShortwaveWattsPerSquareMeter * surfaceAbsorptionFractionTerm;
	}

	public static WaterHeatFluxComponentsSI ComputeHeatFluxes(MeteorologicalConditionsSI meteorology, WaterColumnStateSI water, ShortwaveRadiationParametersSI shortwaveParameters, WaterSurfaceExchangeParametersSI surfaceParameters)
	{
		return ComputeHeatFluxes(meteorology, water, default, shortwaveParameters, surfaceParameters, includeSedimentHeatExchange: false);
	}

	public static WaterHeatFluxComponentsSI ComputeHeatFluxes(MeteorologicalConditionsSI meteorology, WaterColumnStateSI water, SedimentThermalPropertiesSI sediment, ShortwaveRadiationParametersSI shortwaveParameters, WaterSurfaceExchangeParametersSI surfaceParameters)
	{
		ValidatePositive(water.SedimentArea, "SedimentArea");
		return ComputeHeatFluxes(meteorology, water, sediment, shortwaveParameters, surfaceParameters, includeSedimentHeatExchange: true);
	}

	public static float ComputeNetHeatRate(float netSurfaceHeatFluxWattsPerSquareMeter, float surfaceAreaSquareMeters, float sedimentHeatFluxWattsPerSquareMeter, float sedimentAreaSquareMeters)
	{
		ValidateFinite(netSurfaceHeatFluxWattsPerSquareMeter, "netSurfaceHeatFluxWattsPerSquareMeter");
		ValidatePositive(surfaceAreaSquareMeters, "surfaceAreaSquareMeters");
		ValidateFinite(sedimentHeatFluxWattsPerSquareMeter, "sedimentHeatFluxWattsPerSquareMeter");
		ValidateNonnegative(sedimentAreaSquareMeters, "sedimentAreaSquareMeters");
		float z = sedimentHeatFluxWattsPerSquareMeter * sedimentAreaSquareMeters;
		float netHeatRate = MathF.FusedMultiplyAdd(netSurfaceHeatFluxWattsPerSquareMeter, surfaceAreaSquareMeters, z);
		ValidateFinite(netHeatRate, "heatRate");
		return netHeatRate;
	}

	public static float ComputeNetHeatRate(WaterHeatFluxComponentsSI components, WaterColumnStateSI water)
	{
		return ComputeNetHeatRate(components.NetSurfaceHeatFlux, water.SurfaceArea, components.Sediment, water.SedimentArea);
	}

	public static float ComputeTemperatureRate(float netHeatRateWatts, float volumeCubicMeters, float waterDensity, float waterSpecificHeat)
	{
		ValidateFinite(netHeatRateWatts, "netHeatRateWatts");
		ValidatePositive(volumeCubicMeters, "volumeCubicMeters");
		ValidatePositive(waterDensity, "waterDensity");
		ValidatePositive(waterSpecificHeat, "waterSpecificHeat");
		return netHeatRateWatts / volumeCubicMeters / waterDensity / waterSpecificHeat;
	}

	public static float ComputeTemperatureRate(WaterHeatFluxComponentsSI components, WaterColumnStateSI water)
	{
		return ComputeTemperatureRate(ComputeNetHeatRate(components, water), water.Volume, water.WaterDensity, water.WaterSpecificHeat);
	}

	public static float ComputeSolarRadiationHeatFluxUS(float incidentShortwaveBtuPerSquareFootSecond, float surfaceAbsorptionFraction, float lightExtinctionPerFoot, float waterDepthFeet)
	{
		ValidateNonnegative(incidentShortwaveBtuPerSquareFootSecond, "incidentShortwaveBtuPerSquareFootSecond");
		ValidateFraction(surfaceAbsorptionFraction, "surfaceAbsorptionFraction");
		ValidateNonnegative(lightExtinctionPerFoot, "lightExtinctionPerFoot");
		ValidateNonnegative(waterDepthFeet, "waterDepthFeet");
		float y = ComputeSubsurfaceAbsorptionFraction(lightExtinctionPerFoot * waterDepthFeet);
		float surfaceAbsorptionFractionTerm = MathF.FusedMultiplyAdd(1f - surfaceAbsorptionFraction, y, surfaceAbsorptionFraction);
		return incidentShortwaveBtuPerSquareFootSecond * surfaceAbsorptionFractionTerm;
	}

	public static WaterHeatFluxComponentsUS ComputeHeatFluxesUS(MeteorologicalConditionsUS meteorology, WaterColumnStateUS water, ShortwaveRadiationParametersUS shortwaveParameters, WaterSurfaceExchangeParametersUS surfaceParameters)
	{
		return WaterTemperatureUnitConversions.ToUS(ComputeHeatFluxes(WaterTemperatureUnitConversions.ToSI(meteorology), WaterTemperatureUnitConversions.ToSI(water), WaterTemperatureUnitConversions.ToSI(shortwaveParameters), WaterTemperatureUnitConversions.ToSI(surfaceParameters)));
	}

	public static WaterHeatFluxComponentsUS ComputeHeatFluxesUS(MeteorologicalConditionsUS meteorology, WaterColumnStateUS water, SedimentThermalPropertiesUS sediment, ShortwaveRadiationParametersUS shortwaveParameters, WaterSurfaceExchangeParametersUS surfaceParameters)
	{
		return WaterTemperatureUnitConversions.ToUS(ComputeHeatFluxes(WaterTemperatureUnitConversions.ToSI(meteorology), WaterTemperatureUnitConversions.ToSI(water), WaterTemperatureUnitConversions.ToSI(sediment), WaterTemperatureUnitConversions.ToSI(shortwaveParameters), WaterTemperatureUnitConversions.ToSI(surfaceParameters)));
	}

	public static float ComputeNetHeatRateUS(float netSurfaceHeatFluxBtuPerSquareFootSecond, float surfaceAreaSquareFeet, float sedimentHeatFluxBtuPerSquareFootSecond, float sedimentAreaSquareFeet)
	{
		ValidateFinite(netSurfaceHeatFluxBtuPerSquareFootSecond, "netSurfaceHeatFluxBtuPerSquareFootSecond");
		ValidatePositive(surfaceAreaSquareFeet, "surfaceAreaSquareFeet");
		ValidateFinite(sedimentHeatFluxBtuPerSquareFootSecond, "sedimentHeatFluxBtuPerSquareFootSecond");
		ValidateNonnegative(sedimentAreaSquareFeet, "sedimentAreaSquareFeet");
		float z = sedimentHeatFluxBtuPerSquareFootSecond * sedimentAreaSquareFeet;
		float netHeatRateUS = MathF.FusedMultiplyAdd(netSurfaceHeatFluxBtuPerSquareFootSecond, surfaceAreaSquareFeet, z);
		ValidateFinite(netHeatRateUS, "heatRate");
		return netHeatRateUS;
	}

	public static float ComputeNetHeatRateUS(WaterHeatFluxComponentsUS components, WaterColumnStateUS water)
	{
		return ComputeNetHeatRateUS(components.NetSurfaceHeatFlux, water.SurfaceArea, components.Sediment, water.SedimentArea);
	}

	public static float ComputeTemperatureRateUS(float netHeatRateBtuPerSecond, float volumeCubicFeet, float waterDensity, float waterSpecificHeat)
	{
		ValidateFinite(netHeatRateBtuPerSecond, "netHeatRateBtuPerSecond");
		ValidatePositive(volumeCubicFeet, "volumeCubicFeet");
		ValidatePositive(waterDensity, "waterDensity");
		ValidatePositive(waterSpecificHeat, "waterSpecificHeat");
		return netHeatRateBtuPerSecond / volumeCubicFeet / waterDensity / waterSpecificHeat;
	}

	public static float ComputeTemperatureRateUS(WaterHeatFluxComponentsUS components, WaterColumnStateUS water)
	{
		return ComputeTemperatureRateUS(ComputeNetHeatRateUS(components, water), water.Volume, water.WaterDensity, water.WaterSpecificHeat);
	}

	internal static float SumSurfaceHeatFluxes(float solarRadiation, float atmosphericLongwave, float backRadiation, float sensibleAirWater, float latentEvaporation)
	{
		float sum = 0f;
		float compensation = 0f;
		AddCompensated(solarRadiation, ref sum, ref compensation);
		AddCompensated(atmosphericLongwave, ref sum, ref compensation);
		AddCompensated(backRadiation, ref sum, ref compensation);
		AddCompensated(sensibleAirWater, ref sum, ref compensation);
		AddCompensated(latentEvaporation, ref sum, ref compensation);
		return sum + compensation;
	}

	private static WaterHeatFluxComponentsSI ComputeHeatFluxes(MeteorologicalConditionsSI meteorology, WaterColumnStateSI water, SedimentThermalPropertiesSI sediment, ShortwaveRadiationParametersSI shortwaveParameters, WaterSurfaceExchangeParametersSI surfaceParameters, bool includeSedimentHeatExchange)
	{
		float windFunctionMetersPerSecond = WaterSurfaceHeatExchange.ComputeWindFunction(water.WaterTemperature, meteorology.AirTemperature, meteorology.RelativeHumidity, meteorology.AtmosphericPressure, meteorology.WindSpeed, meteorology.WindMeasurementHeight, surfaceParameters.WindRoughnessHeight, surfaceParameters.WindCoefficientA, surfaceParameters.WindCoefficientB, surfaceParameters.WindExponent, surfaceParameters.SurfaceExchangeMode);
		float sensibleAirWater;
		float latentEvaporation;
		if (surfaceParameters.SurfaceExchangeMode == WaterSurfaceExchangeMode.Bowen)
		{
			sensibleAirWater = WaterSurfaceHeatExchange.ComputeBowenSensibleAirWaterHeatFlux(meteorology.AirTemperature, water.WaterTemperature, meteorology.AtmosphericPressure, water.WaterDensity, windFunctionMetersPerSecond);
			latentEvaporation = WaterSurfaceHeatExchange.ComputeBowenEvaporativeHeatFlux(water.WaterTemperature, meteorology.AirTemperature, meteorology.RelativeHumidity, meteorology.AtmosphericPressure, water.WaterDensity, windFunctionMetersPerSecond);
		}
		else
		{
			sensibleAirWater = WaterSurfaceHeatExchange.ComputeSensibleAirWaterHeatFlux(meteorology.AirTemperature, water.WaterTemperature, water.WaterDensity, surfaceParameters.AirSpecificHeat, surfaceParameters.SensibleHeatDiffusivityRatio, windFunctionMetersPerSecond);
			latentEvaporation = WaterSurfaceHeatExchange.ComputeEvaporativeHeatFlux(water.WaterTemperature, meteorology.AirTemperature, meteorology.RelativeHumidity, meteorology.AtmosphericPressure, water.WaterDensity, windFunctionMetersPerSecond);
		}
		return new WaterHeatFluxComponentsSI
		{
			SolarRadiation = ComputeSolarRadiationHeatFlux(meteorology.IncidentShortwave, shortwaveParameters.SurfaceAbsorptionFraction, shortwaveParameters.LightExtinction, water.WaterDepth),
			AtmosphericLongwave = WaterSurfaceHeatExchange.ComputeAtmosphericLongwaveHeatFlux(meteorology.AirTemperature, meteorology.CloudCover),
			BackRadiation = WaterSurfaceHeatExchange.ComputeBackRadiationHeatFlux(water.WaterTemperature),
			SensibleAirWater = sensibleAirWater,
			LatentEvaporation = latentEvaporation,
			Sediment = (includeSedimentHeatExchange ? SedimentHeatExchange.ComputeHeatFlux(water.WaterTemperature, sediment.SedimentTemperature, sediment.Density, sediment.SpecificHeat, sediment.ThermalDiffusivity, sediment.ActiveLayerThickness) : 0f)
		};
	}

	private static void AddCompensated(float computed, ref float sum, ref float compensation)
	{
		float sum2 = sum + computed;
		if (MathF.Abs(sum) >= MathF.Abs(computed))
		{
			compensation += sum - sum2 + computed;
		}
		else
		{
			compensation += computed - sum2 + sum;
		}
		sum = sum2;
	}

	private static float ComputeSubsurfaceAbsorptionFraction(float opticalDepth)
	{
		if (opticalDepth < 0.01f)
		{
			float x = MathF.FusedMultiplyAdd(-1f / 24f, opticalDepth, 1f / 6f);
			x = MathF.FusedMultiplyAdd(x, opticalDepth, -0.5f);
			x = MathF.FusedMultiplyAdd(x, opticalDepth, 1f);
			return opticalDepth * x;
		}
		if (opticalDepth >= 0.5f)
		{
			return 1f - MathF.Exp(0f - opticalDepth);
		}
		float scaledOpticalDepth = 0.5f * opticalDepth;
		return 2f * MathF.Exp(0f - scaledOpticalDepth) * MathF.Sinh(scaledOpticalDepth);
	}

	private static void ValidateFraction(float computed, string parameterName)
	{
		ValidateRange(computed, 0f, 1f, parameterName);
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

	private static void ValidateRange(float computed, float minimum, float maximum, string parameterName)
	{
		ValidateFinite(computed, parameterName);
		if (computed < minimum || computed > maximum)
		{
			throw new ArgumentOutOfRangeException(parameterName, computed, $"Value must be in the inclusive range [{minimum}, {maximum}].");
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

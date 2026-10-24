using System;

namespace Ras.Engine.Kinetics;

public static class WaterSurfaceHeatExchange
{
	private const float CelsiusToKelvin = 273.15f;

	private const float Gravity = 9.806f;

	private const float DryAirGasConstant = 287.05f;

	private const float WaterVaporGasConstant = 461.495f;

	private const float StefanBoltzmann = 5.6703744E-08f;

	private const float StandardWindHeightMeters = 2f;

	private const float HectopascalsPerAtmosphere = 1013.25f;

	private const float BowenWindFunctionScale = 0.001f;

	public static float ComputeAtmosphericLongwaveHeatFlux(float airTemperatureCelsius, float cloudCover)
	{
		ValidateTemperatureCelsius(airTemperatureCelsius, "airTemperatureCelsius");
		ValidateFraction(cloudCover, "cloudCover");
		float airTemperatureCelsiusOffset = airTemperatureCelsius + 273.15f;
		float computed = airTemperatureCelsiusOffset * airTemperatureCelsiusOffset;
		float cloudCoverTerm = MathF.FusedMultiplyAdd(0.17f, cloudCover * cloudCover, 1f);
		return 5.313141E-13f * cloudCoverTerm * computed * computed * computed;
	}

	public static float ComputeBackRadiationHeatFlux(float waterTemperatureCelsius)
	{
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		float waterTemperatureCelsiusOffset = waterTemperatureCelsius + 273.15f;
		float computed = waterTemperatureCelsiusOffset * waterTemperatureCelsiusOffset;
		return -5.5002634E-08f * computed * computed;
	}

	public static float ComputeSensibleAirWaterHeatFlux(float airTemperatureCelsius, float waterTemperatureCelsius, float waterDensityKilogramsPerCubicMeter, float airSpecificHeatJoulesPerKilogramCelsius, float sensibleHeatDiffusivityRatio, float windFunctionMetersPerSecond)
	{
		ValidateTemperatureCelsius(airTemperatureCelsius, "airTemperatureCelsius");
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		ValidatePositive(waterDensityKilogramsPerCubicMeter, "waterDensityKilogramsPerCubicMeter");
		ValidatePositive(airSpecificHeatJoulesPerKilogramCelsius, "airSpecificHeatJoulesPerKilogramCelsius");
		ValidateRange(sensibleHeatDiffusivityRatio, 0.5f, 1.5f, "sensibleHeatDiffusivityRatio");
		ValidateNonnegative(windFunctionMetersPerSecond, "windFunctionMetersPerSecond");
		return ComputeSensibleAirWaterHeatFluxCore(airTemperatureCelsius - waterTemperatureCelsius, waterDensityKilogramsPerCubicMeter, airSpecificHeatJoulesPerKilogramCelsius, sensibleHeatDiffusivityRatio, windFunctionMetersPerSecond);
	}

	public static float ComputeBowenSensibleAirWaterHeatFlux(float airTemperatureCelsius, float waterTemperatureCelsius, float atmosphericPressureHectopascals, float waterDensityKilogramsPerCubicMeter, float windFunctionMetersPerSecond)
	{
		ValidateTemperatureCelsius(airTemperatureCelsius, "airTemperatureCelsius");
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		ValidatePositive(atmosphericPressureHectopascals, "atmosphericPressureHectopascals");
		ValidatePositive(waterDensityKilogramsPerCubicMeter, "waterDensityKilogramsPerCubicMeter");
		ValidateNonnegative(windFunctionMetersPerSecond, "windFunctionMetersPerSecond");
		float latentHeat = ComputeLatentHeatOfVaporizationJoulesPerKilogram(waterTemperatureCelsius);
		return ComputeBowenSensibleAirWaterHeatFluxCore(airTemperatureCelsius - waterTemperatureCelsius, atmosphericPressureHectopascals, waterDensityKilogramsPerCubicMeter, windFunctionMetersPerSecond, latentHeat);
	}

	public static float ComputeEvaporativeHeatFlux(float waterTemperatureCelsius, float airTemperatureCelsius, float relativeHumidity, float atmosphericPressureHectopascals, float waterDensityKilogramsPerCubicMeter, float windFunctionMetersPerSecond)
	{
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		ValidateTemperatureCelsius(airTemperatureCelsius, "airTemperatureCelsius");
		ValidateFraction(relativeHumidity, "relativeHumidity");
		ValidatePositive(atmosphericPressureHectopascals, "atmosphericPressureHectopascals");
		ValidatePositive(waterDensityKilogramsPerCubicMeter, "waterDensityKilogramsPerCubicMeter");
		ValidateNonnegative(windFunctionMetersPerSecond, "windFunctionMetersPerSecond");
		float saturationVaporPressureHectopascals = ComputeSaturationVaporPressureHectopascals(waterTemperatureCelsius);
		float saturationVaporPressureHectopascals2 = ComputeSaturationVaporPressureHectopascals(airTemperatureCelsius);
		ValidateVaporPressure(saturationVaporPressureHectopascals, atmosphericPressureHectopascals, "waterTemperatureCelsius", allowZero: false);
		ValidateVaporPressure(relativeHumidity * saturationVaporPressureHectopascals2, atmosphericPressureHectopascals, "relativeHumidity", allowZero: true);
		float latentHeatOfVaporizationJoulesPerKilogram = ComputeLatentHeatOfVaporizationJoulesPerKilogram(waterTemperatureCelsius);
		float relativeHumidityTerm = MathF.FusedMultiplyAdd(0f - relativeHumidity, saturationVaporPressureHectopascals2, saturationVaporPressureHectopascals);
		return 0f - 0.622f * latentHeatOfVaporizationJoulesPerKilogram * waterDensityKilogramsPerCubicMeter / atmosphericPressureHectopascals * relativeHumidityTerm * windFunctionMetersPerSecond;
	}

	public static float ComputeBowenEvaporativeHeatFlux(float waterTemperatureCelsius, float airTemperatureCelsius, float relativeHumidity, float atmosphericPressureHectopascals, float waterDensityKilogramsPerCubicMeter, float windFunctionMetersPerSecond)
	{
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		ValidateTemperatureCelsius(airTemperatureCelsius, "airTemperatureCelsius");
		ValidateFraction(relativeHumidity, "relativeHumidity");
		ValidatePositive(atmosphericPressureHectopascals, "atmosphericPressureHectopascals");
		ValidatePositive(waterDensityKilogramsPerCubicMeter, "waterDensityKilogramsPerCubicMeter");
		ValidateNonnegative(windFunctionMetersPerSecond, "windFunctionMetersPerSecond");
		float saturationVaporPressureHectopascals = ComputeSaturationVaporPressureHectopascals(waterTemperatureCelsius);
		float saturationVaporPressureHectopascals2 = ComputeSaturationVaporPressureHectopascals(airTemperatureCelsius);
		ValidateVaporPressure(saturationVaporPressureHectopascals, atmosphericPressureHectopascals, "waterTemperatureCelsius", allowZero: false);
		ValidateVaporPressure(relativeHumidity * saturationVaporPressureHectopascals2, atmosphericPressureHectopascals, "relativeHumidity", allowZero: true);
		float latentHeatOfVaporizationJoulesPerKilogram = ComputeLatentHeatOfVaporizationJoulesPerKilogram(waterTemperatureCelsius);
		float relativeHumidityTerm = MathF.FusedMultiplyAdd(0f - relativeHumidity, saturationVaporPressureHectopascals2, saturationVaporPressureHectopascals);
		return 0f - waterDensityKilogramsPerCubicMeter * latentHeatOfVaporizationJoulesPerKilogram * windFunctionMetersPerSecond * relativeHumidityTerm;
	}

	public static float ComputeWindFunction(float waterTemperatureCelsius, float airTemperatureCelsius, float relativeHumidity, float atmosphericPressureHectopascals, float windSpeedMetersPerSecond, float windMeasurementHeightMeters, float windRoughnessHeightMeters, float windCoefficientAMetersPerSecond, float windCoefficientB, float windExponent)
	{
		return ComputeWindFunction(waterTemperatureCelsius, airTemperatureCelsius, relativeHumidity, atmosphericPressureHectopascals, windSpeedMetersPerSecond, windMeasurementHeightMeters, windRoughnessHeightMeters, windCoefficientAMetersPerSecond, windCoefficientB, windExponent, WaterSurfaceExchangeMode.RichardsonAdjusted);
	}

	public static float ComputeWindFunction(float waterTemperatureCelsius, float airTemperatureCelsius, float relativeHumidity, float atmosphericPressureHectopascals, float windSpeedMetersPerSecond, float windMeasurementHeightMeters, float windRoughnessHeightMeters, float windCoefficientAMetersPerSecond, float windCoefficientB, float windExponent, WaterSurfaceExchangeMode surfaceExchangeMode)
	{
		ValidateTemperatureCelsius(waterTemperatureCelsius, "waterTemperatureCelsius");
		ValidateTemperatureCelsius(airTemperatureCelsius, "airTemperatureCelsius");
		ValidateFraction(relativeHumidity, "relativeHumidity");
		ValidatePositive(atmosphericPressureHectopascals, "atmosphericPressureHectopascals");
		ValidateNonnegative(windSpeedMetersPerSecond, "windSpeedMetersPerSecond");
		ValidatePositive(windMeasurementHeightMeters, "windMeasurementHeightMeters");
		ValidatePositive(windRoughnessHeightMeters, "windRoughnessHeightMeters");
		ValidateNonnegative(windCoefficientAMetersPerSecond, "windCoefficientAMetersPerSecond");
		ValidateNonnegative(windCoefficientB, "windCoefficientB");
		ValidatePositive(windExponent, "windExponent");
		ValidateSurfaceExchangeMode(surfaceExchangeMode);
		if (windRoughnessHeightMeters >= windMeasurementHeightMeters)
		{
			throw new ArgumentOutOfRangeException("windRoughnessHeightMeters", windRoughnessHeightMeters, "Wind roughness height must be below the measurement height.");
		}
		if (windRoughnessHeightMeters >= 2f)
		{
			throw new ArgumentOutOfRangeException("windRoughnessHeightMeters", windRoughnessHeightMeters, "Wind roughness height must be below the 2 m reference height.");
		}
		float windSpeedMetersPerSecondTerm = windSpeedMetersPerSecond * MathF.Log(2f / windRoughnessHeightMeters) / MathF.Log(windMeasurementHeightMeters / windRoughnessHeightMeters);
		float windFunction2 = MathF.FusedMultiplyAdd(windCoefficientB, MathF.Pow(windSpeedMetersPerSecondTerm, windExponent), windCoefficientAMetersPerSecond);
		float windFunction;
		switch (surfaceExchangeMode)
		{
		case WaterSurfaceExchangeMode.RichardsonAdjusted:
		{
			float saturationVaporPressureHectopascals = ComputeSaturationVaporPressureHectopascals(waterTemperatureCelsius);
			float saturationVaporPressureHectopascals2 = ComputeSaturationVaporPressureHectopascals(airTemperatureCelsius);
			ValidateVaporPressure(saturationVaporPressureHectopascals, atmosphericPressureHectopascals, "waterTemperatureCelsius", allowZero: false);
			float relativeHumidityTerm = relativeHumidity * saturationVaporPressureHectopascals2;
			ValidateVaporPressure(relativeHumidityTerm, atmosphericPressureHectopascals, "relativeHumidity", allowZero: true);
			float airMinusSurfaceVaporPressureHectopascals = MathF.FusedMultiplyAdd(relativeHumidity, saturationVaporPressureHectopascals2, 0f - saturationVaporPressureHectopascals);
			float relativeMoistAirDensityDifference = ComputeRelativeMoistAirDensityDifference(waterTemperatureCelsius, airTemperatureCelsius, atmosphericPressureHectopascals, relativeHumidityTerm, airMinusSurfaceVaporPressureHectopascals);
			float richardsonNumber;
			if (windSpeedMetersPerSecondTerm != 0f)
			{
				richardsonNumber = -19.612f * relativeMoistAirDensityDifference / (windSpeedMetersPerSecondTerm * windSpeedMetersPerSecondTerm);
			}
			else
			{
				richardsonNumber = ((relativeMoistAirDensityDifference == 0f) ? 0f : MathF.CopySign(float.PositiveInfinity, 0f - relativeMoistAirDensityDifference));
			}
			windFunction = ComputeRichardsonStabilityFactor(richardsonNumber) * windFunction2;
			break;
		}
		case WaterSurfaceExchangeMode.RichardsonDisabled:
			windFunction = windFunction2;
			break;
		default:
			windFunction = 0.001f * windFunction2;
			break;
		}
		if (!float.IsFinite(windFunction))
		{
			throw new ArgumentOutOfRangeException("windCoefficientB", windCoefficientB, "Wind-function parameters produce a non-finite result.");
		}
		return windFunction;
	}

	public static float ComputeAtmosphericLongwaveHeatFluxUS(float airTemperatureFahrenheit, float cloudCover)
	{
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeAtmosphericLongwaveHeatFlux(WaterTemperatureUnitConversions.TemperatureToCelsius(airTemperatureFahrenheit), cloudCover));
	}

	public static float ComputeBackRadiationHeatFluxUS(float waterTemperatureFahrenheit)
	{
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeBackRadiationHeatFlux(WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit)));
	}

	public static float ComputeSensibleAirWaterHeatFluxUS(float airTemperatureFahrenheit, float waterTemperatureFahrenheit, float waterDensityPoundsPerCubicFoot, float airSpecificHeatBtuPerPoundFahrenheit, float sensibleHeatDiffusivityRatio, float windFunctionFeetPerSecond)
	{
		float temperatureToCelsius = WaterTemperatureUnitConversions.TemperatureToCelsius(airTemperatureFahrenheit);
		float temperatureToCelsius2 = WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit);
		ValidateTemperatureCelsius(temperatureToCelsius, "airTemperatureFahrenheit");
		ValidateTemperatureCelsius(temperatureToCelsius2, "waterTemperatureFahrenheit");
		ValidatePositive(waterDensityPoundsPerCubicFoot, "waterDensityPoundsPerCubicFoot");
		ValidatePositive(airSpecificHeatBtuPerPoundFahrenheit, "airSpecificHeatBtuPerPoundFahrenheit");
		ValidateRange(sensibleHeatDiffusivityRatio, 0.5f, 1.5f, "sensibleHeatDiffusivityRatio");
		ValidateNonnegative(windFunctionFeetPerSecond, "windFunctionFeetPerSecond");
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeSensibleAirWaterHeatFluxCore((airTemperatureFahrenheit - waterTemperatureFahrenheit) / 1.8f, waterDensityPoundsPerCubicFoot * 16.018463f, airSpecificHeatBtuPerPoundFahrenheit * 4186.801f, sensibleHeatDiffusivityRatio, windFunctionFeetPerSecond * 0.3048f));
	}

	public static float ComputeBowenSensibleAirWaterHeatFluxUS(float airTemperatureFahrenheit, float waterTemperatureFahrenheit, float atmosphericPressureInchesMercury, float waterDensityPoundsPerCubicFoot, float windFunctionFeetPerSecond)
	{
		float temperatureToCelsius = WaterTemperatureUnitConversions.TemperatureToCelsius(airTemperatureFahrenheit);
		float temperatureToCelsius2 = WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit);
		ValidateTemperatureCelsius(temperatureToCelsius, "airTemperatureFahrenheit");
		ValidateTemperatureCelsius(temperatureToCelsius2, "waterTemperatureFahrenheit");
		ValidatePositive(atmosphericPressureInchesMercury, "atmosphericPressureInchesMercury");
		ValidatePositive(waterDensityPoundsPerCubicFoot, "waterDensityPoundsPerCubicFoot");
		ValidateNonnegative(windFunctionFeetPerSecond, "windFunctionFeetPerSecond");
		float latentHeat = ComputeLatentHeatOfVaporizationJoulesPerKilogram(temperatureToCelsius2);
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeBowenSensibleAirWaterHeatFluxCore((airTemperatureFahrenheit - waterTemperatureFahrenheit) / 1.8f, atmosphericPressureInchesMercury * 33.863888f, waterDensityPoundsPerCubicFoot * 16.018463f, windFunctionFeetPerSecond * 0.3048f, latentHeat));
	}

	public static float ComputeEvaporativeHeatFluxUS(float waterTemperatureFahrenheit, float airTemperatureFahrenheit, float relativeHumidity, float atmosphericPressureInchesMercury, float waterDensityPoundsPerCubicFoot, float windFunctionFeetPerSecond)
	{
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeEvaporativeHeatFlux(WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit), WaterTemperatureUnitConversions.TemperatureToCelsius(airTemperatureFahrenheit), relativeHumidity, atmosphericPressureInchesMercury * 33.863888f, waterDensityPoundsPerCubicFoot * 16.018463f, windFunctionFeetPerSecond * 0.3048f));
	}

	public static float ComputeBowenEvaporativeHeatFluxUS(float waterTemperatureFahrenheit, float airTemperatureFahrenheit, float relativeHumidity, float atmosphericPressureInchesMercury, float waterDensityPoundsPerCubicFoot, float windFunctionFeetPerSecond)
	{
		return WaterTemperatureUnitConversions.HeatFluxToUS(ComputeBowenEvaporativeHeatFlux(WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit), WaterTemperatureUnitConversions.TemperatureToCelsius(airTemperatureFahrenheit), relativeHumidity, atmosphericPressureInchesMercury * 33.863888f, waterDensityPoundsPerCubicFoot * 16.018463f, windFunctionFeetPerSecond * 0.3048f));
	}

	public static float ComputeWindFunctionUS(float waterTemperatureFahrenheit, float airTemperatureFahrenheit, float relativeHumidity, float atmosphericPressureInchesMercury, float windSpeedFeetPerSecond, float windMeasurementHeightFeet, float windRoughnessHeightFeet, float windCoefficientAFeetPerSecond, float windCoefficientB, float windExponent)
	{
		return ComputeWindFunctionUS(waterTemperatureFahrenheit, airTemperatureFahrenheit, relativeHumidity, atmosphericPressureInchesMercury, windSpeedFeetPerSecond, windMeasurementHeightFeet, windRoughnessHeightFeet, windCoefficientAFeetPerSecond, windCoefficientB, windExponent, WaterSurfaceExchangeMode.RichardsonAdjusted);
	}

	public static float ComputeWindFunctionUS(float waterTemperatureFahrenheit, float airTemperatureFahrenheit, float relativeHumidity, float atmosphericPressureInchesMercury, float windSpeedFeetPerSecond, float windMeasurementHeightFeet, float windRoughnessHeightFeet, float windCoefficientAFeetPerSecond, float windCoefficientB, float windExponent, WaterSurfaceExchangeMode surfaceExchangeMode)
	{
		float temperatureToCelsius = WaterTemperatureUnitConversions.TemperatureToCelsius(waterTemperatureFahrenheit);
		float waterTemperatureFahrenheitTerm = (waterTemperatureFahrenheit - airTemperatureFahrenheit) / 1.8f;
		float airTemperatureCelsius = temperatureToCelsius - waterTemperatureFahrenheitTerm;
		return ComputeWindFunction(temperatureToCelsius, airTemperatureCelsius, relativeHumidity, atmosphericPressureInchesMercury * 33.863888f, windSpeedFeetPerSecond * 0.3048f, windMeasurementHeightFeet * 0.3048f, windRoughnessHeightFeet * 0.3048f, windCoefficientAFeetPerSecond * 0.3048f, WaterTemperatureUnitConversions.WindCoefficientBToSI(windCoefficientB, windExponent), windExponent, surfaceExchangeMode) / 0.3048f;
	}

	internal static float ComputeSaturationVaporPressureHectopascals(float temperatureCelsius)
	{
		ValidateTemperatureCelsius(temperatureCelsius, "temperatureCelsius");
		float y = MathF.FusedMultiplyAdd(temperatureCelsius, 6.136821E-11f, 2.034081E-08f);
		y = MathF.FusedMultiplyAdd(temperatureCelsius, y, 3.0312403E-06f);
		y = MathF.FusedMultiplyAdd(temperatureCelsius, y, 0.00026506485f);
		y = MathF.FusedMultiplyAdd(temperatureCelsius, y, 0.014289458f);
		y = MathF.FusedMultiplyAdd(temperatureCelsius, y, 0.44365185f);
		return MathF.FusedMultiplyAdd(temperatureCelsius, y, 6.1078f);
	}

	internal static float ComputeLatentHeatOfVaporizationJoulesPerKilogram(float temperatureCelsius)
	{
		ValidateTemperatureCelsius(temperatureCelsius, "temperatureCelsius");
		float y = MathF.FusedMultiplyAdd(-6E-05f, temperatureCelsius, 0.0016f);
		y = MathF.FusedMultiplyAdd(temperatureCelsius, y, -2.36f);
		float latentHeatOfVaporizationJoulesPerKilogram = 1000f * MathF.FusedMultiplyAdd(temperatureCelsius, y, 2500.8f);
		if (latentHeatOfVaporizationJoulesPerKilogram <= 0f || !float.IsFinite(latentHeatOfVaporizationJoulesPerKilogram))
		{
			throw new ArgumentOutOfRangeException("temperatureCelsius", temperatureCelsius, "Temperature must produce a positive finite latent heat of vaporization.");
		}
		return latentHeatOfVaporizationJoulesPerKilogram;
	}

	internal static float ComputeRichardsonStabilityFactor(float richardsonNumber)
	{
		if (float.IsNaN(richardsonNumber))
		{
			throw new ArgumentOutOfRangeException("richardsonNumber", richardsonNumber, "Value must not be NaN.");
		}
		float richardsonNumberTerm = Math.Clamp(richardsonNumber, -1f, 2f);
		if (richardsonNumberTerm < -0.01f)
		{
			return MathF.Pow(1f - 22f * richardsonNumberTerm, 0.8f);
		}
		if (richardsonNumberTerm <= 0.01f)
		{
			return 1f;
		}
		return MathF.Pow(1f + 34f * richardsonNumberTerm, -0.8f);
	}

	private static float ComputeSensibleAirWaterHeatFluxCore(float temperatureDifferenceCelsius, float waterDensity, float airSpecificHeat, float sensibleHeatDiffusivityRatio, float windFunction)
	{
		return sensibleHeatDiffusivityRatio * airSpecificHeat * waterDensity * temperatureDifferenceCelsius * windFunction;
	}

	private static float ComputeBowenSensibleAirWaterHeatFluxCore(float temperatureDifferenceCelsius, float atmosphericPressureHectopascals, float waterDensity, float windFunction, float latentHeat)
	{
		float scaledAtmosphericPressureHectopascals = atmosphericPressureHectopascals / 1013.25f;
		return waterDensity * latentHeat * windFunction * 0.61f * scaledAtmosphericPressureHectopascals * temperatureDifferenceCelsius;
	}

	private static float ComputeRelativeMoistAirDensityDifference(float waterTemperatureCelsius, float airTemperatureCelsius, float atmosphericPressureHectopascals, float airVaporPressureHectopascals, float airMinusSurfaceVaporPressureHectopascals)
	{
		float y = airTemperatureCelsius + 273.15f;
		float waterTemperatureCelsiusOffset = waterTemperatureCelsius + 273.15f;
		float airVaporPressureHectopascalsTerm = MathF.FusedMultiplyAdd(-0.0013168431f, airVaporPressureHectopascals, atmosphericPressureHectopascals / 287.05f);
		float z = airVaporPressureHectopascalsTerm * (waterTemperatureCelsius - airTemperatureCelsius);
		return MathF.FusedMultiplyAdd(-0.0013168431f * airMinusSurfaceVaporPressureHectopascals, y, z) / (airVaporPressureHectopascalsTerm * waterTemperatureCelsiusOffset);
	}

	private static void ValidateVaporPressure(float vaporPressureHectopascals, float atmosphericPressureHectopascals, string parameterName, bool allowZero)
	{
		if ((allowZero ? (vaporPressureHectopascals < 0f) : (vaporPressureHectopascals <= 0f)) || !float.IsFinite(vaporPressureHectopascals) || vaporPressureHectopascals >= atmosphericPressureHectopascals)
		{
			throw new ArgumentOutOfRangeException(parameterName, "Vapor pressure must be nonnegative, finite, and below atmospheric pressure.");
		}
	}

	private static void ValidateSurfaceExchangeMode(WaterSurfaceExchangeMode surfaceExchangeMode)
	{
		if (surfaceExchangeMode == WaterSurfaceExchangeMode.RichardsonAdjusted || surfaceExchangeMode == WaterSurfaceExchangeMode.RichardsonDisabled || surfaceExchangeMode == WaterSurfaceExchangeMode.Bowen)
		{
			return;
		}
		throw new ArgumentOutOfRangeException("surfaceExchangeMode", surfaceExchangeMode, "Unsupported water-surface exchange mode.");
	}

	private static void ValidateTemperatureCelsius(float computed, string parameterName)
	{
		ValidateFinite(computed, parameterName);
		if (computed <= -273.15f)
		{
			throw new ArgumentOutOfRangeException(parameterName, computed, "Temperature must be above absolute zero.");
		}
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

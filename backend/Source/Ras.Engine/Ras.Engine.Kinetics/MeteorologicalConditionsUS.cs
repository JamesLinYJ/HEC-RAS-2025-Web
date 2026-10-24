namespace Ras.Engine.Kinetics;

public readonly struct MeteorologicalConditionsUS
{
	public float AirTemperature { get; init; }

	public float RelativeHumidity { get; init; }

	public float AtmosphericPressure { get; init; }

	public float WindSpeed { get; init; }

	public float WindMeasurementHeight { get; init; }

	public float CloudCover { get; init; }

	public float IncidentShortwave { get; init; }
}

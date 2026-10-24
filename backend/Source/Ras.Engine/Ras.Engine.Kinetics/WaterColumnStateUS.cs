namespace Ras.Engine.Kinetics;

public readonly struct WaterColumnStateUS
{
	public float WaterTemperature { get; init; }

	public float WaterDepth { get; init; }

	public float SurfaceArea { get; init; }

	public float SedimentArea { get; init; }

	public float Volume { get; init; }

	public float WaterDensity { get; init; }

	public float WaterSpecificHeat { get; init; }
}

namespace Ras.Engine.Kinetics;

public readonly struct WaterSurfaceExchangeParametersSI
{
	public float WindCoefficientA { get; init; }

	public float WindCoefficientB { get; init; }

	public float WindExponent { get; init; }

	public float WindRoughnessHeight { get; init; }

	public float AirSpecificHeat { get; init; }

	public float SensibleHeatDiffusivityRatio { get; init; }

	public WaterSurfaceExchangeMode SurfaceExchangeMode { get; init; }
}

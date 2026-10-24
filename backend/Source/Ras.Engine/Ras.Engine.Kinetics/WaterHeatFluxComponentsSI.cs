namespace Ras.Engine.Kinetics;

public readonly struct WaterHeatFluxComponentsSI
{
	public float SolarRadiation { get; init; }

	public float AtmosphericLongwave { get; init; }

	public float BackRadiation { get; init; }

	public float SensibleAirWater { get; init; }

	public float LatentEvaporation { get; init; }

	public float Sediment { get; init; }

	public float NetSurfaceHeatFlux => WaterHeatBudget.SumSurfaceHeatFluxes(SolarRadiation, AtmosphericLongwave, BackRadiation, SensibleAirWater, LatentEvaporation);
}

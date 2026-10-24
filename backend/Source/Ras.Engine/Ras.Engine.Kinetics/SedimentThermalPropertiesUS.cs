namespace Ras.Engine.Kinetics;

public readonly struct SedimentThermalPropertiesUS
{
	public float SedimentTemperature { get; init; }

	public float ActiveLayerThickness { get; init; }

	public float Density { get; init; }

	public float SpecificHeat { get; init; }

	public float ThermalDiffusivity { get; init; }
}

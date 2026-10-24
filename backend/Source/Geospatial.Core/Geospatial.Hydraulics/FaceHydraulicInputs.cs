namespace Geospatial.Hydraulics;

internal readonly struct FaceHydraulicInputs
{
	internal float Gravity { get; }

	internal float ManningUnitFactor { get; }

	internal float VegetationStemDensity { get; }

	internal float VegetationDragCoefficient { get; }

	internal float VegetationStemDiameter { get; }

	internal FaceHydraulicInputs(float gravity, float manningUnitFactor, float vegetationStemDensity, float vegetationDragCoefficient, float vegetationStemDiameter)
	{
		Gravity = gravity;
		ManningUnitFactor = manningUnitFactor;
		VegetationStemDensity = vegetationStemDensity;
		VegetationDragCoefficient = vegetationDragCoefficient;
		VegetationStemDiameter = vegetationStemDiameter;
	}
}

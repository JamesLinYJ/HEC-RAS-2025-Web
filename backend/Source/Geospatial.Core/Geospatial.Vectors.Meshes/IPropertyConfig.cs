namespace Geospatial.Vectors.Meshes;

public interface IPropertyConfig
{
	static abstract bool StateHasCellArea();

	static abstract bool StateHasFaceHydraulicRadius();

	static abstract bool StateHasCellHydraulicDepth();

	static abstract bool StateHasFaceHydraulicDepth();

	static abstract bool StateHasFaceConveyance();

	static abstract bool StateHasFaceMannings();

	static abstract bool StateHasFaceShearFluxCoeff();
}

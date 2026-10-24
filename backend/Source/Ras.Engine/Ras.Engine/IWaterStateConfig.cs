using Geospatial.Vectors.Meshes;

namespace Ras.Engine;

public interface IWaterStateConfig : IPropertyConfig
{
	static abstract bool StateHasFaceFlow();

	static abstract bool StateHasFaceVelTang();

	static abstract bool StateHasFaceVelMag();

	static abstract bool StateHasLocalTimeStepping();
}

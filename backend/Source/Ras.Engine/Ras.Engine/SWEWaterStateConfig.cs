using System.Runtime.InteropServices;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct SWEWaterStateConfig : IWaterStateConfig, IPropertyConfig
{
	public static bool StateHasFaceFlow()
	{
		return true;
	}

	public static bool StateHasCellArea()
	{
		return true;
	}

	public static bool StateHasFaceVelTang()
	{
		return true;
	}

	public static bool StateHasFaceVelMag()
	{
		return true;
	}

	public static bool StateHasFaceShearFluxCoeff()
	{
		return true;
	}

	public static bool StateHasFaceHydraulicRadius()
	{
		return true;
	}

	public static bool StateHasCellHydraulicDepth()
	{
		return true;
	}

	public static bool StateHasLocalTimeStepping()
	{
		return true;
	}

	public static bool StateHasFaceHydraulicDepth()
	{
		return false;
	}

	public static bool StateHasFaceConveyance()
	{
		return false;
	}

	public static bool StateHasFaceMannings()
	{
		return false;
	}
}

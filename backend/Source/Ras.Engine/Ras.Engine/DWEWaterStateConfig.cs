using System.Runtime.InteropServices;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public readonly struct DWEWaterStateConfig : IWaterStateConfig, IPropertyConfig
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
		return false;
	}

	public static bool StateHasFaceVelMag()
	{
		return false;
	}

	public static bool StateHasFaceShearFluxCoeff()
	{
		return false;
	}

	public static bool StateHasFaceHydraulicRadius()
	{
		return false;
	}

	public static bool StateHasCellHydraulicDepth()
	{
		return false;
	}

	public static bool StateHasLocalTimeStepping()
	{
		return false;
	}

	public static bool StateHasFaceHydraulicDepth()
	{
		return false;
	}

	public static bool StateHasFaceConveyance()
	{
		return true;
	}

	public static bool StateHasFaceMannings()
	{
		return false;
	}
}

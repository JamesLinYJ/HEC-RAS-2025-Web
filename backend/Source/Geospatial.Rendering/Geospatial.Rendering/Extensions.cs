using System.Drawing;

namespace Geospatial.Rendering;

public static class Extensions
{
	public static int ToARGB(this Color clr)
	{
		return (clr.A << 24) | (clr.R << 16) | (clr.G << 8) | clr.B;
	}
}

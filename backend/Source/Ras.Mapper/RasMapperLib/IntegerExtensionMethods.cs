using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class IntegerExtensionMethods
{
	public static int Bound(this int src, int min, int max)
	{
		if (src < min)
		{
			return min;
		}
		if (src > max)
		{
			return max;
		}
		return src;
	}
}

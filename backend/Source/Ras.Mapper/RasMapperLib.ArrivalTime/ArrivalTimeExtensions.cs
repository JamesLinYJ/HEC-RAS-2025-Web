using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.ArrivalTime;

[StandardModule]
internal static class ArrivalTimeExtensions
{
	public static ExtremaType SwapType(this ExtremaType eType)
	{
		if (eType == ExtremaType.Maxima)
		{
			return ExtremaType.Minima;
		}
		return ExtremaType.Maxima;
	}
}

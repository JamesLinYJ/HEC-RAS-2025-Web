using System.Diagnostics;

namespace Ras.Mapping.ArrivalTime;

[DebuggerDisplay("{ToString()}")]
public struct LocalExtrema(ExtremaType extrema, int time)
{
	public ExtremaType Extrema = extrema;

	public int Time = time;

	public override string ToString()
	{
		return Extrema.ToString() + " at time " + Time;
	}
}

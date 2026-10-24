using System.Diagnostics;

namespace RasMapperLib.ArrivalTime;

[DebuggerDisplay("{ToString()}")]
public struct LocalExtrema
{
	public ExtremaType Extrema;

	public int Time;

	public LocalExtrema(ExtremaType extrema, int time)
	{
		this = default;
		Extrema = extrema;
		Time = time;
	}

	public override string ToString()
	{
		return Extrema.ToString() + " at time " + Time;
	}
}

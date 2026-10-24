using System.Diagnostics;

namespace Ras.Mapping.ArrivalTime;

[DebuggerDisplay("{ToString()}")]
public struct Window(int startTime, int stopTime, float min, float max, Slope slope)
{
	public int StartTime = startTime;

	public int StopTime = stopTime;

	public float MinValue = min;

	public float MaxValue = max;

	public Slope Slope = slope;

	public string DirectionString()
	{
		if (Slope == Slope.Decreasing)
		{
			return "Decreasing";
		}
		if (Slope == Slope.Increasing)
		{
			return "Increasing";
		}
		if (Slope == Slope.Indeterminate)
		{
			return "Indeterminate";
		}
		return "N/A";
	}

	public override string ToString()
	{
		return StartTime + "->" + StopTime + " " + DirectionString() + " [" + MinValue + ", " + MaxValue + "]";
	}
}

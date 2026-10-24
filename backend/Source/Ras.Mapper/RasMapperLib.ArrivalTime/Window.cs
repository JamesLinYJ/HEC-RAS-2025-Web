using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RasMapperLib.ArrivalTime;

[ComVisible(false)]
[DebuggerDisplay("{ToString()}")]
public struct Window
{
	public int StartTime;

	public int StopTime;

	public float MinValue;

	public float MaxValue;

	public Slope Slope;

	public Window(int startTime, int stopTime, float min, float max, Slope slope)
	{
		this = default;
		MinValue = min;
		MaxValue = max;
		StartTime = startTime;
		StopTime = stopTime;
		Slope = slope;
	}

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

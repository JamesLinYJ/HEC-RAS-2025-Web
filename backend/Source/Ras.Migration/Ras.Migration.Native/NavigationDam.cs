using System.Collections.Generic;

namespace Ras.Migration.Native;

public class NavigationDam
{
	public float NormalTimeIncr;

	public float RapidlyVaryingIncr;

	public float InitialChangeTime;

	public float GateOpenMin;

	public float GateOpenRate;

	public float GateCloseRate;

	public string Type;

	public float GateOpenMax;

	public float[] Flow;

	public float[] WSMax;

	public float[] WSMin;

	public List<RRS> FlowMonitors = new List<RRS>();

	public RRS HingePointRRS;

	public string HingePointLine;

	public string MinPoolLine;

	public string MaxPoolLine;

	public NavigationDam(string[] line)
	{
		NormalTimeIncr = FloatOrNaN(line, 0);
		RapidlyVaryingIncr = FloatOrNaN(line, 1);
		InitialChangeTime = FloatOrNaN(line, 2);
		GateOpenMin = FloatOrNaN(line, 3);
		GateOpenRate = FloatOrNaN(line, 4);
		GateCloseRate = FloatOrNaN(line, 5);
		Type = ItemOrBlank(line, 6);
		GateOpenMax = FloatOrNaN(line, 7);
	}

	private static string ItemOrBlank(string[] split, int idx)
	{
		if (idx < split.Length)
		{
			return split[idx].Trim();
		}
		return "";
	}

	private static float FloatOrNaN(string[] split, int idx)
	{
		if (idx < split.Length)
		{
			if (float.TryParse(split[idx].Trim(), out var result))
			{
				return result;
			}
			return float.NaN;
		}
		return float.NaN;
	}
}

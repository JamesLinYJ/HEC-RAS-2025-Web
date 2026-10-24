using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Utility.Progress;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

[DebuggerDisplay("{DebugDisplay()}")]
public class Component
{
	public readonly string Key;

	public readonly string Name;

	public readonly int RelativeCompositionPercentage;

	public readonly string MapUnitKey;

	public readonly List<Horizon> Horizons;

	public Component(string cokey, string coname, int relPct)
	{
		Key = cokey;
		Name = coname;
		RelativeCompositionPercentage = relPct;
		Horizons = new List<Horizon>();
	}

	public Component(string cokey, string coname, int relPct, string mukey)
	{
		Key = cokey;
		Name = coname;
		RelativeCompositionPercentage = relPct;
		MapUnitKey = mukey;
		Horizons = new List<Horizon>();
	}

	public Component(DataRow coRow)
	{
		Key = (string)coRow["cokey"];
		Name = (string)coRow["compname"];
		RelativeCompositionPercentage = (int)coRow["comppct_r"];
		Horizons = new List<Horizon>();
	}

	public Horizon TopHorizon()
	{
		if (Horizons.Count == 0)
		{
			return null;
		}
		int minValue = int.MinValue;
		Horizon result = null;
		for (int i = 0; i < Horizons.Count; i++)
		{
			Horizon horizon = Horizons[i];
			int depthTop = horizon.DepthTop;
			if (depthTop == 0)
			{
				return horizon;
			}
			if (depthTop < minValue)
			{
				result = horizon;
				minValue = depthTop;
			}
		}
		return result;
	}

	private string DebugDisplay()
	{
		return Name + ": " + RelativeCompositionPercentage + "% with " + Horizons.Count + " Horizons";
	}

	public bool ValidateHorizons()
	{
		int depthBottom = 0;
		foreach (Horizon orderBy in Horizons.OrderBy((Horizon h) => h.DepthTop))
		{
			if (orderBy.DepthTop != depthBottom)
			{
				return false;
			}
			depthBottom = orderBy.DepthBottom;
		}
		return true;
	}

	public static bool VerifyHorizonDepths(IList<Component> components, ProgressReporter prog)
	{
		int ordinal = 0;
		for (int i = 0; i < components.Count; i++)
		{
			if (!components[i].ValidateHorizons())
			{
				ordinal++;
				prog.ReportMessage("Bad Horizon data on component " + i);
			}
		}
		return ordinal == 0;
	}
}

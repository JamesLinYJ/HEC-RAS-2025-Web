using System.Collections.Generic;
using System.Data;
using System.Diagnostics;

namespace Geospatial.GDALAssist.Vectors.SSURGO;

[DebuggerDisplay("{Symbol} - {HydrologicSoilGroup}")]
public class MapUnit
{
	public readonly string Key;

	public readonly string Symbol;

	public readonly string HydrologicSoilGroup;

	public readonly List<Component> Components;

	public MapUnit(string mukey, string musym, string hydrSoilGrp)
	{
		Key = mukey;
		Symbol = musym;
		HydrologicSoilGroup = hydrSoilGrp;
		Components = new List<Component>();
	}

	public MapUnit(DataRow muRow)
	{
		Key = (string)muRow["mapunit.mukey"];
		Symbol = (string)muRow["mapunit.musym"];
		HydrologicSoilGroup = (string)muRow["muaggatt.hydgrpdcd"];
		Components = new List<Component>();
	}
}

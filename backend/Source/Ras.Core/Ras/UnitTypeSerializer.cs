using Utility.Units;

namespace Ras;

public static class UnitTypeSerializer
{
	public static string Serialize(UnitSystem ut)
	{
		return ut switch
		{
			UnitSystem.USCustomary => "USC", 
			UnitSystem.SI => "SI", 
			_ => "", 
		};
	}

	public static UnitSystem Deserialize(string units, UnitSystem fallback = UnitSystem.USCustomary)
	{
		if (units == "USC")
		{
			return UnitSystem.USCustomary;
		}
		if (units == "SI")
		{
			return UnitSystem.SI;
		}
		return fallback;
	}
}

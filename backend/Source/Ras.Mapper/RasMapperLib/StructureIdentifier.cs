using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class StructureIdentifier
{
	public string River;

	public string Reach;

	public string RiverStation;

	public string Connection;

	public string PumpStation;

	public static StructureIdentifier FromPumpStationName(string name)
	{
		return new StructureIdentifier("", "", "", "", name);
	}

	public StructureIdentifier(string riv, string rch, string rs, string conn, string pumpStat)
	{
		River = riv;
		Reach = rch;
		RiverStation = rs;
		Connection = conn;
		PumpStation = pumpStat;
	}

	public StructureIdentifier(string riv, string rch, string rs)
	{
		River = riv;
		Reach = rch;
		RiverStation = rs;
	}

	private StructureIdentifier()
	{
	}

	public static StructureIdentifier NewConnectionID(string conn)
	{
		return new StructureIdentifier
		{
			Connection = conn
		};
	}

	public static StructureIdentifier NewPumpStationID(string pumpStation)
	{
		return new StructureIdentifier
		{
			PumpStation = pumpStation
		};
	}

	public bool IsConnection()
	{
		if (string.IsNullOrEmpty(Connection))
		{
			return false;
		}
		return true;
	}

	public bool IsLateralStructure()
	{
		return !IsConnection();
	}

	public override int GetHashCode()
	{
		return (River + Reach + RiverStation + Connection).GetHashCode();
	}

	public override bool Equals(object obj)
	{
		return Matches(obj as StructureIdentifier);
	}

	public bool Matches(StructureIdentifier otherStructure)
	{
		if (otherStructure == null)
		{
			return false;
		}
		if (string.IsNullOrEmpty(Connection))
		{
			if (string.IsNullOrEmpty(PumpStation))
			{
				if (string.IsNullOrEmpty(River) && string.IsNullOrEmpty(Reach) && string.IsNullOrEmpty(RiverStation))
				{
					return false;
				}
				return Operators.CompareString(River.Trim(), otherStructure.River.Trim(), TextCompare: false) == 0 && Operators.CompareString(Reach.Trim(), otherStructure.Reach.Trim(), TextCompare: false) == 0 && Operators.CompareString(RiverStation.Trim(), otherStructure.RiverStation.Trim(), TextCompare: false) == 0;
			}
			return Operators.CompareString(PumpStation.Trim(), otherStructure.PumpStation.Trim(), TextCompare: false) == 0;
		}
		return Operators.CompareString(Connection.Trim(), otherStructure.Connection.Trim(), TextCompare: false) == 0;
	}
}

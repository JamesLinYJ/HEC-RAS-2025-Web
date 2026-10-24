using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class StructureGroupIdentifier
{
	public StructureIdentifier StructureID;

	public string GroupName;

	public StructureGroupIdentifier(StructureIdentifier structureID, string groupName)
	{
		StructureID = structureID;
		GroupName = groupName;
	}

	public StructureGroupIdentifier(string riv, string rch, string rs, string conn, string groupname)
	{
		StructureID = new StructureIdentifier(riv, rch, rs, conn, "");
		GroupName = groupname;
	}

	public override int GetHashCode()
	{
		return (StructureID.River + StructureID.Reach + StructureID.RiverStation + StructureID.Connection + GroupName).GetHashCode();
	}

	public override bool Equals(object obj)
	{
		return Matches(obj as StructureGroupIdentifier);
	}

	public bool Matches(StructureGroupIdentifier otherGroup)
	{
		if (StructureID.Matches(otherGroup.StructureID) && Operators.CompareString(GroupName, otherGroup.GroupName, TextCompare: false) == 0)
		{
			return true;
		}
		return false;
	}
}

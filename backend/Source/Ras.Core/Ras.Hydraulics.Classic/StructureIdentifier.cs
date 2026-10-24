using Utility.Hashing;
using Utility.ORM;

namespace Ras.Hydraulics.Classic;

public class StructureIdentifier : IDataHash
{
	[HashContributor]
	[Metadata("River")]
	public string RiverName { get; set; }

	[HashContributor]
	[Metadata("Reach")]
	public string ReachName { get; set; }

	[HashContributor]
	[Metadata("RS")]
	public string RiverStation { get; set; }

	[HashContributor]
	[Metadata("Type")]
	public StructureType Type { get; set; }

	[HashContributor]
	[Metadata("Connection")]
	public string ConnectionName { get; set; }

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public bool Matches(StructureIdentifier other)
	{
		if (string.IsNullOrEmpty(ConnectionName))
		{
			return ConnectionName == other.ConnectionName;
		}
		if (RiverName == other.RiverName && ReachName == other.ReachName)
		{
			return RiverStation == other.RiverStation;
		}
		return false;
	}

	public override string ToString()
	{
		if (string.IsNullOrEmpty(ConnectionName))
		{
			return RiverName + " " + ReachName + " " + RiverStation + " (" + Type.ToString() + ")";
		}
		return ConnectionName + " (" + Type.ToString() + ")";
	}
}

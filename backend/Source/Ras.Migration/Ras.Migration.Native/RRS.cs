namespace Ras.Migration.Native;

public struct RRS
{
	public string RiverName;

	public string ReachName;

	public string RiverStation;

	public RRS(string riv, string rch, string rs)
	{
		RiverName = riv;
		ReachName = rch;
		RiverStation = rs;
	}

	public RRS(string csv)
		: this(csv.Split(','))
	{
	}

	public RRS(string[] split)
	{
		RiverName = split[0];
		ReachName = split[1];
		RiverStation = split[2];
	}
}

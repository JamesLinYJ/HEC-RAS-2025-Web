namespace Ras.Migration.Native;

public struct RRSValue(string riv, string rch, string rs, float v)
{
	public string RiverName = riv;

	public string ReachName = rch;

	public string RiverStation = rs;

	public float Value = v;
}

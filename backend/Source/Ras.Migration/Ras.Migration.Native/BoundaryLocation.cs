using System.Diagnostics;

namespace Ras.Migration.Native;

[DebuggerDisplay("{ToPrettyString()}")]
public struct BoundaryLocation(string[] data)
{
	public string River = ItemOrBlank(data, 0);

	public string Reach = ItemOrBlank(data, 1);

	public string RS = ItemOrBlank(data, 2);

	public string LowerRS = ItemOrBlank(data, 3);

	public string Connection = ItemOrBlank(data, 4);

	public string StorageArea = ItemOrBlank(data, 5);

	public string PumpStation = ItemOrBlank(data, 6);

	public string BCLine = ItemOrBlank(data, 7);

	public string PipeNode = ItemOrBlank(data, 8);

	private static string ItemOrBlank(string[] split, int idx)
	{
		if (idx < split.Length)
		{
			return split[idx].Trim();
		}
		return "";
	}

	public string ToPrettyString()
	{
		return $"{River}, {Reach}, {RS}-{LowerRS} | {Connection} | {StorageArea}";
	}
}

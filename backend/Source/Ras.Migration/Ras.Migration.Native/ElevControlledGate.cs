namespace Ras.Migration.Native;

public class ElevControlledGate
{
	public string Name;

	public float US_WSOpen;

	public float US_WSClose;

	public float OpenRate;

	public float CloseRate;

	public float MaxOpenHeight;

	public float MinOpenHeight;

	public float InitialHeight;

	public string ReferenceMode;

	public float ReferenceWsOpen;

	public float ReferenceWsClose;

	public string ReferenceWsRiver;

	public string ReferenceWsReach;

	public string ReferenceWsRs;

	public string ReferenceWsStorage;

	public float StageDiffOpen;

	public float StageDiffClose;

	public string StageDiffUsRiver;

	public string StageDiffUsReach;

	public string StageDiffUsRs;

	public string StageDiffUsStorage;

	public string StageDiffDsRiver;

	public string StageDiffDsReach;

	public string StageDiffDsRs;

	public string StageDiffDsStorage;

	public ElevControlledGate(string[] line1, string[] line2, string[] line3)
	{
		Name = ItemOrBlank(line1, 0);
		US_WSOpen = FloatOrNaN(line1, 1);
		US_WSClose = FloatOrNaN(line1, 2);
		OpenRate = FloatOrNaN(line1, 3);
		CloseRate = FloatOrNaN(line1, 4);
		MaxOpenHeight = FloatOrNaN(line1, 5);
		MinOpenHeight = FloatOrNaN(line1, 6);
		InitialHeight = FloatOrNaN(line1, 7);
		ReferenceMode = ItemOrBlank(line1, 8);
		ReferenceWsOpen = FloatOrNaN(line2, 0);
		ReferenceWsClose = FloatOrNaN(line2, 1);
		ReferenceWsRiver = ItemOrBlank(line2, 2);
		ReferenceWsReach = ItemOrBlank(line2, 3);
		ReferenceWsRs = ItemOrBlank(line2, 4);
		ReferenceWsStorage = ItemOrBlank(line2, 5);
		StageDiffOpen = FloatOrNaN(line3, 0);
		StageDiffClose = FloatOrNaN(line3, 1);
		StageDiffUsRiver = ItemOrBlank(line3, 2);
		StageDiffUsReach = ItemOrBlank(line3, 3);
		StageDiffUsRs = ItemOrBlank(line3, 4);
		StageDiffUsStorage = ItemOrBlank(line3, 5);
		StageDiffDsRiver = ItemOrBlank(line3, 6);
		StageDiffDsReach = ItemOrBlank(line3, 7);
		StageDiffDsRs = ItemOrBlank(line3, 8);
		StageDiffDsStorage = ItemOrBlank(line3, 9);
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

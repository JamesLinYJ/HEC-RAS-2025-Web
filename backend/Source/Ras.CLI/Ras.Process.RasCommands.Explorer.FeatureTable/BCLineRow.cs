using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;

namespace Ras.Process.RasCommands.Explorer.FeatureTable;

public struct BCLineRow
{
	public string Name;

	public bool IsEnabled;

	public string HydraulicType;

	public string HydraulicValue;

	public int PayloadCount;

	public string Description;

	public static BCLineRow FromBCLine(BoundaryConditionLine bcl)
	{
		BCLineRow result = new BCLineRow
		{
			Name = (bcl.Name ?? ""),
			IsEnabled = bcl.IsEnabled,
			PayloadCount = (bcl.Payloads?.Count ?? 0),
			Description = (bcl.Description ?? "")
		};
		if ((bool)bcl.TryGetHydraulicPayload(out var pl))
		{
			result.HydraulicType = pl.Variable ?? "Unknown";
			result.HydraulicValue = GetPayloadValueString(pl);
		}
		else
		{
			result.HydraulicType = "(none)";
			result.HydraulicValue = "";
		}
		return result;
	}

	private static string GetPayloadValueString(PayloadBase payload)
	{
		if (payload == null)
		{
			return "";
		}
		if (payload is ConstantPayload { Value: var localValue })
		{
			return localValue.ToString("G5");
		}
		if (payload is ExternalPayload externalPayload)
		{
			return "[" + (externalPayload.ExternalConnection ?? "External") + "]";
		}
		if (payload is TimeSeriesPayload { Count: var count })
		{
			return $"[{count} pts]";
		}
		return payload.GetType().Name.Replace("Payload", "");
	}
}

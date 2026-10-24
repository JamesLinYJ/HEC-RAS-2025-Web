using Utility.Extensions;

namespace Ras.Layers.Payloads;

public static class WellKnownPayloadVariables
{
	public static string[] HydraulicPayloadVariables = new string[4] { "Flow", "Stage", "StageVelocityVelocity", "Normal Depth" };

	public const string Unknown = "Unknown";

	public const string Flow = "Flow";

	public const string Stage = "Stage";

	public const string StageVelocityVelocity = "StageVelocityVelocity";

	public const string NormalDepth = "Normal Depth";

	public const string Nitrogen = "Nitrogen";

	public const string SedimentPrefix = "Sediment";

	public static bool IsHydraulicPayload(PayloadBase payload)
	{
		return IsHydraulicPayload(payload.Variable);
	}

	public static bool IsHydraulicPayload(string payloadVariable)
	{
		for (int i = 0; i < HydraulicPayloadVariables.Length; i++)
		{
			if (HydraulicPayloadVariables[i].RoughlyEquals(payloadVariable))
			{
				return true;
			}
		}
		return false;
	}
}

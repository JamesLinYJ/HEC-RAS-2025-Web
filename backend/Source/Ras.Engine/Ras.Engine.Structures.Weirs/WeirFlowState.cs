using System;
using System.Text.Json;

namespace Ras.Engine.Structures.Weirs;

public class WeirFlowState
{
	public double[] Q;

	public double WSEUS { get; set; }

	public double WSEDS { get; set; }

	public double VelUS { get; set; }

	public double VelDS { get; set; }

	public double CoefWeirSubmergence { get; set; }

	public double CoefOgeeHead { get; set; }

	public double Submergence { get; set; }

	public void AllocateFaceFlows(int nFaces)
	{
		Q = new double[nFaces];
	}

	public string ToJson()
	{
		JsonSerializerOptions options = new JsonSerializerOptions();
		return JsonSerializer.Serialize(this, options);
	}

	public static WeirFlowState FromJson(string json)
	{
		return JsonSerializer.Deserialize<WeirFlowState>(json) ?? throw new InvalidOperationException("Failed to deserialize WeirFlowState");
	}
}

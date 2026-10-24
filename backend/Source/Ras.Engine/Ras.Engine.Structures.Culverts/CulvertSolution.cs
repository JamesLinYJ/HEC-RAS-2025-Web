using System;
using System.Text.Json;

namespace Ras.Engine.Structures.Culverts;

public class CulvertSolution
{
	public class Critical
	{
		public double Depth { get; set; }

		public double VelHead { get; set; }

		public double EG { get; set; }

		public double EGDepth { get; set; }
	}

	public double Qtotal
	{
		get
		{
			if (!IsReverseFlow)
			{
				return Qoriented;
			}
			return 0.0 - Qoriented;
		}
	}

	public bool IsReverseFlow { get; set; }

	public double Qoriented { get; set; }

	public double EGInletControl { get; set; }

	public double EGOutletControl { get; set; }

	public bool IsOutletControl { get; set; }

	public double DepthInlet { get; set; }

	public double WSEInlet { get; set; }

	public double EGUS { get; set; }

	public double EGDS { get; set; }

	public double WSEUS { get; set; }

	public double WSEDS { get; set; }

	public double WSEOutlet { get; set; }

	public double WSEOutletSup { get; set; }

	public double VelHeadUS { get; set; }

	public double VelHeadDS { get; set; }

	public double EntranceLoss { get; set; }

	public double ExitLoss { get; set; }

	public double FrictionLoss { get; set; }

	public double NormalDepth { get; set; }

	public double MaxDepthQ { get; set; }

	public double[] DepthInternal { get; set; }

	public Critical Crit { get; set; } = new Critical();

	public CulvertSolution(CulvertSolverControl control)
	{
		InitInternalDepthArray(control.NumProfileStations);
	}

	private void InitInternalDepthArray(int count)
	{
		DepthInternal = new double[count];
	}

	public string ToJson()
	{
		JsonSerializerOptions options = new JsonSerializerOptions();
		return JsonSerializer.Serialize(this, options);
	}

	public static CulvertSolution FromJson(string json)
	{
		return JsonSerializer.Deserialize<CulvertSolution>(json) ?? throw new InvalidOperationException("Failed to deserialize CulvertState");
	}
}

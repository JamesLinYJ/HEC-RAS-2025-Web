using System;
using System.Text.Json;

namespace Ras.Engine.Structures.Culverts;

public class CulvertPhysProps : ICulvertGeometry
{
	public CulvertInletCoeff InletCoefUS { get; set; } = new CulvertInletCoeff();

	public CulvertInletCoeff InletCoefDS { get; set; } = new CulvertInletCoeff();

	public CulvertShape Shape { get; set; } = CulvertShape.Circular;

	public double FullRise { get; set; }

	public double Span { get; set; }

	public double Length { get; set; }

	public double Mann { get; set; } = 0.025;

	public double MannBottom { get; set; } = 0.025;

	public bool IsMannBottom { get; set; }

	public double DepthMannBottom { get; set; }

	public double DepthBlocked { get; set; }

	public double InvertUS { get; set; }

	public double InvertDS { get; set; }

	public double CoefEntranceLoss { get; set; } = 0.5;

	public double CoefEntranceLossReverse { get; set; } = 0.5;

	public double CoefExitLoss { get; set; } = 1.0;

	public double CoefExitLossReverse { get; set; } = 1.0;

	public bool IsPartiallyBlocked => DepthBlocked > 0.0;

	public double EffectiveRise => FullRise - DepthBlocked;

	public double Slope
	{
		get
		{
			double invertDrop = InvertUS - InvertDS;
			double computed = Length * Length - invertDrop * invertDrop;
			if (computed <= 0.0)
			{
				return 0.0;
			}
			return invertDrop / Math.Sqrt(computed);
		}
	}

	public FlowOrientedCulvert AsFlowOriented(bool reversed)
	{
		return new FlowOrientedCulvert(this, reversed);
	}

	public string ToJson()
	{
		JsonSerializerOptions options = new JsonSerializerOptions();
		return JsonSerializer.Serialize(this, options);
	}

	public static CulvertPhysProps FromJson(string json)
	{
		return JsonSerializer.Deserialize<CulvertPhysProps>(json) ?? throw new InvalidOperationException("Failed to deserialize CulvertPhysProps");
	}
}

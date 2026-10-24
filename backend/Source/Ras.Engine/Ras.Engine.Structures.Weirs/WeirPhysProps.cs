using System;
using System.Text.Json;
using Utility.Units;

namespace Ras.Engine.Structures.Weirs;

public class WeirPhysProps
{
	public WeirCrestShape Shape { get; set; }

	public double WeirCoef { get; set; } = 3.33;

	public double SubmergenceAcoef { get; set; } = 7.25;

	public double SubmergenceBcoef { get; set; } = 0.5;

	public double DesignHead { get; set; }

	public bool IsDesignHead { get; set; }

	public double Length { get; set; }

	public float[] FaceElevationTable { get; set; }

	public float[] FaceWettedPerimTable { get; set; }

	public double Grav { get; set; }

	public void SetUnitSys(UnitSystem unitSys)
	{
		if (unitSys == UnitSystem.USCustomary)
		{
			Grav = 32.174;
		}
		else
		{
			Grav = 9.80665;
		}
	}

	public string ToJson()
	{
		JsonSerializerOptions options = new JsonSerializerOptions();
		return JsonSerializer.Serialize(this, options);
	}

	public static WeirPhysProps FromJson(string json)
	{
		return JsonSerializer.Deserialize<WeirPhysProps>(json) ?? throw new InvalidOperationException("Failed to deserialize WeirPhysProps");
	}
}

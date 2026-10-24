using System;
using Utility.Units;

namespace Ras.Layers;

public class DefaultUnits
{
	public static readonly DefaultUnits SI = new DefaultUnits(UnitSystem.SI);

	public static readonly DefaultUnits USC = new DefaultUnits(UnitSystem.USCustomary);

	public const float GravityUSC = 32.174f;

	public const float GravitySI = 9.80665f;

	private const char Super2 = 'ý';

	private const char Super3 = 'ý';

	public float Gravity
	{
		get
		{
			if (Units == UnitSystem.SI)
			{
				return 9.80665f;
			}
			_ = Units;
			return 32.174f;
		}
	}

	public string LengthUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "feet";
			}
			if (Units == UnitSystem.SI)
			{
				return "meters";
			}
			return "feet";
		}
	}

	public Length TypedLengthUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return Length.Feet;
			}
			if (Units == UnitSystem.SI)
			{
				return Length.Meters;
			}
			return Length.Feet;
		}
	}

	public string ShortLengthUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "ft";
			}
			if (Units == UnitSystem.SI)
			{
				return "m";
			}
			return "ft";
		}
	}

	public Length DefaultLengthUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return Length.Feet;
			}
			if (Units == UnitSystem.SI)
			{
				return Length.Meters;
			}
			return Length.Feet;
		}
	}

	public string SmallLengthUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "in";
			}
			if (Units == UnitSystem.SI)
			{
				return "mm";
			}
			return "in";
		}
	}

	public Length PrecipLengthUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return Length.Inches;
			}
			if (Units == UnitSystem.SI)
			{
				return Length.Millimeters;
			}
			return Length.Inches;
		}
	}

	public PeriodAveragePrecipitation PrecipRateUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return PeriodAveragePrecipitation.InchesPerHour;
			}
			if (Units == UnitSystem.SI)
			{
				return PeriodAveragePrecipitation.MillimeterPerHour;
			}
			return PeriodAveragePrecipitation.InchesPerHour;
		}
	}

	public PeriodCumulativePrecipitation PrecipPeriodCumulativeUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return PeriodCumulativePrecipitation.Inches;
			}
			if (Units == UnitSystem.SI)
			{
				return PeriodCumulativePrecipitation.Millimeters;
			}
			return PeriodCumulativePrecipitation.Inches;
		}
	}

	public WindSpeed WindSpeedUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return WindSpeed.FeetPerSecond;
			}
			if (Units == UnitSystem.SI)
			{
				return WindSpeed.MetersPerSecond;
			}
			return WindSpeed.FeetPerSecond;
		}
	}

	public string ShortAreaUnits => ShortLengthUnits + "ý";

	public string ShortVolumeUnits => ShortLengthUnits + "ý";

	public string ShortVolumeUnits_SA
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "ac-ft";
			}
			if (Units == UnitSystem.SI)
			{
				return "1000 mý";
			}
			return "ac-ft";
		}
	}

	public string FlowUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "CFS";
			}
			return "CMS";
		}
	}

	public string VelocityUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "feet/sec";
			}
			if (Units == UnitSystem.SI)
			{
				return "meters/sec";
			}
			return "feet/sec";
		}
	}

	public string ShortVelocityUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "ft/s";
			}
			if (Units == UnitSystem.SI)
			{
				return "m/s";
			}
			return "ft/s";
		}
	}

	public string ShearUnits
	{
		get
		{
			if (Units == UnitSystem.USCustomary)
			{
				return "lbs/sq ft";
			}
			if (Units == UnitSystem.SI)
			{
				return "pascal";
			}
			return "lbs/sq ft";
		}
	}

	public UnitSystem Units { get; }

	public static DefaultUnits FromDefault(UnitSystem ut)
	{
		return ut switch
		{
			UnitSystem.USCustomary => USC, 
			UnitSystem.SI => SI, 
			_ => throw new NotSupportedException(), 
		};
	}

	public static string ShortLengthUnitsFromDefault(UnitSystem ut)
	{
		return FromDefault(ut).ShortLengthUnits;
	}

	private DefaultUnits(UnitSystem units)
	{
		Units = units;
	}
}

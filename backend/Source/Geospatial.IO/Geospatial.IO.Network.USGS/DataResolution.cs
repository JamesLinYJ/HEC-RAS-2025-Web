using System;

namespace Geospatial.IO.Network.USGS;

public readonly struct DataResolution : IEquatable<DataResolution>, IComparable<DataResolution>
{
	public double Meters { get; }

	public USGSDataResolution Category { get; }

	public static DataResolution OneMeter => new DataResolution(1.0, USGSDataResolution.OneMeter);

	public static DataResolution OneNinthArcSecond => new DataResolution(3.0, USGSDataResolution.OneNinthArcSecond);

	public static DataResolution FiveMeter => new DataResolution(5.0, USGSDataResolution.FiveMeter);

	public static DataResolution OneThirdArcSecond => new DataResolution(10.0, USGSDataResolution.OneThirdArcSecond);

	public static DataResolution OneArcSecond => new DataResolution(30.0, USGSDataResolution.OneArcSecond);

	public static DataResolution TwoArcSecond => new DataResolution(60.0, USGSDataResolution.TwoArcSecond);

	public static DataResolution Original => new DataResolution(0.0, USGSDataResolution.Original);

	public static DataResolution Unknown => new DataResolution(0.0, USGSDataResolution.Unknown);

	private DataResolution(double meters, USGSDataResolution category)
	{
		Meters = meters;
		Category = category;
	}

	public static DataResolution FromMeters(double meters)
	{
		if (meters <= 0.0)
		{
			return Unknown;
		}
		if (meters <= 1.1)
		{
			return OneMeter;
		}
		if (meters <= 3.5)
		{
			return OneNinthArcSecond;
		}
		if (meters <= 5.5)
		{
			return FiveMeter;
		}
		if (meters <= 11.0)
		{
			return OneThirdArcSecond;
		}
		if (meters <= 35.0)
		{
			return OneArcSecond;
		}
		if (meters <= 65.0)
		{
			return TwoArcSecond;
		}
		return new DataResolution(meters, USGSDataResolution.Custom);
	}

	public static DataResolution ParseFromText(string stringValue)
	{
		if (string.IsNullOrEmpty(stringValue))
		{
			return Unknown;
		}
		string replace = stringValue.ToLowerInvariant().Replace("one meter", "1 meter").Replace("five meter", "5 meter");
		if (replace.Contains("1/9 arc") || replace.Contains("1/9-arc") || replace.Contains("19 arc"))
		{
			return OneNinthArcSecond;
		}
		if (replace.Contains("1/3 arc") || replace.Contains("1/3-arc") || replace.Contains("13 arc"))
		{
			return OneThirdArcSecond;
		}
		if (replace.Contains("2 arc") || replace.Contains("2-arc"))
		{
			return TwoArcSecond;
		}
		if (replace.Contains("1 arc") || replace.Contains("1-arc"))
		{
			return OneArcSecond;
		}
		if (replace.Contains("5 meter") || replace.Contains("5m ") || replace.Contains("5m)"))
		{
			return FiveMeter;
		}
		if (replace.Contains("1 meter") || replace.Contains("1m ") || replace.Contains("1m)"))
		{
			return OneMeter;
		}
		if (replace.Contains("original"))
		{
			return Original;
		}
		return Unknown;
	}

	public static DataResolution FromCategory(USGSDataResolution category)
	{
		return category switch
		{
			USGSDataResolution.OneMeter => OneMeter, 
			USGSDataResolution.OneNinthArcSecond => OneNinthArcSecond, 
			USGSDataResolution.FiveMeter => FiveMeter, 
			USGSDataResolution.OneThirdArcSecond => OneThirdArcSecond, 
			USGSDataResolution.OneArcSecond => OneArcSecond, 
			USGSDataResolution.TwoArcSecond => TwoArcSecond, 
			USGSDataResolution.Original => Original, 
			_ => Unknown, 
		};
	}

	public string ToDisplayString()
	{
		return Category switch
		{
			USGSDataResolution.OneMeter => "1 meter", 
			USGSDataResolution.OneNinthArcSecond => "1/9 arc second", 
			USGSDataResolution.FiveMeter => "5 meter", 
			USGSDataResolution.OneThirdArcSecond => "1/3 arc second", 
			USGSDataResolution.OneArcSecond => "1 arc second", 
			USGSDataResolution.TwoArcSecond => "2 arc second", 
			USGSDataResolution.Custom => $"{Meters:F1} meter", 
			USGSDataResolution.Original => "Original", 
			_ => "Unknown", 
		};
	}

	public string GetSearchAliases()
	{
		return Category switch
		{
			USGSDataResolution.OneMeter => "1 meter 1m", 
			USGSDataResolution.OneNinthArcSecond => "1/9 arc second 3m 3 meter", 
			USGSDataResolution.FiveMeter => "5 meter 5m", 
			USGSDataResolution.OneThirdArcSecond => "1/3 arc second 10m 10 meter", 
			USGSDataResolution.OneArcSecond => "1 arc second 30m 30 meter", 
			USGSDataResolution.TwoArcSecond => "2 arc second 60m 60 meter", 
			USGSDataResolution.Original => "Original", 
			USGSDataResolution.Custom => $"{Meters:F0}m {Meters:F0} meter", 
			_ => "", 
		};
	}

	public int CompareTo(DataResolution other)
	{
		return Meters.CompareTo(other.Meters);
	}

	public bool Equals(DataResolution other)
	{
		if (Category == other.Category)
		{
			return Math.Abs(Meters - other.Meters) < 0.01;
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj is DataResolution other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return Meters.GetHashCode();
	}

	public override string ToString()
	{
		return ToDisplayString();
	}

	public static bool operator ==(DataResolution left, DataResolution right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(DataResolution left, DataResolution right)
	{
		return !left.Equals(right);
	}

	public static bool operator <(DataResolution left, DataResolution right)
	{
		return left.Meters < right.Meters;
	}

	public static bool operator >(DataResolution left, DataResolution right)
	{
		return left.Meters > right.Meters;
	}

	public static bool operator <=(DataResolution left, DataResolution right)
	{
		return left.Meters <= right.Meters;
	}

	public static bool operator >=(DataResolution left, DataResolution right)
	{
		return left.Meters >= right.Meters;
	}
}

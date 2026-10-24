using System;
using System.Diagnostics;
using Geospatial.IO.ORM;
using Geospatial.Vectors;
using Utility.ORM;
using Utility.Units;

namespace Ras.Layers.Meteorology;

[DebuggerDisplay("{Name}")]
public class MeteorologicStation
{
	public const float DefaultElevationSI = 20f;

	public const float DefaultElevationUSC = 70f;

	public const float NoDataElevation = float.NaN;

	[Metadata("Name")]
	public string Name { get; set; }

	[WrappedColumnName("$X=X,$Y=Y")]
	public Point Location { get; set; }

	[Metadata("Elevation")]
	public float ElevationOffGround { get; set; } = float.NaN;

	public MeteorologicStation()
	{
	}

	public MeteorologicStation(string name, Point loc)
	{
		Name = name;
		Location = loc;
	}

	public MeteorologicStation(string name, Point loc, float elev)
	{
		Name = name;
		Location = loc;
		ElevationOffGround = elev;
	}

	public float GetActualElevationOffGround(UnitSystem units)
	{
		if (!float.IsNaN(ElevationOffGround))
		{
			return ElevationOffGround;
		}
		return units switch
		{
			UnitSystem.USCustomary => 70f, 
			UnitSystem.SI => 20f, 
			_ => throw new NotSupportedException(), 
		};
	}

	public bool Matches(MeteorologicStation other)
	{
		if (Name == other.Name && Location == other.Location)
		{
			return ElevationOffGround == other.ElevationOffGround;
		}
		return false;
	}
}

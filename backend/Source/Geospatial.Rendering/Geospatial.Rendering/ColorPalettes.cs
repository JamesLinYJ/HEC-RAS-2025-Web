using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Utility.Extensions;
using Utility.Units;

namespace Geospatial.Rendering;

public static class ColorPalettes
{
	public static ColorPalette OGTerrain = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 102, 205, 170),
		Color.FromArgb(255, 255, 255, 0),
		Color.FromArgb(255, 0, 128, 0),
		Color.FromArgb(255, 255, 165, 0),
		Color.FromArgb(255, 139, 0, 0),
		Color.FromArgb(255, 165, 42, 42),
		Color.FromArgb(255, 128, 128, 128),
		Color.FromArgb(255, 255, 250, 250)
	}, "Terrain Classic");

	public static ColorPalette Terrain = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 153, 245, 255),
		Color.FromArgb(255, 68, 224, 68),
		Color.FromArgb(255, 0, 128, 0),
		Color.FromArgb(255, 255, 255, 0),
		Color.FromArgb(255, 255, 165, 0),
		Color.FromArgb(255, 139, 0, 0),
		Color.FromArgb(255, 164, 42, 42),
		Color.FromArgb(255, 128, 128, 128),
		Color.FromArgb(255, 255, 255, 255)
	}, "Terrain");

	public static ColorPalette WaterSurface = new ColorPalette(new List<Color>
	{
		Color.Chartreuse,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red,
		Color.Pink,
		Color.Magenta
	}, "Water Surface");

	public static ColorPalette Depths = new ColorPalette(new List<Color>
	{
		Color.Teal,
		Color.Blue
	}, "Depths");

	public static ColorPalette Grayscale = new ColorPalette(new List<Color>
	{
		Color.White,
		Color.Black
	}, "Grayscale");

	public static ColorPalette Velocities = new ColorPalette(new List<Color>
	{
		Color.DarkBlue,
		Color.Blue,
		Color.LightGreen,
		Color.Yellow,
		Color.Orange,
		Color.OrangeRed,
		Color.DarkRed
	}, "Velocities");

	public static ColorPalette EnergyDepth = new ColorPalette(new List<Color>
	{
		Color.LightGreen,
		Color.DarkGreen
	}, "Energy Depth");

	public static ColorPalette WaterSurfaceElevation = new ColorPalette(new List<Color>
	{
		Color.Chartreuse,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red,
		Color.Pink,
		Color.Magenta
	}, "Water Surface Elevation");

	public static ColorPalette WSError = new ColorPalette(new List<Color>
	{
		Color.FromArgb(0, 200, 190, 190),
		Color.FromArgb(255, 226, 140, 140),
		Color.FromArgb(255, 240, 70, 70),
		Color.FromArgb(255, 255, 0, 0),
		Color.FromArgb(255, 160, 0, 0)
	}, "Water Surface Error");

	public static ColorPalette Courant = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 0, 128, 64),
		Color.FromArgb(255, 112, 239, 120),
		Color.FromArgb(255, 168, 228, 0),
		Color.FromArgb(255, 228, 210, 0),
		Color.FromArgb(255, 255, 166, 0),
		Color.FromArgb(255, 254, 106, 0),
		Color.FromArgb(255, 250, 61, 0),
		Color.FromArgb(255, 241, 34, 0),
		Color.FromArgb(255, 215, 0, 0)
	}, "Courant");

	public static ColorPalette ArrivalTime = new ColorPalette(new List<Color>
	{
		Color.Red,
		Color.IndianRed,
		Color.Orange,
		Color.Yellow,
		Color.LightGreen,
		Color.Green
	}, "Arrival Time");

	public static ColorPalette Duration = new ColorPalette(new List<Color>
	{
		Color.LightGreen,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.IndianRed,
		Color.Red
	}, "Duration");

	public static ColorPalette Shear = new ColorPalette(new List<Color>
	{
		Color.PaleGreen,
		Color.Tan,
		Color.Yellow,
		Color.Orange,
		Color.IndianRed,
		Color.Red
	}, "Shear");

	public static ColorPalette StreamPower = new ColorPalette(new List<Color>
	{
		Color.PaleGreen,
		Color.Tan,
		Color.Yellow,
		Color.Orange,
		Color.IndianRed,
		Color.Red
	}, "Stream Power");

	public static ColorPalette Ice = new ColorPalette(new List<Color>
	{
		Color.LightGray,
		Color.SteelBlue
	}, "Ice");

	public static ColorPalette Hazard = new ColorPalette(new List<Color>
	{
		Color.Blue,
		Color.RoyalBlue,
		Color.Cyan,
		Color.LawnGreen,
		Color.LightGreen,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red,
		Color.HotPink,
		Color.Magenta
	}, "Hazard");

	public static ColorPalette DepthForecast = new ColorPalette(new List<Color>
	{
		Color.LightBlue,
		Color.RoyalBlue,
		Color.Yellow,
		Color.Red
	}, "Depth Forecast");

	public static ColorPalette Weather = new ColorPalette(new List<Color>
	{
		Color.LawnGreen,
		Color.LightGreen,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red,
		Color.Magenta,
		Color.HotPink,
		Color.Cyan,
		Color.RoyalBlue
	}, "Weather");

	public static ColorPalette ManningsN = new ColorPalette(new List<Color>
	{
		Color.Cyan,
		Color.DeepSkyBlue,
		Color.Blue,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red
	}, "Mannings N");

	public static ColorPalette Rainbow = new ColorPalette(new List<Color>
	{
		Color.Violet,
		Color.Indigo,
		Color.Blue,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red
	}, "Rainbow");

	public static ColorPalette GrayToRed = new ColorPalette(new List<Color>
	{
		Color.LightGray,
		Color.Red
	}, "Gray To Red");

	public static ColorPalette Interpolation = new ColorPalette(new List<Color>
	{
		Color.White,
		Color.Black
	}, "Interpolation");

	public static ColorPalette InterpolationM = new ColorPalette(new List<Color>
	{
		Color.Gray,
		Color.Red,
		Color.Blue,
		Color.Red,
		Color.Gray
	}, "Interpolation M");

	public static ColorPalette BBGYOR = new ColorPalette(new List<Color>
	{
		Color.Cyan,
		Color.Blue,
		Color.Green,
		Color.Yellow,
		Color.Orange,
		Color.Red
	}, "Cyan to Red");

	public static ColorPalette RedGreen = new ColorPalette(new List<Color>
	{
		Color.Red,
		Color.Orange,
		Color.Yellow,
		Color.Blue,
		Color.Cyan,
		Color.Green,
		Color.LightGreen
	}, "Red To Green");

	public static ColorPalette GreenRed = new ColorPalette(new List<Color>
	{
		Color.LightGreen,
		Color.Green,
		Color.Cyan,
		Color.Blue,
		Color.Yellow,
		Color.Orange,
		Color.Red
	}, "Green To Red");

	public static ColorPalette GraytoRed = new ColorPalette(new List<Color>
	{
		Color.Gray,
		Color.Yellow,
		Color.Orange,
		Color.Red
	}, "Gray To Red");

	public static ColorPalette Precipitation = new ColorPalette(new List<Color>
	{
		Color.FromArgb(0, 153, 153, 153),
		Color.FromArgb(255, 0, 255, 129),
		Color.FromArgb(255, 0, 198, 85),
		Color.FromArgb(255, 0, 165, 67),
		Color.FromArgb(255, 0, 137, 55),
		Color.FromArgb(255, 0, 108, 46),
		Color.FromArgb(255, 0, 94, 41),
		Color.FromArgb(255, 255, 246, 1),
		Color.FromArgb(255, 248, 184, 0),
		Color.FromArgb(255, 255, 138, 42),
		Color.FromArgb(255, 245, 99, 0),
		Color.FromArgb(255, 232, 0, 0),
		Color.FromArgb(255, 178, 0, 0),
		Color.FromArgb(255, 235, 0, 139),
		Color.FromArgb(255, 169, 0, 203),
		Color.FromArgb(255, 130, 2, 138),
		Color.FromArgb(255, 100, 0, 99)
	}, "Precipitation");

	public static ColorPalette PrecipitationCumulative = new ColorPalette(new List<Color>
	{
		Color.FromArgb(0, 153, 153, 153),
		Color.FromArgb(255, 75, 210, 247),
		Color.FromArgb(255, 118, 160, 198),
		Color.FromArgb(255, 60, 75, 172),
		Color.FromArgb(255, 60, 247, 75),
		Color.FromArgb(255, 60, 180, 71),
		Color.FromArgb(255, 60, 135, 67),
		Color.FromArgb(255, 247, 247, 60),
		Color.FromArgb(255, 251, 222, 136),
		Color.FromArgb(255, 247, 172, 60),
		Color.FromArgb(255, 247, 60, 60),
		Color.FromArgb(255, 191, 60, 60),
		Color.FromArgb(255, 153, 60, 60),
		Color.FromArgb(255, 247, 60, 247),
		Color.FromArgb(255, 153, 116, 228)
	}, "Precipitation Cumulative");

	public static ColorPalette PrecipitationIncremental = new ColorPalette(new List<Color>
	{
		Color.FromArgb(0, 153, 153, 153),
		Color.FromArgb(255, 0, 255, 129),
		Color.FromArgb(255, 0, 198, 85),
		Color.FromArgb(255, 0, 165, 67),
		Color.FromArgb(255, 0, 137, 55),
		Color.FromArgb(255, 0, 108, 46),
		Color.FromArgb(255, 0, 94, 41),
		Color.FromArgb(255, 255, 246, 1),
		Color.FromArgb(255, 248, 184, 0),
		Color.FromArgb(255, 255, 138, 42),
		Color.FromArgb(255, 245, 99, 0),
		Color.FromArgb(255, 232, 0, 0),
		Color.FromArgb(255, 178, 0, 0),
		Color.FromArgb(255, 235, 0, 139),
		Color.FromArgb(255, 169, 0, 203),
		Color.FromArgb(255, 130, 2, 138),
		Color.FromArgb(255, 100, 0, 99)
	}, "Precipitation Incremental");

	public static ColorPalette BedChange = new ColorPalette(new List<Color>
	{
		Color.Red,
		Color.White,
		Color.Blue
	}, "Bed Change");

	public static ColorPalette SedimentConcentration = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 47, 128, 250),
		Color.FromArgb(255, 39, 151, 235),
		Color.FromArgb(255, 28, 170, 223),
		Color.FromArgb(255, 0, 185, 199),
		Color.FromArgb(255, 41, 195, 170),
		Color.FromArgb(255, 72, 203, 134),
		Color.FromArgb(255, 129, 204, 89),
		Color.FromArgb(255, 187, 196, 47),
		Color.FromArgb(255, 234, 186, 48),
		Color.FromArgb(255, 254, 199, 53),
		Color.FromArgb(255, 245, 238, 33)
	}, "Sediment Concentration");

	public static ColorPalette SedimentConcentrationAlt = new ColorPalette(new List<Color>
	{
		Color.FromArgb(200, 128, 255, 255),
		Color.FromArgb(255, 211, 138, 3),
		Color.FromArgb(255, 172, 113, 2),
		Color.FromArgb(255, 111, 72, 2),
		Color.FromArgb(255, 71, 47, 1),
		Color.FromArgb(255, 34, 22, 0)
	}, "Sediment Concentration Alt");

	public static ColorPalette Mud = new ColorPalette(new List<Color>
	{
		Color.FromArgb(200, 250, 209, 88),
		Color.FromArgb(255, 211, 138, 3),
		Color.FromArgb(255, 172, 113, 2),
		Color.FromArgb(255, 111, 72, 2),
		Color.FromArgb(255, 71, 47, 1),
		Color.FromArgb(255, 34, 22, 0)
	}, "Mud");

	public static ColorPalette WindSpeed = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 204, 204, 204),
		Color.FromArgb(255, 102, 102, 102),
		Color.FromArgb(255, 197, 96, 255),
		Color.FromArgb(255, 118, 64, 255),
		Color.FromArgb(255, 0, 0, 255),
		Color.FromArgb(255, 1, 255, 197),
		Color.FromArgb(255, 1, 255, 0),
		Color.FromArgb(255, 185, 255, 0),
		Color.FromArgb(255, 239, 255, 0),
		Color.FromArgb(255, 255, 239, 0),
		Color.FromArgb(255, 255, 210, 0),
		Color.FromArgb(255, 255, 180, 0),
		Color.FromArgb(255, 255, 149, 0),
		Color.FromArgb(255, 255, 108, 0),
		Color.FromArgb(255, 255, 90, 0),
		Color.FromArgb(255, 255, 71, 0),
		Color.FromArgb(255, 255, 35, 0),
		Color.FromArgb(255, 255, 0, 0),
		Color.FromArgb(255, 197, 0, 0),
		Color.FromArgb(255, 149, 0, 0),
		Color.FromArgb(255, 109, 0, 0),
		Color.FromArgb(255, 154, 0, 40),
		Color.FromArgb(255, 159, 0, 59),
		Color.FromArgb(255, 197, 0, 73),
		Color.FromArgb(255, 255, 0, 255)
	}, "Wind Speed");

	public static ColorPalette AirTemperature = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 240, 140, 220),
		Color.FromArgb(255, 220, 80, 200),
		Color.FromArgb(255, 160, 20, 180),
		Color.FromArgb(255, 120, 60, 200),
		Color.FromArgb(255, 10, 100, 240),
		Color.FromArgb(255, 0, 200, 255),
		Color.FromArgb(255, 0, 200, 160),
		Color.FromArgb(255, 0, 200, 20),
		Color.FromArgb(255, 170, 225, 0),
		Color.FromArgb(255, 250, 245, 0),
		Color.Orange,
		Color.Red,
		Color.DarkRed
	}, "Air Temperature");

	public static ColorPalette NLCD = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 84, 117, 168),
		Color.FromArgb(255, 255, 255, 255),
		Color.FromArgb(255, 232, 209, 209),
		Color.FromArgb(255, 226, 158, 140),
		Color.FromArgb(255, 255, 0, 0),
		Color.FromArgb(255, 181, 0, 0),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 133, 199, 126),
		Color.FromArgb(255, 56, 129, 78),
		Color.FromArgb(255, 212, 231, 176),
		Color.FromArgb(255, 175, 150, 60),
		Color.FromArgb(255, 220, 202, 143),
		Color.FromArgb(255, 253, 233, 170),
		Color.FromArgb(255, 209, 209, 130),
		Color.FromArgb(255, 163, 204, 81),
		Color.FromArgb(255, 130, 186, 158),
		Color.FromArgb(255, 251, 246, 93),
		Color.FromArgb(255, 202, 145, 70),
		Color.FromArgb(255, 200, 230, 248),
		Color.FromArgb(255, 100, 179, 203)
	}, "NLCD");

	public static List<int> NLCDCategories = new List<int>
	{
		11, 12, 21, 22, 23, 24, 31, 41, 42, 43,
		51, 52, 71, 72, 73, 74, 81, 82, 90, 95
	};

	public static ColorPalette AndersonII = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 226, 158, 140),
		Color.FromArgb(255, 255, 0, 0),
		Color.FromArgb(255, 181, 0, 0),
		Color.FromArgb(255, 0, 0, 0),
		Color.FromArgb(255, 181, 0, 0),
		Color.FromArgb(255, 226, 158, 140),
		Color.FromArgb(255, 232, 209, 209),
		Color.FromArgb(255, 251, 246, 93),
		Color.FromArgb(255, 202, 145, 70),
		Color.FromArgb(255, 251, 246, 93),
		Color.FromArgb(255, 225, 200, 80),
		Color.FromArgb(255, 253, 233, 170),
		Color.FromArgb(255, 220, 202, 143),
		Color.FromArgb(255, 235, 210, 155),
		Color.FromArgb(255, 133, 199, 126),
		Color.FromArgb(255, 56, 129, 78),
		Color.FromArgb(255, 212, 231, 176),
		Color.FromArgb(255, 84, 117, 168),
		Color.FromArgb(255, 84, 117, 168),
		Color.FromArgb(255, 84, 117, 168),
		Color.FromArgb(255, 84, 117, 168),
		Color.FromArgb(255, 200, 230, 248),
		Color.FromArgb(255, 100, 179, 213),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 170, 186, 158),
		Color.FromArgb(255, 130, 210, 158),
		Color.FromArgb(255, 210, 205, 192),
		Color.FromArgb(255, 130, 186, 220),
		Color.FromArgb(255, 130, 186, 158),
		Color.FromArgb(255, 255, 255, 255),
		Color.FromArgb(255, 255, 255, 255)
	}, "Anderson II");

	public static ColorPalette CCAP = new ColorPalette(new List<Color>
	{
		Color.FromArgb(255, 0, 0, 0),
		Color.FromArgb(255, 255, 255, 255),
		Color.FromArgb(255, 194, 194, 194),
		Color.FromArgb(255, 151, 151, 151),
		Color.FromArgb(255, 204, 204, 0),
		Color.FromArgb(255, 84, 33, 0),
		Color.FromArgb(255, 193, 160, 79),
		Color.FromArgb(255, 242, 186, 135),
		Color.FromArgb(255, 0, 242, 0),
		Color.FromArgb(255, 0, 58, 0),
		Color.FromArgb(255, 7, 160, 58),
		Color.FromArgb(255, 109, 109, 0),
		Color.FromArgb(255, 242, 242, 0),
		Color.FromArgb(255, 249, 209, 0),
		Color.FromArgb(255, 170, 249, 239),
		Color.FromArgb(255, 0, 91, 91),
		Color.FromArgb(255, 242, 109, 248),
		Color.FromArgb(255, 242, 0, 242),
		Color.FromArgb(255, 61, 0, 61),
		Color.FromArgb(255, 109, 0, 109),
		Color.FromArgb(255, 175, 0, 175),
		Color.FromArgb(255, 0, 242, 242),
		Color.FromArgb(255, 0, 0, 119),
		Color.FromArgb(255, 0, 0, 242),
		Color.FromArgb(102, 117, 219)
	}, "CCAP");

	public static ColorPalette AirPressure;

	public static ColorPalette CumulativeIterations;

	public static IReadOnlyList<string> Names => PaletteFields().Select(DisplayName).OrderBy((string n) => n, StringComparer.Ordinal).ToList();

	private static string DisplayName(FieldInfo f)
	{
		ColorPalette colorPalette = (ColorPalette)f.GetValue(null);
		if (!string.IsNullOrWhiteSpace(colorPalette?.Name))
		{
			return colorPalette.Name;
		}
		return f.Name;
	}

	public static bool TryGet(string name, out ColorPalette palette)
	{
		palette = null;
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		foreach (FieldInfo paletteField in PaletteFields())
		{
			ColorPalette colorPalette = (ColorPalette)paletteField.GetValue(null);
			if (paletteField.Name.RoughlyEquals(name) || colorPalette?.Name?.RoughlyEquals(name) == true)
			{
				palette = colorPalette;
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<FieldInfo> PaletteFields()
	{
		return from f in typeof(ColorPalettes).GetFields(BindingFlags.Static | BindingFlags.Public)
			where f.FieldType == typeof(ColorPalette)
			select f;
	}

	public static List<ColorPalette> GetAllPalettes()
	{
		List<ColorPalette> colorPalettes = new List<ColorPalette>();
		FieldInfo[] fields = typeof(ColorPalettes).GetFields();
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.IsStatic && fieldInfo.FieldType == typeof(ColorPalette))
			{
				ColorPalette getValue = fieldInfo.GetValue(null) as ColorPalette;
				colorPalettes.Add(getValue);
			}
		}
		return colorPalettes;
	}

	public static List<InterpolatedSurfaceFill> GetValuesByUnit(UnitSystem units)
	{
		List<InterpolatedSurfaceFill> interpolatedSurfaceFills = new List<InterpolatedSurfaceFill>();
		FieldInfo[] fields = ((units == UnitSystem.USCustomary) ? typeof(USCustomarySurfaceFills) : typeof(MetricSurfaceFills)).GetFields();
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.IsStatic && fieldInfo.FieldType == typeof(InterpolatedSurfaceFill) && fieldInfo.GetValue(null) is InterpolatedSurfaceFill interpolatedSurfaceFill)
			{
				interpolatedSurfaceFills.Add(interpolatedSurfaceFill);
			}
		}
		fields = typeof(SurfaceFills).GetFields();
		foreach (FieldInfo fieldInfo2 in fields)
		{
			if (fieldInfo2.IsStatic && fieldInfo2.FieldType == typeof(InterpolatedSurfaceFill) && fieldInfo2.GetValue(null) is InterpolatedSurfaceFill interpolatedSurfaceFill2)
			{
				interpolatedSurfaceFills.Add(interpolatedSurfaceFill2);
			}
		}
		return interpolatedSurfaceFills;
	}

	static ColorPalettes()
	{
		int ordinal = 4;
		List<Color> colors = new List<Color>(ordinal);
		CollectionsMarshal.SetCount(colors, ordinal);
		Span<Color> colorsSpan = CollectionsMarshal.AsSpan(colors);
		int ordinal2 = 0;
		colorsSpan[ordinal2] = Color.FromArgb(255, Color.Blue);
		ordinal2++;
		colorsSpan[ordinal2] = Color.FromArgb(255, Color.Green);
		ordinal2++;
		colorsSpan[ordinal2] = Color.FromArgb(255, Color.Yellow);
		ordinal2++;
		colorsSpan[ordinal2] = Color.FromArgb(255, Color.Red);
		AirPressure = new ColorPalette(colors);
		CumulativeIterations = new ColorPalette(new List<Color>
		{
			Color.LightGreen,
			Color.Yellow,
			Color.Orange,
			Color.OrangeRed,
			Color.DarkRed
		}, "Cumulative Iterations");
	}
}

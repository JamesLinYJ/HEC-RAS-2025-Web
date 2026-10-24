using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utility.Extensions;

namespace Geospatial.Rendering;

public static class SurfaceFills
{
	public static InterpolatedSurfaceFill CourantValues = new InterpolatedSurfaceFill(ColorPalettes.Courant, new _003C_003Ez__ReadOnlyArray<double>(new double[9] { 0.0, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 }), isStretched: true, regenForScreen: false, "Courant");

	public static InterpolatedSurfaceFill ArrivalTimeValues = new InterpolatedSurfaceFill(ColorPalettes.ArrivalTime, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 1.0, 2.0, 4.0, 6.0, 8.0 }), isStretched: true, regenForScreen: false, "Arrival Time");

	public static InterpolatedSurfaceFill StreamPowerValues = new InterpolatedSurfaceFill(ColorPalettes.StreamPower, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 2.0, 5.0, 10.0, 25.0, 50.0 }), isStretched: true, regenForScreen: false, "Stream Power");

	public static InterpolatedSurfaceFill HazardValues = new InterpolatedSurfaceFill(ColorPalettes.Hazard, new _003C_003Ez__ReadOnlyArray<double>(new double[11]
	{
		0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0,
		10.0
	}), isStretched: true, regenForScreen: false, "Hazard");

	public static InterpolatedSurfaceFill ManningsNValues = new InterpolatedSurfaceFill(ColorPalettes.ManningsN, new _003C_003Ez__ReadOnlyArray<double>(new double[7] { 0.02, 0.03, 0.05, 0.06, 0.08, 0.12, 0.16 }), isStretched: true, regenForScreen: false, "Mannings N");

	public static InterpolatedSurfaceFill RainbowValues = new InterpolatedSurfaceFill(ColorPalettes.Rainbow, new _003C_003Ez__ReadOnlyArray<double>(new double[7] { 0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0 }), isStretched: true, regenForScreen: false, "Rainbow");

	public static InterpolatedSurfaceFill GrayRedValues = new InterpolatedSurfaceFill(ColorPalettes.GrayToRed, new _003C_003Ez__ReadOnlyArray<double>(new double[2] { 0.0, 100.0 }), isStretched: true, regenForScreen: false, "Gray To Red");

	public static InterpolatedSurfaceFill BedChangeValues = new InterpolatedSurfaceFill(ColorPalettes.BedChange, new _003C_003Ez__ReadOnlyArray<double>(new double[3] { -3.0, 0.0, 3.0 }), isStretched: true, regenForScreen: false, "Bed Change");

	public static InterpolatedSurfaceFill SedimentConcentrationValues = new InterpolatedSurfaceFill(ColorPalettes.SedimentConcentration, new _003C_003Ez__ReadOnlyArray<double>(new double[11]
	{
		0.0, 2.0, 5.0, 10.0, 20.0, 30.0, 50.0, 100.0, 200.0, 300.0,
		500.0
	}), isStretched: true, regenForScreen: false, "Sediment Concentration");

	public static InterpolatedSurfaceFill SedimentConcentrationAltValues = new InterpolatedSurfaceFill(ColorPalettes.SedimentConcentrationAlt, new _003C_003Ez__ReadOnlyArray<double>(new double[6] { 0.0, 5.0, 10.0, 50.0, 100.0, 500.0 }), isStretched: true, regenForScreen: false, "Sediment Concentration Alt");

	public static InterpolatedSurfaceFill NLCDValues = new InterpolatedSurfaceFill(ColorPalettes.NLCD, new _003C_003Ez__ReadOnlyArray<double>(new double[20]
	{
		11.0, 12.0, 21.0, 22.0, 23.0, 24.0, 31.0, 41.0, 42.0, 43.0,
		51.0, 52.0, 71.0, 72.0, 73.0, 74.0, 81.0, 82.0, 90.0, 95.0
	}), isStretched: true, regenForScreen: false, "NLCD");

	public static InterpolatedSurfaceFill AndersonIIValues = new InterpolatedSurfaceFill(ColorPalettes.AndersonII, new _003C_003Ez__ReadOnlyArray<double>(new double[37]
	{
		11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 17.0, 21.0, 22.0, 23.0,
		24.0, 31.0, 32.0, 33.0, 41.0, 42.0, 43.0, 51.0, 52.0, 53.0,
		54.0, 61.0, 62.0, 71.0, 72.0, 73.0, 74.0, 75.0, 76.0, 77.0,
		81.0, 82.0, 83.0, 84.0, 85.0, 91.0, 92.0
	}), isStretched: true, regenForScreen: false, "Anderson II");

	public static InterpolatedSurfaceFill CCAPValues = new InterpolatedSurfaceFill(ColorPalettes.CCAP, Enumerable.Range(1, 23).Select((Func<int, double>)((int i) => i)).ToArray(), isStretched: true, regenForScreen: false, "CCAP");

	public static InterpolatedSurfaceFill WindSpeedValues = new InterpolatedSurfaceFill(ColorPalettes.WindSpeed, Enumerable.Range(0, ColorPalettes.WindSpeed.Colors.Count).Select((Func<int, double>)((int i) => i)).ToArray(), isStretched: true, regenForScreen: false, "Wind Speed");

	public static InterpolatedSurfaceFill PrecipInches = new InterpolatedSurfaceFill(ColorPalettes.Precipitation, new _003C_003Ez__ReadOnlyArray<double>(new double[17]
	{
		0.01, 0.1, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0,
		7.5, 10.0, 12.5, 15.0, 20.0, 25.0, 30.0
	}), isStretched: true, regenForScreen: false, "Precip Inches")
	{
		RenderZeroAsNoData = true
	};

	public static InterpolatedSurfaceFill PrecipMillimeters = new InterpolatedSurfaceFill(ColorPalettes.Precipitation, new _003C_003Ez__ReadOnlyArray<double>(new double[17]
	{
		0.254, 2.54, 12.7, 25.4, 38.1, 50.8, 63.5, 76.2, 101.0, 127.0,
		191.0, 254.0, 318.0, 381.0, 508.0, 635.0, 762.0
	}), isStretched: true, regenForScreen: false, "Precip Millimeters")
	{
		RenderZeroAsNoData = true
	};

	public static InterpolatedSurfaceFill AirPressures = new InterpolatedSurfaceFill(ColorPalettes.AirPressure, new _003C_003Ez__ReadOnlyArray<double>(new double[4] { 950.0, 987.5, 1025.0, 1100.0 }), isStretched: true, regenForScreen: false, "Air Pressure");

	public static IReadOnlyList<string> Names => Fields().Select(DisplayName).OrderBy((string n) => n, StringComparer.Ordinal).ToList();

	private static string DisplayName(FieldInfo f)
	{
		InterpolatedSurfaceFill interpolatedSurfaceFill = (InterpolatedSurfaceFill)f.GetValue(null);
		if (!string.IsNullOrWhiteSpace(interpolatedSurfaceFill?.Name))
		{
			return interpolatedSurfaceFill.Name;
		}
		return f.Name;
	}

	public static bool TryGet(string name, out InterpolatedSurfaceFill fill)
	{
		fill = null;
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		foreach (FieldInfo fieldInfo in Fields())
		{
			InterpolatedSurfaceFill interpolatedSurfaceFill = (InterpolatedSurfaceFill)fieldInfo.GetValue(null);
			if (fieldInfo.Name.RoughlyEquals(name) || interpolatedSurfaceFill?.Name?.RoughlyEquals(name) == true)
			{
				fill = interpolatedSurfaceFill;
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<FieldInfo> Fields()
	{
		return from f in typeof(SurfaceFills).GetFields(BindingFlags.Static | BindingFlags.Public)
			where typeof(InterpolatedSurfaceFill).IsAssignableFrom(f.FieldType)
			select f;
	}
}

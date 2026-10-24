using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
public static class IHasRRRSLayerExtensions
{
	public static string GetRiverName<T>(this T lyr, int fid) where T : FeatureLayer
,IHasRRRSLayer	{
		return DataTableExtensions.GetString(lyr.FeatureTable(), fid, "River");
	}

	public static void SetRiverName<T>(this T lyr, int fID, string rivName) where T : FeatureLayer
,IHasRRRSLayer	{
		lyr.FeatureRow(fID)["River"] = rivName;
	}

	public static string GetReachName<T>(this T lyr, int fid) where T : FeatureLayer
,IHasRRRSLayer	{
		return DataTableExtensions.GetString(lyr.FeatureTable(), fid, "Reach");
	}

	public static void SetReachName<T>(this T lyr, int fID, string rchName) where T : FeatureLayer
,IHasRRRSLayer	{
		lyr.FeatureRow(fID)["Reach"] = rchName;
	}

	public static string GetRiverStationName<T>(this T lyr, int fID) where T : FeatureLayer
,IHasRRRSLayer	{
		return DataTableExtensions.GetString(lyr.FeatureTable(), fID, "River Station");
	}

	public static void SetRiverStationName<T>(this T lyr, int fID, string rsName) where T : FeatureLayer
,IHasRRRSLayer	{
		lyr.FeatureRow(fID)["River Station"] = rsName;
	}

	public static double GetRiverStationComputed<T>(this T lyr, int fid) where T : FeatureLayer
,IHasRRRSLayer	{
		return DataTableExtensions.GetDouble(lyr.FeatureTable(), fid, "ComputedUnroundedRS");
	}

	public static void SetRiverStationComputed<T>(this T lyr, int fID, double rs) where T : FeatureLayer
,IHasRRRSLayer	{
		lyr.FeatureRow(fID)["ComputedUnroundedRS"] = rs;
	}

	public static int GetRoundingDecimalPlaces<T>(this T lyr) where T : FeatureLayer
,IHasRRRSLayer	{
		return lyr.RiverStationDecimalPlaces;
	}

	public static void SetRoundingDecimalPlaces<T>(this T lyr, int decimalPlaces) where T : FeatureLayer
,IHasRRRSLayer	{
		lyr.RiverStationDecimalPlaces = decimalPlaces;
	}

	public static SharedData.EnumRSUnits GetRoundingUnits<T>(this T lyr) where T : FeatureLayer
,IHasRRRSLayer	{
		return lyr.UnitsRiverStation;
	}

	public static void SetRoundingUnits<T>(this T lyr, SharedData.EnumRSUnits unit) where T : FeatureLayer
,IHasRRRSLayer	{
		lyr.UnitsRiverStation = unit;
	}

	public static void ClearRRRSColumns<T>(this T lyr, int fID) where T : FeatureLayer
,IHasRRRSLayer	{
		SetRiverName(lyr, fID, "");
		SetReachName(lyr, fID, "");
		SetRiverStationName(lyr, fID, "");
		SetRiverStationComputed(lyr, fID, double.NaN);
	}
}

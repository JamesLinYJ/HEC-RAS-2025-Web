using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

[StandardModule]
internal static class EnforceBreaklineLayerExtensions
{
	public static void InitializeBreaklineColumns<T>(this T lyr) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		DataColumnGroup dataColumnGroup = new DataColumnGroup("Breakline Spacing", isVisible: true);
		lyr.AddManagedAttributeColumn(lyr.NearSpacingColumnName, lyr.NearSpacingDiskName, typeof(float), dataColumnGroup);
		lyr.AddManagedAttributeColumn(lyr.NearRepeatsColumnName, lyr.NearRepeatsDiskName, typeof(int), dataColumnGroup);
		lyr.AddManagedAttributeColumn(lyr.FarSpacingColumnName, lyr.FarSpacingDiskName, typeof(float), dataColumnGroup);
		lyr.AddManagedAttributeColumn("Enforce 1 Cell Protection Radius", "Protection Radius", typeof(bool), dataColumnGroup);
	}

	public static float GetNearSpacing<T>(this T lyr, int fid) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		return DataTableExtensions.GetSingle(lyr.FeatureTable(), fid, lyr.NearSpacingColumnName);
	}

	public static void SetNearSpacing<T>(this T lyr, int fid, float spacing) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		if (!(spacing < 0f))
		{
			lyr.FeatureRow(fid)[lyr.NearSpacingColumnName] = spacing;
		}
	}

	public static int GetNearRepeats<T>(this T lyr, int fid) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		return DataTableExtensions.GetInteger(lyr.FeatureTable(), fid, lyr.NearRepeatsColumnName);
	}

	public static void SetNearRepeats<T>(this T lyr, int fid, int repeats) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		lyr.FeatureRow(fid)[lyr.NearRepeatsColumnName] = repeats;
	}

	public static float GetFarSpacing<T>(this T lyr, int fid) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		return DataTableExtensions.GetSingle(lyr.FeatureTable(), fid, lyr.FarSpacingColumnName);
	}

	public static void SetFarSpacing<T>(this T lyr, int fid, float spacing) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		if (!(spacing < 0f))
		{
			lyr.FeatureRow(fid)[lyr.FarSpacingColumnName] = spacing;
		}
	}

	public static bool GetEnforceOneCellProtectionRadius<T>(this T lyr, int fid) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		return DataTableExtensions.GetBoolean(lyr.FeatureTable(), fid, "Enforce 1 Cell Protection Radius");
	}

	public static void SetEnforceOneCellProtectionRadius<T>(this T lyr, int fid, bool enforceOneCellProtectionRadius) where T : FeatureLayer, IEnforceBreaklineLayer
	{
		lyr.FeatureRow(fid)["Enforce 1 Cell Protection Radius"] = enforceOneCellProtectionRadius;
	}
}
